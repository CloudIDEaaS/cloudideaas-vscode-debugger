using ChromeDebugger;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using Utils;
using VSCodeDebugger.Models;
using static ScintillaNET.Style;

namespace VSCodeDebugger
{
    public class CdpConnection : ILogWriter
    {
        private readonly ClientWebSocket webSocket = new();
        private Task loopTask;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JObject>> pendingCommands = new();
        private readonly ConcurrentDictionary<string, ParsedScript> scripts = new();
        private CdpPausedState? pausedState;
        private int nextCdpCommandId;
        private readonly SemaphoreSlim webSocketSendLock = new(1, 1);
        private StandardStreamService standardStreamService;
        private readonly ConcurrentDictionary<int, JObject> callFrames = new();
        private readonly ConcurrentDictionary<int, string> objectReferences = new();
        private int nextObjectReference;
        private readonly ConcurrentDictionary<string, List<string>> sourceBreakpoints = new();
        private readonly ConcurrentDictionary<int, ScopeReference> scopeReferences = new();
        private readonly ConcurrentDictionary<int, string> sourceReferences = new();
        private int nextSourceReference;
        private JObject? currentException;
        private readonly ConcurrentDictionary<string, int> dapBreakpointIds = new();
        private int nextDapBreakpointId;
        private LogWriter logWriter;
        private LogWriter messagesLogWriter;

        public CdpConnection(LogWriter logWriter, LogWriter messagesLogWriter)
        {
            this.logWriter = logWriter;
            this.messagesLogWriter = messagesLogWriter;
        }

        public JArray? PausedCallFrames
        {
            get
            {
                if (pausedState == null)
                {
                    return null;
                }

                return pausedState.CallFrames;
            }
        }
        public int RegisterBreakpoint(string sourcePath, string breakpointId)
        {
            var breakpoints = sourceBreakpoints.GetOrAdd(sourcePath, _ => new List<string>());
            int dapBreakpointId;

            lock (breakpoints)
            {
                breakpoints.Add(breakpointId);
            }

            dapBreakpointId = Interlocked.Increment(ref nextDapBreakpointId);

            dapBreakpointIds[breakpointId] = dapBreakpointId;

            return dapBreakpointId;
        }

        public async Task RemoveBreakpointsForSourceAsync(string sourcePath)
        {
            if (!sourceBreakpoints.TryRemove(sourcePath, out var breakpoints))
            {
                return;
            }

            List<string> breakpointIds;

            lock (breakpoints)
            {
                breakpointIds = breakpoints.ToList();
            }

            foreach (var breakpointId in breakpointIds)
            {
                await SendCdpCommandAsync("Debugger.removeBreakpoint", new
                {
                    breakpointId = breakpointId
                });

                dapBreakpointIds.TryRemove(breakpointId, out _);
            }
        }
        public IEnumerable<ParsedScript> GetScripts()
        {
            return scripts.Values.OrderBy(s => s.Url).ToList();
        }

