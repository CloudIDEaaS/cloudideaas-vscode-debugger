using AngleSharp.Text;
using Microsoft.Build.Construction;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utils;
using Utils.Kestrel;
using VSCodeDebugger;
using VSCodeDebugger.CommandHandlers;
using VSCodeDebugger.Models;
using static ScintillaNET.Style;

namespace ChromeDebugger
{
    public class StandardStreamService : BaseStandardStreamService<DapCommandPacket>, ILogWriter
    {
        private DirectoryInfo logsDirectory;
        private LogWriter logWriter;
        private string assemblyLoadLogPath;
        private LogWriter assemblyLoadLogWriter;
        private string dapMessagesLogPath;
        private LogWriter dapMessagesLogWriter;
        private string cdpMessagesLogPath;
        private LogWriter cdpMessagesLogWriter;
        private CancellationTokenSource cdpCancellationTokenSource;
        private CancellationTokenSource dotNetCancellationTokenSource;
        private Dictionary<string, object> vsCodeSettings;
        private Dictionary<string, object> launchSettings;
        private string userDataDirectory;
        private string debuggerLogDirectory;
        private int usedPort = -1;
        private string vsCodeDirectory;
        private int nextSequence;
        private CdpConnection? cdpConnection;
        private Task dotNetTask;
        private bool relaunching;

        public StandardStreamService(LogWriter logWriter, string workingDirectory)
        {
            logsDirectory = new DirectoryInfo(Path.Combine(workingDirectory, @".vscode\logs"));

            cdpCancellationTokenSource = new CancellationTokenSource();
            dotNetCancellationTokenSource = new CancellationTokenSource();

            this.logWriter = logWriter;
            this.currentWorkingDirectory = workingDirectory;

            if (!logsDirectory.Exists)
            {
                logsDirectory.Create();
            }

            dapMessagesLogPath = Path.Combine(logsDirectory.FullName, DateTime.Now.ToSortableShortDateTimeText() + "_VSCodeDebugger.DapMessages.log");
            dapMessagesLogWriter = new LogWriter(dapMessagesLogPath);

            cdpMessagesLogPath = Path.Combine(logsDirectory.FullName, DateTime.Now.ToSortableShortDateTimeText() + "_VSCodeDebugger.CdpMessages.log");
            cdpMessagesLogWriter = new LogWriter(cdpMessagesLogPath);

            assemblyLoadLogPath = Path.Combine(logsDirectory.FullName, DateTime.Now.ToSortableShortDateTimeText() + "_Assembly.log");
            assemblyLoadLogWriter = new LogWriter(assemblyLoadLogPath);

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            AppDomain.CurrentDomain.AssemblyLoad += (sender, e) =>
            {
                assemblyLoadLogWriter.WriteLine($"{e.LoadedAssembly.Location}");
            };
        }

        protected override async void HandleCommand(DapCommandPacket commandPacket)
        {
            if (commandPacket.Type != "request")
            {
                DebugUtils.Break();

                return;
            }

            WriteInputMessage(commandPacket);
            WriteLine($"Received command: {commandPacket.Command}");

            switch (commandPacket.Command)
            {
                case "initialize":
                    {
                        var initializeResponse = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                { "supportsConfigurationDoneRequest", true },
                                { "supportsFunctionBreakpoints", false },
                                { "supportsConditionalBreakpoints", true },
                                { "supportsHitConditionalBreakpoints", true },
                                { "supportsLogPoints", true },
                                { "supportsEvaluateForHovers", true },
                                { "supportsSetVariable", true },
                                { "supportsExceptionInfoRequest", true },
                                { "supportsLoadedSourcesRequest", true },
                                { "supportsBreakpointLocationsRequest", true },
                                { "supportsRestartFrame", true },
                                { "supportsTerminateRequest", true },
                                {
                                    "exceptionBreakpointFilters", new List<Dictionary<string, object>>
                                    {
                                        new Dictionary<string, object>
                                        {
                                            { "filter", "all" },
                                            { "label", "All Exceptions" },
                                            { "default", false }
                                        },
                                        new Dictionary<string, object>
                                        {
                                            { "filter", "uncaught" },
                                            { "label", "Uncaught Exceptions" },
                                            { "default", false }
                                        }
                                    }
                                }
                            }
                        };

                        this.vsCodeSettings = commandPacket.Arguments;

                        this.outputWriter.WriteJsonCommandT(initializeResponse, true, WriteOutputMessage);
                    }
                    break;
                case "launch":
                    {
                        string url;
                        string workspaceFolder;
                        DapEventPacket initializedEvent;
                        var launchResponse = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = "launch",
                            Success = true,
                            Body = new Dictionary<string, object>()
                        };

                        launchSettings = commandPacket.Arguments;
                        url = launchSettings["url"].ToString()!;
                        workspaceFolder = launchSettings["workspaceFolder"]?.ToString() ?? this.currentWorkingDirectory;

                        await HandleLaunchAsync(url, workspaceFolder);

                        this.outputWriter.WriteJsonCommandT(launchResponse, true, WriteOutputMessage);

                        initializedEvent = new DapEventPacket
                        {
                            Sequence = NextSequence(),
                            Event = "initialized"
                        };

                        this.outputWriter.WriteJsonCommandT(initializedEvent, true);
                    }
                    break;
                case "setBreakpoints":
                    {
                        var args = commandPacket.Arguments;
                        var source = args["source"] as JObject ?? throw new InvalidOperationException("Missing source.");
                        var breakpoints = args["breakpoints"] as JArray ?? new JArray();
                        var sourcePath = source["path"]?.ToString() ?? throw new InvalidOperationException("Missing source path.");
                        var responseBreakpoints = new List<Dictionary<string, object>>();
                        string browserUrl;
                        int line;
                        int column;
                        string? condition;
                        string? hitCondition;
                        string? logMessage;
                        string? chromeCondition;
                        bool verified;
                        int actualLine;
                        int actualColumn;
                        int dapBreakpointId;
                        JObject cdpResult;
                        DapResponsePacket response;

                        browserUrl = ConvertSourcePathToBrowserUrl(sourcePath, launchSettings["workspaceFolder"]?.ToString(), launchSettings["url"]?.ToString());

                        await cdpConnection.RemoveBreakpointsForSourceAsync(sourcePath);

                        foreach (var breakpoint in breakpoints)
                        {
                            line = breakpoint["line"]?.Value<int>() ?? throw new InvalidOperationException("Missing breakpoint line.");
                            column = breakpoint["column"]?.Value<int>() ?? 1;
                            condition = breakpoint["condition"]?.ToString();
                            hitCondition = breakpoint["hitCondition"]?.ToString();
                            logMessage = breakpoint["logMessage"]?.ToString();

                            chromeCondition = BuildBreakpointCondition(condition, hitCondition, logMessage);

                            var parameters = new JObject
                            {
                                { "url", browserUrl },
                                { "lineNumber", line - 1 },
                                { "columnNumber", Math.Max(column - 1, 0) }
                            };

                            if (!string.IsNullOrWhiteSpace(chromeCondition))
                            {
                                parameters["condition"] = chromeCondition;
                            }

                            cdpResult = await cdpConnection.SendCdpCommandAsync("Debugger.setBreakpointByUrl", parameters);

                            verified = false;
                            actualLine = line;
                            actualColumn = column;

                            var breakpointId = cdpResult["breakpointId"]?.ToString();
                            var locations = cdpResult["locations"] as JArray;

                            dapBreakpointId = 0;

                            if (!string.IsNullOrWhiteSpace(breakpointId))
                            {
                                dapBreakpointId = cdpConnection.RegisterBreakpoint(sourcePath, breakpointId);
                            }

                            if (locations != null && locations.Count > 0 && locations[0] is JObject location)
                            {
                                verified = true;

                                actualLine = location["lineNumber"]?.Value<int>() + 1 ?? line;
                                actualColumn = location["columnNumber"]?.Value<int>() + 1 ?? column;
                            }

                            responseBreakpoints.Add(new Dictionary<string, object>
                            {
                                { "id", dapBreakpointId },
                                { "verified", verified },
                                { "line", actualLine },
                                { "column", actualColumn }
                            });
                        }

                        response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                { "breakpoints", responseBreakpoints }
                            }
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "setExceptionBreakpoints":
                    {
                        var args = commandPacket.Arguments;
                        var filters = args["filters"] as JArray ?? new JArray();
                        var filterList = filters.Select(f => f?.ToString()).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
                        string pauseState;
                        DapResponsePacket response;

                        if (filterList.Contains("all"))
                        {
                            pauseState = "all";
                        }
                        else if (filterList.Contains("uncaught"))
                        {
                            pauseState = "uncaught";
                        }
                        else
                        {
                            pauseState = "none";
                        }

                        await cdpConnection.SendCdpCommandAsync("Debugger.setPauseOnExceptions", new
                        {
                            state = pauseState
                        });

                        response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>()
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "setVariable":
                    {
                        var args = commandPacket.Arguments;
                        var variablesReference = Convert.ToInt32(args["variablesReference"]);
                        var name = args["name"]?.ToString() ?? throw new InvalidOperationException("Missing variable name.");
                        var valueExpression = args["value"]?.ToString() ?? string.Empty;
                        var scope = cdpConnection.GetScopeReference(variablesReference);
                        JObject evaluateResult;
                        Dictionary<string, object> responseResult;

                        if (scope == null)
                        {
                            throw new InvalidOperationException($"Unknown variables reference {variablesReference}.");
                        }

                        evaluateResult = await cdpConnection.SendCdpCommandAsync("Runtime.evaluate", new
                        {
                            expression = valueExpression,
                            returnByValue = false
                        });

                        var newValue = evaluateResult["result"] as JObject ?? throw new InvalidOperationException("Unable to evaluate variable value.");

                        await cdpConnection.SendCdpCommandAsync("Debugger.setVariableValue", new
                        {
                            scopeNumber = scope.ScopeNumber,
                            variableName = name,
                            newValue = newValue,
                            callFrameId = scope.CallFrameId
                        });

                        responseResult = new Dictionary<string, object>
                        {
                            { "value", GetCdpValueText(newValue) },
                            { "variablesReference", 0 }
                        };

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = responseResult
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "exceptionInfo":
                    {
                        var exception = cdpConnection.GetCurrentException();
                        var exceptionId = exception?["className"]?.ToString() ?? exception?["description"]?.ToString() ?? "Exception";
                        var description = exception?["description"]?.ToString() ?? exception?["value"]?.ToString() ?? exceptionId;

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                { "exceptionId", exceptionId },
                                { "description", description },
                                { "breakMode", "always" }
                            }
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "loadedSources":
                    {
                        var sources = new List<Dictionary<string, object>>();

                        foreach (var script in cdpConnection.GetScripts())
                        {
                            if (string.IsNullOrWhiteSpace(script.Url))
                            {
                                continue;
                            }

                            var sourcePath = ConvertBrowserUrlToSourcePath(script.Url);

                            sources.Add(new Dictionary<string, object>
                            {
                                { "name", Path.GetFileName(sourcePath) },
                                { "path", sourcePath },
                                { "sourceReference", 0 }
                            });
                        }

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                { "sources", sources }
                            }
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "source":
                    {
                        var args = commandPacket.Arguments;
                        var sourceReference = Convert.ToInt32(args["sourceReference"]);
                        var scriptId = cdpConnection.GetScriptIdForSourceReference(sourceReference);

                        if (string.IsNullOrWhiteSpace(scriptId))
                        {
                            throw new InvalidOperationException($"Unknown source reference {sourceReference}.");
                        }

                        var cdpResult = await cdpConnection.SendCdpCommandAsync("Debugger.getScriptSource", new
                        {
                            scriptId = scriptId
                        });

                        var content = cdpResult["scriptSource"]?.ToString() ?? string.Empty;

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                { "content", content },
                                { "mimeType", "text/javascript" }
                            }
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "breakpointLocations":
                    {
                        var args = commandPacket.Arguments;
                        var source = args["source"] as JObject ?? throw new InvalidOperationException("Missing source.");
                        var sourcePath = source["path"]?.ToString() ?? throw new InvalidOperationException("Missing source path.");
                        var line = Convert.ToInt32(args["line"]);
                        var column = args.TryGetValue("column", out var columnValue) ? Convert.ToInt32(columnValue) : 1;
                        var endLine = args.TryGetValue("endLine", out var endLineValue) ? Convert.ToInt32(endLineValue) : line;
                        var scriptId = cdpConnection.FindScriptIdByPath(ConvertSourcePathToBrowserUrl(sourcePath, launchSettings["workspaceFolder"]?.ToString(), launchSettings["url"]?.ToString()));
                        var breakpoints = new List<Dictionary<string, object>>();

                        if (!string.IsNullOrWhiteSpace(scriptId))
                        {
                            var cdpResult = await cdpConnection.SendCdpCommandAsync("Debugger.getPossibleBreakpoints", new
                            {
                                start = new
                                {
                                    scriptId = scriptId,
                                    lineNumber = line - 1,
                                    columnNumber = Math.Max(column - 1, 0)
                                },
                                end = new
                                {
                                    scriptId = scriptId,
                                    lineNumber = endLine - 1
                                },
                                restrictToFunction = false
                            });

                            var locations = cdpResult["locations"] as JArray;

                            if (locations != null)
                            {
                                foreach (var locationToken in locations)
                                {
                                    if (locationToken is not JObject location)
                                    {
                                        continue;
                                    }

                                    breakpoints.Add(new Dictionary<string, object>
                                    {
                                        { "line", location["lineNumber"]?.Value<int>() + 1 ?? line },
                                        { "column", location["columnNumber"]?.Value<int>() + 1 ?? 1 }
                                    });
                                }
                            }
                        }

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                { "breakpoints", breakpoints }
                            }
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "restartFrame":
                    {
                        var args = commandPacket.Arguments;
                        var frameId = Convert.ToInt32(args["frameId"]);
                        var callFrameId = cdpConnection.GetCallFrameId(frameId) ?? throw new InvalidOperationException($"Unknown frame {frameId}.");

                        await cdpConnection.SendCdpCommandAsync("Debugger.restartFrame", new
                        {
                            callFrameId = callFrameId,
                            mode = "StepInto"
                        });

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>()
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "terminate":
                    {
                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>()
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        if (cdpConnection != null)
                        {
                            await cdpConnection.DisconnectAsync();
                        }

                        cdpCancellationTokenSource.Cancel();
                        KillAnyDotNetServeInstance(this.usedPort);

                        dotNetCancellationTokenSource.Cancel();

                        try
                        {
                            logsDirectory.CleanupSortableFiles("*.log", TimeSpan.FromDays(3));
                        }
                        catch
                        {
                        }

                        break;
                    }
                case "threads":
                    {
                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                {
                                    "threads", new List<Dictionary<string, object>>
                                    {
                                        new Dictionary<string, object>
                                        {
                                            { "id", 1 },
                                            { "name", "Chrome Main Thread" }
                                        }
                                    }
                                }
                            }
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "stackTrace":
                    {
                        var stackFrames = new List<Dictionary<string, object>>();
                        var callFrames = cdpConnection.PausedCallFrames;
                        int frameId = 1;

                        if (callFrames != null)
                        {
                            foreach (var callFrameToken in callFrames)
                            {
                                var callFrame = callFrameToken as JObject;

                                if (callFrame == null)
                                {
                                    continue;
                                }

                                var functionName = callFrame["functionName"]?.ToString();
                                var location = callFrame["location"] as JObject ?? throw new InvalidOperationException("Missing call frame location.");
                                var scriptId = location["scriptId"]?.ToString() ?? throw new InvalidOperationException("Missing script id.");
                                var lineNumber = location["lineNumber"]?.Value<int>() + 1 ?? 1;
                                var columnNumber = location["columnNumber"]?.Value<int>() + 1 ?? 1;
                                var sourceUrl = cdpConnection.GetScriptUrl(scriptId);
                                var sourcePath = ConvertBrowserUrlToSourcePath(sourceUrl);

                                stackFrames.Add(new Dictionary<string, object>
                                {
                                    { "id", frameId++ },
                                    { "name", string.IsNullOrWhiteSpace(functionName) ? "<anonymous>" : functionName },
                                    { "line", lineNumber },
                                    { "column", columnNumber },
                                    {
                                        "source", new Dictionary<string, object>
                                        {
                                            { "name", Path.GetFileName(sourcePath) },
                                            { "path", sourcePath }
                                        }
                                    }
                                });
                            }
                        }

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                { "stackFrames", stackFrames },
                                { "totalFrames", stackFrames.Count }
                            }
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "scopes":
                    {
                        var args = commandPacket.Arguments;
                        var frameId = Convert.ToInt32(args["frameId"]);
                        var scopes = new List<Dictionary<string, object>>();
                        var callFrame = cdpConnection.GetCallFrame(frameId);

                        if (callFrame != null)
                        {
                            var scopeChain = callFrame["scopeChain"] as JArray;

                            if (scopeChain != null)
                            {
                                foreach (var scopeToken in scopeChain)
                                {
                                    var scope = scopeToken as JObject;

                                    if (scope == null)
                                    {
                                        continue;
                                    }

                                    var scopeType = scope["type"]?.ToString();
                                    var scopeObject = scope["object"] as JObject;
                                    var objectId = scopeObject?["objectId"]?.ToString();

                                    if (!string.IsNullOrWhiteSpace(objectId))
                                    {
                                        var callFrameId = callFrame["callFrameId"]?.ToString() ?? string.Empty;
                                        var scopeNumber = 0;
                                        var variablesReference = cdpConnection.RegisterScopeReference(callFrameId, scopeNumber, objectId);

                                        scopes.Add(new Dictionary<string, object>
                                        {
                                            { "name", scopeType ?? "Scope" },
                                            { "variablesReference", variablesReference },
                                            { "expensive", false }
                                        });

                                        scopeNumber++;
                                    }
                                }
                            }
                        }

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                { "scopes", scopes }
                            }
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "variables":
                    {
                        var args = commandPacket.Arguments;
                        var variablesReference = Convert.ToInt32(args["variablesReference"]);
                        var objectId = cdpConnection.GetObjectReference(variablesReference);
                        var variables = new List<Dictionary<string, object>>();

                        if (!string.IsNullOrWhiteSpace(objectId))
                        {
                            var cdpResult = await cdpConnection.SendCdpCommandAsync("Runtime.getProperties", new
                            {
                                objectId = objectId,
                                ownProperties = true,
                                generatePreview = true
                            });

                            var properties = cdpResult["result"] as JArray;

                            if (properties != null)
                            {
                                foreach (var propertyToken in properties)
                                {
                                    var property = propertyToken as JObject;

                                    if (property == null)
                                    {
                                        continue;
                                    }

                                    var name = property["name"]?.ToString() ?? string.Empty;
                                    var value = property["value"] as JObject;

                                    if (value == null)
                                    {
                                        continue;
                                    }

                                    var valueText = GetCdpValueText(value);
                                    var childObjectId = value["objectId"]?.ToString();
                                    var childReference = string.IsNullOrWhiteSpace(childObjectId) ? 0 : cdpConnection.RegisterObjectReference(childObjectId);

                                    variables.Add(new Dictionary<string, object>
                                    {
                                        { "name", name },
                                        { "value", valueText },
                                        { "variablesReference", childReference }
                                    });
                                }
                            }
                        }

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                { "variables", variables }
                            }
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "continue":
                    {
                        await cdpConnection.SendCdpCommandAsync("Debugger.resume");

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                { "allThreadsContinued", true }
                            }
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "next":
                    {
                        await cdpConnection.SendCdpCommandAsync("Debugger.stepOver");

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>()
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "stepIn":
                    {
                        await cdpConnection.SendCdpCommandAsync("Debugger.stepInto");

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>()
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "stepOut":
                    {
                        await cdpConnection.SendCdpCommandAsync("Debugger.stepOut");

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>()
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "pause":
                    {
                        await cdpConnection.SendCdpCommandAsync("Debugger.pause");

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>()
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "evaluate":
                    {
                        var args = commandPacket.Arguments;
                        var expression = args["expression"]?.ToString() ?? string.Empty;
                        var frameId = args.TryGetValue("frameId", out var frameIdValue) ? Convert.ToInt32(frameIdValue) : 0;
                        JObject cdpResult;

                        if (frameId > 0)
                        {
                            var callFrameId = cdpConnection.GetCallFrameId(frameId);

                            cdpResult = await cdpConnection.SendCdpCommandAsync("Debugger.evaluateOnCallFrame", new
                            {
                                callFrameId = callFrameId,
                                expression = expression,
                                returnByValue = false,
                                generatePreview = true
                            });
                        }
                        else
                        {
                            cdpResult = await cdpConnection.SendCdpCommandAsync("Runtime.evaluate", new
                            {
                                expression = expression,
                                returnByValue = false,
                                generatePreview = true
                            });
                        }

                        var result = cdpResult["result"] as JObject ?? new JObject();
                        var resultText = GetCdpValueText(result);
                        var objectId = result["objectId"]?.ToString();
                        var variablesReference = string.IsNullOrWhiteSpace(objectId) ? 0 : cdpConnection.RegisterObjectReference(objectId);

                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>
                            {
                                { "result", resultText },
                                { "variablesReference", variablesReference }
                            }
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        break;
                    }
                case "disconnect":
                    {
                        var response = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>()
                        };

                        outputWriter.WriteJsonCommandT(response, true, WriteOutputMessage);

                        if (cdpConnection != null)
                        {
                            await cdpConnection.DisconnectAsync();
                        }

                        cdpCancellationTokenSource.Cancel();

                        break;
                    }
                case "configurationDone":
                    {
                        string url;
                        var configurationResponse = new DapResponsePacket
                        {
                            Sequence = NextSequence(),
                            Type = "response",
                            RequestSequence = commandPacket.Sequence,
                            Command = commandPacket.Command,
                            Success = true,
                            Body = new Dictionary<string, object>()
                        };

                        outputWriter.WriteJsonCommandT(configurationResponse, true, WriteOutputMessage);

                        //
                        // Chrome was intentionally launched on about:blank so that CDP could be
                        // connected and all VS Code breakpoints could be configured before any
                        // application JavaScript executes. configurationDone is the signal that
                        // the debugger configuration phase is complete, so navigate to the real
                        // application now.
                        //
                        url = launchSettings["url"]?.ToString() ?? throw new InvalidOperationException("Launch URL is not available.");

                        await cdpConnection.SendCdpCommandAsync("Page.navigate", new
                        {
                            url = url
                        });

                        break;
                    }

                default:
                    DebugUtils.Break();
                    break;
            }
        }

        private async void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                try
                {
                    WriteLine("Unhandled exception: " + e.ExceptionObject.ToString());
                }
                catch
                {
                }

                if (cdpConnection != null)
                {
                    await cdpConnection.DisconnectAsync();
                }

                if (cdpCancellationTokenSource != null)
                {
                    cdpCancellationTokenSource.Cancel();
                }

                if (this.usedPort != -1)
                {
                    KillAnyDotNetServeInstance(this.usedPort);
                }

                if (dotNetCancellationTokenSource != null)
                {
                    dotNetCancellationTokenSource.Cancel();
                }
            }
            catch (Exception ex)
            {
                WriteLine("Error during unhandled exception cleanup: " + ex.ToString());
            }
            finally
            {
                Environment.Exit(1);
            }
        }

        private void WriteOutputMessage(string message)
        {
            dapMessagesLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Output " + "*".Repeat(50) + "\r\n");
            dapMessagesLogWriter.WriteLine("\r\n" + message + "\r\n");
        }

        private void WriteInputMessage(DapCommandPacket commandPacket)
        {
            var json = JsonExtensions.ToJsonText(commandPacket, true);

            dapMessagesLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Input " + "*".Repeat(50) + "\r\n");
            dapMessagesLogWriter.WriteLine("\r\n" + json + "\r\n");
        }

        private string? BuildBreakpointCondition(string? condition, string? hitCondition, string? logMessage)
        {
            var conditions = new List<string>();

            if (!string.IsNullOrWhiteSpace(condition))
            {
                conditions.Add($"({condition})");
            }

            if (!string.IsNullOrWhiteSpace(hitCondition) && int.TryParse(hitCondition, out int hitCount))
            {
                conditions.Add($"((this.__cloudIdeaasBreakpointHits = (this.__cloudIdeaasBreakpointHits || 0) + 1) >= {hitCount})");
            }

            if (!string.IsNullOrWhiteSpace(logMessage))
            {
                var escaped = logMessage.Replace("\\", "\\\\").Replace("\"", "\\\"");

                conditions.Add($"(console.log(\"{escaped}\"), false)");
            }

            if (conditions.Count == 0)
            {
                return null;
            }

            return string.Join(" && ", conditions);
        }

        private string ConvertBrowserUrlToSourcePath(string browserUrl)
        {
            var workspaceFolder = launchSettings["workspaceFolder"]?.ToString() ?? Environment.CurrentDirectory;
            var applicationUrl = launchSettings["url"]?.ToString() ?? throw new InvalidOperationException("Application URL is not available.");
            var browserUri = new Uri(browserUrl);
            var applicationUri = new Uri(applicationUrl);
            var relativePath = Uri.UnescapeDataString(browserUri.AbsolutePath.TrimStart('/'));

            return Path.Combine(workspaceFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private string GetCdpValueText(JObject value)
        {
            if (value["value"] != null)
            {
                return value["value"]!.ToString();
            }

            if (value["description"] != null)
            {
                return value["description"]!.ToString();
            }

            if (value["type"] != null)
            {
                return value["type"]!.ToString();
            }

            return string.Empty;
        }

        public int NextSequence()
        {
            return Interlocked.Increment(ref nextSequence);
        }

        public void WritePacket(object packet)
        {
            this.outputWriter.WriteJsonCommandT(packet, true, WriteOutputMessage);
        }
        private async Task<string> FindChromeTargetAsync(string targetUrl, string? fallbackUrl, string debuggerHost, int port, CancellationToken cancellationToken = default)
        {
            using (var httpClient = new HttpClient())
            {
                var endpoint = $"http://{debuggerHost}:{port}/json/list";
                var json = await httpClient.GetStringAsync(endpoint, cancellationToken);
                var targets = JArray.Parse(json);
                JObject? firstPageTarget = null;

                WriteOutputMessage(json);

                foreach (var targetToken in targets)
                {
                    var target = targetToken as JObject;
                    string url;

                    if (target == null)
                    {
                        continue;
                    }

                    if (target["type"]?.ToString() != "page")
                    {
                        continue;
                    }

                    url = target["url"]?.ToString()!;

                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    var webSocketUrl = target["webSocketDebuggerUrl"]?.ToString();

                    if (string.IsNullOrWhiteSpace(webSocketUrl))
                    {
                        continue;
                    }

                    firstPageTarget ??= target;

                    //
                    // First preference is the page Chrome was explicitly launched with.
                    //
                    if (UrlsMatch(url, targetUrl))
                    {
                        WriteLine("Found preferred Chrome debugging target: {0}", url);

                        return webSocketUrl;
                    }
                }

                //
                // Chrome can navigate away from about:blank before its debugging
                // target is queried. If that happened, look for the application's
                // requested URL instead.
                //
                if (!string.IsNullOrWhiteSpace(fallbackUrl))
                {
                    foreach (var targetToken in targets)
                    {
                        var target = targetToken as JObject;

                        if (target == null || target["type"]?.ToString() != "page")
                        {
                            continue;
                        }

                        var url = target["url"]?.ToString();
                        var webSocketUrl = target["webSocketDebuggerUrl"]?.ToString();

                        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(webSocketUrl))
                        {
                            continue;
                        }

                        if (UrlsMatch(url, fallbackUrl))
                        {
                            WriteLine("Preferred Chrome target '{0}' was not found. Using application target: {1}", targetUrl, url);

                            return webSocketUrl;
                        }
                    }
                }

                //
                // Last resort: if Chrome has exactly one normal page target, use it.
                // Chrome's internal browser_ui targets are intentionally ignored.
                //
                var pageTargets = targets
                    .OfType<JObject>()
                    .Where(t => t["type"]?.ToString() == "page")
                    .Where(t => !string.IsNullOrWhiteSpace(t["webSocketDebuggerUrl"]?.ToString()))
                    .ToList();

                if (pageTargets.Count == 1)
                {
                    var target = pageTargets[0];
                    var url = target["url"]?.ToString() ?? string.Empty;
                    var webSocketUrl = target["webSocketDebuggerUrl"]!.ToString();

                    WriteLine("Preferred Chrome target '{0}' was not found. Using the only available page target: {1}", targetUrl, url);

                    return webSocketUrl;
                }
            }

            throw new InvalidOperationException($"Could not find a Chrome debugging target for '{targetUrl}' or '{fallbackUrl}'.");
        }

        private string ConvertSourcePathToBrowserUrl(string sourcePath, string? workspaceFolder, string? applicationUrl)
        {
            string relativePath;
            string origin;
            Uri baseUri;

            if (string.IsNullOrWhiteSpace(workspaceFolder))
            {
                throw new InvalidOperationException("Workspace folder is not available.");
            }

            if (string.IsNullOrWhiteSpace(applicationUrl))
            {
                throw new InvalidOperationException("Application URL is not available.");
            }

            relativePath = Path.GetRelativePath(workspaceFolder, sourcePath);
            relativePath = relativePath.Replace('\\', '/');

            baseUri = new Uri(applicationUrl);

            origin = $"{baseUri.Scheme}://{baseUri.Authority}/";

            return new Uri(new Uri(origin), relativePath).ToString();
        }

        private bool UrlsMatch(string chromeUrl, string requestedUrl)
        {
            if (!Uri.TryCreate(chromeUrl, UriKind.Absolute, out var chromeUri))
            {
                return false;
            }

            if (!Uri.TryCreate(requestedUrl, UriKind.Absolute, out var requestedUri))
            {
                return false;
            }

            return string.Equals(chromeUri.Scheme, requestedUri.Scheme, StringComparison.OrdinalIgnoreCase) && string.Equals(chromeUri.Host, requestedUri.Host, StringComparison.OrdinalIgnoreCase) && string.Equals(chromeUri.AbsolutePath.TrimEnd('/'), requestedUri.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> WaitForChromeAsync(string address, int port, int timeoutMilliseconds = 10000, CancellationToken cancellationToken = default)
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(500)
            };

            var endpoint = $"http://{address}:{port}/json/version";

            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < TimeSpan.FromMilliseconds(timeoutMilliseconds))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var response = await httpClient.GetAsync(endpoint, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync(cancellationToken);

                        WriteOutputMessage(content);

                        return content;
                    }
                }
                catch (HttpRequestException)
                {
                    // Chrome isn't listening yet.
                }
                catch (TaskCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    // Individual HTTP attempt timed out.
                }

                await Task.Delay(100, cancellationToken);
            }

            throw new TimeoutException($"Chrome did not start its debugging endpoint at {endpoint} within {timeoutMilliseconds} ms.");
        }