        public string? FindScriptIdByPath(string browserUrl)
        {
            foreach (var script in scripts.Values)
            {
                if (string.Equals(script.Url, browserUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return script.ScriptId;
                }
            }

            return null;
        }

        public int RegisterSourceReference(string scriptId)
        {
            int sourceReference;

            sourceReference = Interlocked.Increment(ref nextSourceReference);

            sourceReferences[sourceReference] = scriptId;

            return sourceReference;
        }

        public string? GetScriptIdForSourceReference(int sourceReference)
        {
            if (sourceReferences.TryGetValue(sourceReference, out var scriptId))
            {
                return scriptId;
            }

            return null;
        }

        public string GetScriptUrl(string scriptId)
        {
            if (scripts.TryGetValue(scriptId, out var script))
            {
                return script.Url ?? string.Empty;
            }

            return string.Empty;
        }

        public int RegisterScopeReference(string callFrameId, int scopeNumber, string objectId)
        {
            int variablesReference;

            variablesReference = Interlocked.Increment(ref nextObjectReference);

            objectReferences[variablesReference] = objectId;

            scopeReferences[variablesReference] = new ScopeReference
            {
                CallFrameId = callFrameId,
                ScopeNumber = scopeNumber,
                ObjectId = objectId
            };

            return variablesReference;
        }

        public ScopeReference? GetScopeReference(int variablesReference)
        {
            if (scopeReferences.TryGetValue(variablesReference, out var scopeReference))
            {
                return scopeReference;
            }

            return null;
        }

        private void UpdateCallFrames(JArray callFrameArray)
        {
            int frameId = 1;

            callFrames.Clear();

            foreach (var callFrameToken in callFrameArray)
            {
                if (callFrameToken is JObject callFrame)
                {
                    callFrames[frameId++] = (JObject)callFrame.DeepClone();
                }
            }
        }

        public JObject? GetCallFrame(int frameId)
        {
            if (callFrames.TryGetValue(frameId, out var callFrame))
            {
                return callFrame;
            }

            return null;
        }

        public string? GetCallFrameId(int frameId)
        {
            JObject callFrame;

            if (!callFrames.TryGetValue(frameId, out callFrame))
            {
                return null;
            }

            return callFrame["callFrameId"]?.ToString();
        }

        public int RegisterObjectReference(string objectId)
        {
            int variablesReference;

            variablesReference = Interlocked.Increment(ref nextObjectReference);

            objectReferences[variablesReference] = objectId;

            return variablesReference;
        }

        public string? GetObjectReference(int variablesReference)
        {
            if (objectReferences.TryGetValue(variablesReference, out var objectId))
            {
                return objectId;
            }

            return null;
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Debug session ended.", cancellationToken);
            }

            if (loopTask != null)
            {
                try
                {
                    await loopTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            webSocket.Dispose();
        }

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken, StandardStreamService standardStreamService)
        {
            this.standardStreamService = standardStreamService;

            await webSocket.ConnectAsync(uri, cancellationToken);

            loopTask = Task.Run(() => ReceiveLoopAsync(webSocket, cancellationToken));
        }

        private async Task ReceiveLoopAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];

            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var messageStream = new MemoryStream();

                WebSocketReceiveResult result;

                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);