        private async Task HandleLaunchAsync(string url, string workingDirectory)
        {
            var chromeHandler = new ChromeCommandHandler();
            var dotNetHandler = new DotNetCommandHandler();
            var serverReadyResetEvent = new ManualResetEvent(false);
            var chromeStartedResetEvent = new ManualResetEvent(false);
            var hasListeningOn = false;
            var lockObject = LockManager.CreateObject();
            var uri = new Uri(url);
            var port = uri.Port;
            var errorCount = 0;
            var debuggerUri = new Uri($"http://127.0.0.1:9222");
            OutputWriteLine outputWriteLine = null!;
            ErrorWriteLine errorWriteLine = null!;
            string webSocketUrl;
            Uri webSocketUri;
            string chromeResponse;

            this.usedPort = port;

            vsCodeDirectory = Path.Combine(workingDirectory, @".vscode");
            userDataDirectory = Path.Combine(vsCodeDirectory, "chrome-debug-profile");
            debuggerLogDirectory = Path.Combine(vsCodeDirectory, @"chrome-debug-profile");

            chromeHandler.OutputWriteLine = (f, e) =>
            {
                WriteLine(f, e);
            };

            chromeHandler.ErrorWriteLine = (f, e) =>
            {
                var error = string.Format(f, e);

                using (ConsoleColorizer.UseColor(ConsoleColor.Red))
                {
                    WriteLine(f, e);
                }
            };

            outputWriteLine = (f, e) =>
            {
                var output = string.Format(f, e);

                using (lockObject.Lock())
                {
                    if (hasListeningOn)
                    {
                        serverReadyResetEvent.Set();
                    }
                    else if (output == "Listening on:")
                    {
                        hasListeningOn = true;
                    }
                }

                WriteLine(f, e);
            };

            errorWriteLine = (f, e) =>
            {
                var error = string.Format(f, e);

                WriteLine(f, e);

                using (lockObject.Lock())
                {
                    errorCount++;

                    if (relaunching)
                    {
                        return;
                    }
                }

                if (error == "dotnet failed with ExitCode=2" || error.RegexIsMatch(@"Unexpected error: System.IO.IOException: Failed to bind to address (?<address>.*?): address already in use."))
                {
                    var address = error.RegexGet(@"Unexpected error: System.IO.IOException: Failed to bind to address (?<address>.*?): address already in use.", "address");
                    var flowControl = KillRelaunchDotNetServe(workingDirectory, dotNetHandler, lockObject, port, outputWriteLine, errorWriteLine, true);

                    if (!flowControl)
                    {
                        return;
                    }
                }
                else
                {
                    DebugUtils.Break();
                }

                WriteLine(f, e);
            };

            dotNetHandler.OutputWriteLine = outputWriteLine;
            dotNetHandler.ErrorWriteLine = errorWriteLine;

            dotNetTask = Task.Run(() =>
            {
                dotNetHandler.Serve(workingDirectory, port);

            }, dotNetCancellationTokenSource.Token);

            if (!serverReadyResetEvent.WaitOne(60_000)) // kn todo - put back to 10 secs.
            {
                DebugUtils.Break();
            }

            chromeHandler.ProcessStarted += (s, e) => chromeStartedResetEvent.Set();

            //
            // Launch Chrome on about:blank. The real application URL is not loaded until
            // configurationDone, after VS Code has sent all initial breakpoint configuration.
            //

            if (!Directory.Exists(userDataDirectory))
            {
                Directory.CreateDirectory(userDataDirectory);
            }

            chromeHandler.LaunchDebugMode(new Uri("about:blank"), debuggerUri, new DirectoryInfo(userDataDirectory));

            await Task.Run(() => chromeStartedResetEvent.WaitOne(10_000));

            chromeResponse = await WaitForChromeAsync(debuggerUri.Host, debuggerUri.Port);

            webSocketUrl = await FindChromeTargetAsync("about:blank", url, debuggerUri.Host, debuggerUri.Port);
            webSocketUri = new Uri(webSocketUrl);

            cdpConnection = new CdpConnection(logWriter, cdpMessagesLogWriter);

            await cdpConnection.ConnectAsync(webSocketUri, cdpCancellationTokenSource.Token, this);

            //
            // 7. Enable the CDP domains we need.
            //
            await cdpConnection.SendCdpCommandAsync("Page.enable");
            await cdpConnection.SendCdpCommandAsync("Runtime.enable");
            await cdpConnection.SendCdpCommandAsync("Debugger.enable");

            await cdpConnection.SendCdpCommandAsync("Debugger.setAsyncCallStackDepth", new
            {
                maxDepth = 32
            });
        }