                        return;
                    }

                    messageStream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                string json = Encoding.UTF8.GetString(messageStream.GetBuffer(), 0, (int)messageStream.Length);

                await HandleCdpMessageAsync(json);
            }
        }

        private async Task SendDapEventAsync(string eventName, object? body = null)
        {
            var packet = new
            {
                seq = standardStreamService.NextSequence(),
                type = "event",
                @event = eventName,
                body
            };

            await Task.Run(() => standardStreamService.WritePacket(packet));
        }

        private async Task SendWebSocketTextAsync(string text, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(text);

            WriteOutputMessage(text);

            await webSocketSendLock.WaitAsync(cancellationToken);

            try
            {
                await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
            finally
            {
                webSocketSendLock.Release();
            }
        }

        public async Task<JObject> SendCdpCommandAsync(string method, object? parameters = null, string? sessionId = null, CancellationToken cancellationToken = default)
        {
            var id = Interlocked.Increment(ref nextCdpCommandId);
            var completionSource = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!pendingCommands.TryAdd(id, completionSource))
            {
                throw new InvalidOperationException($"Unable to register CDP command {id}.");
            }

            var command = new JObject
            {
                ["id"] = id,
                ["method"] = method
            };

            if (parameters != null)
            {
                command["params"] = parameters is JToken token ? token : JToken.FromObject(parameters);
            }

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                command["sessionId"] = sessionId;
            }

            var json = command.ToString(Formatting.None);

            WriteOutputMessage(json);

            try
            {
                await SendWebSocketTextAsync(json, cancellationToken);

                using var registration = cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));

                return await completionSource.Task;
            }
            catch
            {
                pendingCommands.TryRemove(id, out _);
                throw;
            }
        }

        private async Task HandleCdpMessageAsync(string json)
        {
            WriteInputMessage(json);

            var root = JObject.Parse(json);
            var method = root["method"]?.ToString();

            //
            // CDP EVENT
            //
            if (!string.IsNullOrWhiteSpace(method))
            {
                switch (method)
                {
                    case "Debugger.paused":
                        await HandleDebuggerPausedAsync(root);
                        break;

                    case "Debugger.resumed":
                        await HandleDebuggerResumedAsync(root);
                        break;

                    case "Debugger.scriptParsed":
                        await HandleScriptParsedAsync(root);
                        break;

                    case "Debugger.breakpointResolved":
                        await HandleBreakpointResolvedAsync(root);
                        break;

                    case "Debugger.globalObjectCleared":
                        HandleGlobalObjectCleared();
                        break;
                }

                return;
            }

            //
            // CDP RESPONSE
            //
            var id = root["id"]?.Value<int?>();

            if (id.HasValue)
            {
                HandleCdpResponse(id.Value, root);
            }
        }

        private async Task HandleDebuggerResumedAsync(JObject message)
        {
            pausedState = null;

            callFrames.Clear();
            objectReferences.Clear();

            currentException = null;
            scopeReferences.Clear();

            WriteLine("[CDP] Debugger resumed.");

            await SendDapEventAsync("continued", new
            {
                threadId = 1,
                allThreadsContinued = true
            });
        }

        private async Task HandleScriptParsedAsync(JObject message)
        {
            string? url = null;
            var parameters = message["params"] as JObject;

            if (parameters == null)
            {
                return;
            }

            var scriptId = parameters["scriptId"]?.ToString();

            if (string.IsNullOrWhiteSpace(scriptId))
            {
                return;
            }

            url = parameters["url"]?.ToString();

            var startLine = parameters["startLine"]?.Value<int>() ?? 0;
            var startColumn = parameters["startColumn"]?.Value<int>() ?? 0;
            var endLine = parameters["endLine"]?.Value<int>() ?? 0;
            var endColumn = parameters["endColumn"]?.Value<int>() ?? 0;

            var script = new ParsedScript
            {
                ScriptId = scriptId,
                Url = url,
                StartLine = startLine,
                StartColumn = startColumn,
                EndLine = endLine,
                EndColumn = endColumn
            };

            scripts[scriptId] = script;

            WriteLine($"[CDP] Script parsed: ScriptId={scriptId}, URL={url}");

            //
            // Newer CDP versions include the URL breakpoints that resolved while this
            // script was being parsed. Report those resolutions back to VS Code so an
            // initially-unverified breakpoint becomes visually verified.
            //
            if (parameters["resolvedBreakpoints"] is JArray resolvedBreakpoints)
            {
                foreach (var resolvedBreakpointToken in resolvedBreakpoints)
                {
                    if (resolvedBreakpointToken is not JObject resolvedBreakpoint)
                    {
                        continue;
                    }

                    var breakpointId = resolvedBreakpoint["breakpointId"]?.ToString();
                    var location = resolvedBreakpoint["location"] as JObject;

                    if (string.IsNullOrWhiteSpace(breakpointId) || location == null)
                    {
                        continue;
                    }

                    await SendBreakpointChangedEventAsync(breakpointId, location);
                }
            }
        }

        private async Task HandleBreakpointResolvedAsync(JObject message)
        {
            var parameters = message["params"] as JObject;

            if (parameters == null)
            {
                return;
            }

            var breakpointId = parameters["breakpointId"]?.ToString();
            var location = parameters["location"] as JObject;

            if (string.IsNullOrWhiteSpace(breakpointId) || location == null)
            {
                return;
            }

            await SendBreakpointChangedEventAsync(breakpointId, location);
        }

        private async Task SendBreakpointChangedEventAsync(string breakpointId, JObject location)
        {
            if (!dapBreakpointIds.TryGetValue(breakpointId, out var dapBreakpointId))
            {
                return;
            }

            var line = location["lineNumber"]?.Value<int>() + 1 ?? 1;
            var column = location["columnNumber"]?.Value<int>() + 1 ?? 1;

            await SendDapEventAsync("breakpoint", new
            {
                reason = "changed",
                breakpoint = new
                {
                    id = dapBreakpointId,
                    verified = true,
                    line = line,
                    column = column
                }
            });
        }

        private void HandleGlobalObjectCleared()
        {
            scripts.Clear();
            callFrames.Clear();
            objectReferences.Clear();
            scopeReferences.Clear();
            sourceReferences.Clear();

            pausedState = null;
            currentException = null;

            WriteLine("[CDP] Global object cleared. Cleared stale script and paused-state references.");
        }

        private void HandleCdpResponse(int id, JObject message)
        {
            if (!pendingCommands.TryRemove(id, out var completionSource))
            {
                WriteLine($"[CDP] Received response for unknown id {id}.");

                return;
            }

            //
            // CDP error response.
            //
            var error = message["error"] as JObject;

            if (error != null)
            {
                var code = error["code"]?.Value<int>() ?? 0;
                var errorMessage = error["message"]?.ToString() ?? "Unknown CDP error";

                completionSource.TrySetException(new InvalidOperationException($"CDP command {id} failed: [{code}] {errorMessage}"));

                return;
            }

            //
            // Normal response.
            //
            var result = message["result"] as JObject;

            if (result != null)
            {
                completionSource.TrySetResult((JObject)result.DeepClone());

                return;
            }

            //
            // Some commands legitimately return an empty result.
            //
            completionSource.TrySetResult(new JObject());
        }
        public JObject? GetCurrentException()
        {
            return currentException == null ? null : (JObject)currentException.DeepClone();
        }

        private async Task HandleDebuggerPausedAsync(JObject message)
        {
            string? reason = null;
            var callFrames = new JArray();
            JObject? data = null;
            var hitBreakpoints = new List<string>();
            var parameters = message["params"] as JObject;

            if (parameters == null)
            {
                return;
            }

            reason = parameters["reason"]?.ToString();

            currentException = null;

            if (reason == "exception" && parameters["data"] is JObject exceptionData)
            {
                currentException = (JObject)exceptionData.DeepClone();
            }

            if (parameters["callFrames"] is JArray callFramesArray)
            {
                callFrames = (JArray)callFramesArray.DeepClone();

                UpdateCallFrames(callFrames);
            }

            if (parameters["data"] is JObject dataObject)
            {
                data = (JObject)dataObject.DeepClone();
            }

            if (parameters["hitBreakpoints"] is JArray hitBreakpointArray)
            {
                foreach (var breakpoint in hitBreakpointArray)
                {
                    var id = breakpoint?.ToString();

                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        hitBreakpoints.Add(id);
                    }
                }
            }

            pausedState = new CdpPausedState
            {
                Reason = reason,
                CallFrames = callFrames,
                Data = data,
                HitBreakpoints = hitBreakpoints
            };

            WriteLine($"[CDP] Debugger paused. Reason={reason}");

            //
            // Translate the CDP pause reason into a DAP reason.
            //
            var dapReason = reason switch
            {
                "exception" => "exception",
                "assert" => "exception",
                "debugCommand" => "pause",
                "DOM" => "breakpoint",
                "EventListener" => "breakpoint",
                "XHR" => "breakpoint",
                _ when hitBreakpoints.Count > 0 => "breakpoint",
                _ => "pause"
            };

            await SendDapEventAsync("stopped", new
            {
                reason = dapReason,
                threadId = 1,
                allThreadsStopped = true
            });
        }

        public IDisposable ErrorMode()
        {
            throw new NotImplementedException();
        }

        private void WriteOutputMessage(string message)
        {
            messagesLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Output " + "*".Repeat(50) + "\r\n");
            messagesLogWriter.WriteLine("\r\n" + message + "\r\n");
        }

        private void WriteInputMessage(string message)
        {
            messagesLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Input " + "*".Repeat(50) + "\r\n");
            messagesLogWriter.WriteLine("\r\n" + message + "\r\n");
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

        public void WriteLine()
        {
            throw new NotImplementedException();
        }

        public void WriteLine(string format, params object[] args)
        {
            throw new NotImplementedException();
        }
    }
}