        private bool KillAnyDotNetServeInstance(int port)
        {
            var dotNetProcesses = Process.GetProcessesByName("dotnet").ToList();

            foreach (var process in dotNetProcesses)
            {
                var platformProcess = process.GetPlatformProcess();
                var commandLine = platformProcess.CommandLine;
                var processPort = commandLine.RegexGet(@"serve --port (?<port>\d+?)$", "port");

                if (processPort != null && int.TryParse(processPort, out int parsedPort) && parsedPort == port)
                {
                    process.Kill();

                    WriteLine("Killed dotnet process with PID {0} that is using port {1}.", process.Id, port);
                }
            }

            return true;
        }

        private bool KillRelaunchDotNetServe(string workingDirectory, DotNetCommandHandler dotNetHandler, IManagedLockObject lockObject, int port, OutputWriteLine outputWriteLine, ErrorWriteLine errorWriteLine, bool relaunch = false)
        {
            var dotNetProcesses = Process.GetProcessesByName("dotnet").Where(p => p.Id != dotNetHandler.ProcessId).ToList();

            foreach (var process in dotNetProcesses)
            {
                var platformProcess = process.GetPlatformProcess();
                var commandLine = platformProcess.CommandLine;
                var processPort = commandLine.RegexGet(@"serve --port (?<port>\d+?)$", "port");

                if (processPort != null && int.TryParse(processPort, out int parsedPort) && parsedPort == port)
                {
                    process.Kill();

                    WriteLine("Killed dotnet process with PID {0} that is using port {1}.", process.Id, port);

                    dotNetHandler.OutputWriteLine = outputWriteLine;
                    dotNetHandler.ErrorWriteLine = errorWriteLine;

                    if (relaunch)
                    {
                        using (lockObject.Lock())
                        {
                            if (this.relaunching)
                            {
                                return false;
                            }

                            this.relaunching = true;
                        }

                        dotNetTask = Task.Run(() =>
                        {
                            try
                            {
                                if (!dotNetHandler.HasExited)
                                {
                                    dotNetHandler.Kill();
                                }

                                dotNetHandler.Serve(workingDirectory, port);
                            }
                            finally
                            {
                                using (lockObject.Lock())
                                {
                                    this.relaunching = false;
                                }
                            }

                        }, dotNetCancellationTokenSource.Token);
                    }
                }
            }

            return true;
        }

        public IDisposable ErrorMode()
        {
            throw new NotImplementedException();
        }

        public void Write(string value)
        {
            throw new NotImplementedException();
        }

        public void Write(string format, params object[] args)
        {
            throw new NotImplementedException();
        }

        public void WriteLine(string value)
        {
            logWriter.WriteLine(value);
        }

        protected override void HandleExcepion(Exception ex)
        {
            logWriter.WriteLine(ex.ToString());
        }

        public void WriteLine()
        {
            throw new NotImplementedException();
        }

        public void WriteLine(string format, params object[] args)
        {
            logWriter.WriteLine(format, args);
        }
    }
}