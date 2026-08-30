const AdmZip = require("adm-zip");
const childProcess = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const VSIX_FILE = "cloudideaas-vscode-debugger-release.vsix";
const DAP_TIMEOUT_MS = 15000;
const BREAKPOINT_TIMEOUT_MS = 20000;
const HTTP_PORT = 18991;

function section(title) {
  console.log("");
  console.log("============================================================");
  console.log(title);
  console.log("============================================================");
}

function pass(message) {
  console.log(`[PASS] ${message}`);
}

function info(message) {
  console.log(`[INFO] ${message}`);
}

function fail(message) {
  throw new Error(message);
}

function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function formatBytes(bytes) {
  const mb = bytes / 1024 / 1024;

  return `${mb.toFixed(2)} MB`;
}

function requireFile(rootDirectory, relativePath) {
  const fullPath = path.join(rootDirectory, relativePath);

  if (!fs.existsSync(fullPath)) {
    fail(`Required VSIX file is missing: ${relativePath}`);
  }

  pass(relativePath);

  return fullPath;
}

function readJson(filePath) {
  return JSON.parse(
    fs.readFileSync(
      filePath,
      "utf8"
    )
  );
}

function findChrome() {
  const candidates = [
    path.join(
      process.env.PROGRAMFILES || "C:\\Program Files",
      "Google",
      "Chrome",
      "Application",
      "chrome.exe"
    ),
    path.join(
      process.env["PROGRAMFILES(X86)"] || "C:\\Program Files (x86)",
      "Google",
      "Chrome",
      "Application",
      "chrome.exe"
    ),
    path.join(
      process.env.LOCALAPPDATA || "",
      "Google",
      "Chrome",
      "Application",
      "chrome.exe"
    )
  ];

  for (const candidate of candidates) {
    if (
      candidate &&
      fs.existsSync(candidate)
    ) {
      return candidate;
    }
  }

  return null;
}

function killVerifierChrome(workspaceDirectory) {
  if (process.platform !== "win32") {
    return;
  }

  const chromeProfileDirectory = path.join(
    workspaceDirectory,
    ".vscode",
    "chrome-debug-profile"
  );

  const escapedProfileDirectory = chromeProfileDirectory.replace(
    /'/g,
    "''"
  );

  const script = [
    "$ErrorActionPreference = 'SilentlyContinue'",
    `$profile = '${escapedProfileDirectory}'`,
    "$processes = Get-CimInstance Win32_Process -Filter \"Name = 'chrome.exe'\"",
    "$matches = $processes | Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($profile, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 }",
    "$matches | ForEach-Object {",
    "    Write-Output $_.ProcessId",
    "    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue",
    "}"
  ].join("; ");

  try {
    const result = childProcess.spawnSync(
      "powershell.exe",
      [
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy",
        "Bypass",
        "-Command",
        script
      ],
      {
        encoding: "utf8",
        windowsHide: true
      }
    );

    const processIds = (result.stdout || "")
      .split(/\r?\n/)
      .map(value => value.trim())
      .filter(value => /^\d+$/.test(value));

    if (processIds.length > 0) {
      pass(
        `Killed verifier Chrome process(es): ${processIds.join(", ")}`
      );
    }
  }
  catch (error) {
    info(
      `Unable to clean up verifier Chrome processes: ${error.message}`
    );
  }
}

function createTestWorkspace() {
  const workspaceDirectory = fs.mkdtempSync(
    path.join(
      os.tmpdir(),
      "cloudideaas-debugger-workspace-"
    )
  );

  fs.mkdirSync(
    path.join(
      workspaceDirectory,
      ".vscode"
    ),
    {
      recursive: true
    }
  );

  const htmlPath = path.join(
    workspaceDirectory,
    "index.html"
  );

  const html = [
    "<!DOCTYPE html>",
    "<html>",
    "<head>",
    "    <meta charset=\"utf-8\">",
    "    <title>CloudIDEaaS Debugger Verification</title>",
    "</head>",
    "<body>",
    "    <script>",
    "        function verifyDebugger()",
    "        {",
    "            const value = 123;",
    "            const result = value + 1;",
    "            console.log(result);",
    "        }",
    "",
    "        setTimeout(verifyDebugger, 1500);",
    "    </script>",
    "</body>",
    "</html>"
  ].join("\r\n");

  fs.writeFileSync(
    htmlPath,
    html,
    "utf8"
  );

  return {
    workspaceDirectory,
    htmlPath,
    breakpointLine: 12
  };
}

class DapClient {
  constructor(child) {
    this.child = child;
    this.buffer = Buffer.alloc(0);
    this.sequence = 1;
    this.pendingResponses = new Map();
    this.eventWaiters = [];
    this.queuedEvents = [];
    this.stderr = "";

    child.stdout.on(
      "data",
      data => {
        this.buffer = Buffer.concat(
          [
            this.buffer,
            data
          ]
        );

        this.processBuffer();
      }
    );

    child.stderr.on(
      "data",
      data => {
        this.stderr += data.toString(
          "utf8"
        );
      }
    );
  }

  processBuffer() {
    while (true) {
      const headerEnd = this.buffer.indexOf(
        "\r\n\r\n"
      );

      if (headerEnd < 0) {
        return;
      }

      const headerText = this.buffer
        .subarray(
          0,
          headerEnd
        )
        .toString(
          "ascii"
        );

      const match = /Content-Length:\s*(\d+)/i.exec(
        headerText
      );

      if (!match) {
        fail(
          `Malformed DAP header:\n${headerText}`
        );
      }

      const contentLength = Number(
        match[1]
      );

      const bodyStart = headerEnd + 4;
      const bodyEnd = bodyStart + contentLength;

      if (this.buffer.length < bodyEnd) {
        return;
      }

      const bodyBuffer = this.buffer.subarray(
        bodyStart,
        bodyEnd
      );

      this.buffer = this.buffer.subarray(
        bodyEnd
      );

      const bodyText = bodyBuffer.toString(
        "utf8"
      );

      let message;

      try {
        message = JSON.parse(
          bodyText
        );
      }
      catch (error) {
        fail(
          `Invalid JSON received from adapter:\n${bodyText}\n\n${error.message}`
        );
      }

      this.handleMessage(
        message
      );
    }
  }

  handleMessage(message) {
    if (message.type === "response") {
      const pending = this.pendingResponses.get(
        message.request_seq
      );

      if (pending) {
        this.pendingResponses.delete(
          message.request_seq
        );

        clearTimeout(
          pending.timeout
        );

        if (message.success === false) {
          pending.reject(
            new Error(
              `DAP command "${message.command}" failed: ${message.message || "Unknown error"}`
            )
          );

          return;
        }

        pending.resolve(
          message
        );
      }

      return;
    }

    if (message.type === "event") {
      for (
        let index = 0;
        index < this.eventWaiters.length;
        index++
      ) {
        const waiter = this.eventWaiters[index];

        if (
          waiter.eventName === message.event
        ) {
          this.eventWaiters.splice(
            index,
            1
          );

          clearTimeout(
            waiter.timeout
          );

          waiter.resolve(
            message
          );

          return;
        }
      }

      this.queuedEvents.push(
        message
      );
    }
  }

  sendRequest(command, argumentsObject = {}) {
    const sequence = this.sequence++;

    const request = {
      seq: sequence,
      type: "request",
      command,
      arguments: argumentsObject
    };

    const body = Buffer.from(
      JSON.stringify(
        request
      ),
      "utf8"
    );

    const header = Buffer.from(
      `Content-Length: ${body.length}\r\n\r\n`,
      "ascii"
    );

    return new Promise(
      (resolve, reject) => {
        const timeout = setTimeout(
          () => {
            this.pendingResponses.delete(
              sequence
            );

            reject(
              new Error(
                `Timed out waiting for DAP response to "${command}".`
              )
            );
          },
          DAP_TIMEOUT_MS
        );

        this.pendingResponses.set(
          sequence,
          {
            resolve,
            reject,
            timeout
          }
        );

        this.child.stdin.write(
          Buffer.concat(
            [
              header,
              body
            ]
          )
        );
      }
    );
  }

  waitForEvent(eventName, timeoutMilliseconds = DAP_TIMEOUT_MS) {
    const queuedIndex = this.queuedEvents.findIndex(
      message =>
        message.event === eventName
    );

    if (queuedIndex >= 0) {
      const message = this.queuedEvents.splice(
        queuedIndex,
        1
      )[0];

      return Promise.resolve(
        message
      );
    }

    return new Promise(
      (resolve, reject) => {
        const waiter = {
          eventName,
          resolve,
          reject,
          timeout: null
        };

        waiter.timeout = setTimeout(
          () => {
            const index = this.eventWaiters.indexOf(
              waiter
            );

            if (index >= 0) {
              this.eventWaiters.splice(
                index,
                1
              );
            }

            reject(
              new Error(
                `Timed out waiting for DAP "${eventName}" event.`
              )
            );
          },
          timeoutMilliseconds
        );

        this.eventWaiters.push(
          waiter
        );
      }
    );
  }
}

function dumpAdapterLog(workspaceDirectory, stderr) {
  section(
    "ADAPTER DIAGNOSTICS"
  );

  if (
    stderr &&
    stderr.trim()
  ) {
    console.log("");
    console.log(
      "--- STDERR ---"
    );

    console.log(
      stderr.trim()
    );
  }

  const logDirectory = path.join(
    workspaceDirectory,
    ".vscode",
    "logs"
  );

  console.log("");

  console.log(
    `[INFO] Looking for adapter logs in: ${logDirectory}`
  );

  if (!fs.existsSync(logDirectory)) {
    console.log(
      "No adapter log directory was created."
    );

    return;
  }

  const files = fs
    .readdirSync(
      logDirectory
    )
    .filter(
      file =>
        file
          .toLowerCase()
          .endsWith(".log")
    );

  if (files.length === 0) {
    console.log(
      "No .log files were created."
    );

    return;
  }

  files.sort(
    (left, right) => {
      const leftPath = path.join(
        logDirectory,
        left
      );

      const rightPath = path.join(
        logDirectory,
        right
      );

      return (
        fs.statSync(rightPath).mtimeMs -
        fs.statSync(leftPath).mtimeMs
      );
    }
  );

  for (const file of files) {
    const fullPath = path.join(
      logDirectory,
      file
    );

    console.log("");
    console.log(
      `--- ${file} ---`
    );

    const content = fs.readFileSync(
      fullPath,
      "utf8"
    );

    const lines = content.split(
      /\r?\n/
    );

    const tail = lines.slice(
      Math.max(
        0,
        lines.length - 150
      )
    );

    console.log(
      tail.join("\n")
    );
  }
}

async function verifyAdapter(extensionDirectory) {
  section(
    "LAUNCHING PACKAGED DEBUG ADAPTER"
  );

  const adapterPath = path.join(
    extensionDirectory,
    "bin",
    "VSCodeDebugger.exe"
  );

  const test = createTestWorkspace();

  const chromePath = findChrome();

  if (!chromePath) {
    fail(
      "Google Chrome could not be found on this machine."
    );
  }

  const testUrl =
    `http://127.0.0.1:${HTTP_PORT}/index.html`;

  info(
    `Adapter: ${adapterPath}`
  );

  info(
    `Test workspace: ${test.workspaceDirectory}`
  );

  info(
    `Test page: ${test.htmlPath}`
  );

  info(
    `Breakpoint line: ${test.breakpointLine}`
  );

  info(
    `Adapter log directory: ${path.join(test.workspaceDirectory, ".vscode", "logs")}`
  );

  info(
    `Chrome: ${chromePath}`
  );

  info(
    `Debugger URL: ${testUrl}`
  );

  info(
    "The packaged adapter is responsible for starting dotnet serve."
  );

  let child = null;
  let dap = null;

  try {
    child = childProcess.spawn(
      adapterPath,
      [
        test.workspaceDirectory
      ],
      {
        cwd: path.dirname(
          adapterPath
        ),
        windowsHide: true,
        stdio: [
          "pipe",
          "pipe",
          "pipe"
        ]
      }
    );

    dap = new DapClient(
      child
    );

    child.once(
      "exit",
      code => {
        if (
          code !== null &&
          code !== 0
        ) {
          info(
            `Adapter exited with code ${code}.`
          );
        }
      }
    );

    section(
      "DAP INITIALIZE"
    );

    info(
      "Sending DAP initialize request."
    );

    const initializeResponse = await dap.sendRequest(
      "initialize",
      {
        clientID: "cloudideaas-vsix-verifier",
        clientName: "CloudIDEaaS VSIX Verifier",
        adapterID: "cloudideaas-vscode-debugger",
        pathFormat: "path",
        linesStartAt1: true,
        columnsStartAt1: true,
        supportsVariableType: true,
        supportsVariablePaging: true,
        supportsRunInTerminalRequest: false,
        locale: "en-US"
      }
    );

    if (
      initializeResponse.type !== "response" ||
      initializeResponse.command !== "initialize" ||
      initializeResponse.success === false
    ) {
      fail(
        "Invalid DAP initialize response."
      );
    }

    pass(
      "DAP initialize response received."
    );

    section(
      "DAP LAUNCH"
    );

    info(
      "Sending DAP launch request."
    );

    const launchResponse = await dap.sendRequest(
      "launch",
      {
        name: "VSIX Verification",
        type: "cloudideaas-vscode-debugger",
        request: "launch",
        url: testUrl,
        webRoot: test.workspaceDirectory,
        workspaceFolder: test.workspaceDirectory,
        chromePath,
        port: HTTP_PORT
      }
    );

    if (
      launchResponse.type !== "response" ||
      launchResponse.command !== "launch" ||
      launchResponse.success === false
    ) {
      fail(
        "Invalid DAP launch response."
      );
    }

    pass(
      "DAP launch response received."
    );

    const initializedEvent = await dap.waitForEvent(
      "initialized"
    );

    if (
      !initializedEvent ||
      initializedEvent.event !== "initialized"
    ) {
      fail(
        "Invalid DAP initialized event."
      );
    }

    pass(
      "DAP initialized event received."
    );

    section(
      "BREAKPOINT CONFIGURATION"
    );

    const breakpointResponse = await dap.sendRequest(
      "setBreakpoints",
      {
        source: {
          name: "index.html",
          path: test.htmlPath
        },
        breakpoints: [
          {
            line: test.breakpointLine
          }
        ],
        lines: [
          test.breakpointLine
        ],
        sourceModified: false
      }
    );

    if (
      !breakpointResponse.body ||
      !Array.isArray(
        breakpointResponse.body.breakpoints
      )
    ) {
      fail(
        "setBreakpoints response did not contain breakpoints."
      );
    }

    pass(
      `Breakpoint request accepted for line ${test.breakpointLine}.`
    );

    await dap.sendRequest(
      "setExceptionBreakpoints",
      {
        filters: []
      }
    );

    pass(
      "Exception breakpoints configured."
    );

    await dap.sendRequest(
      "configurationDone",
      {}
    );

    pass(
      "configurationDone accepted."
    );

    section(
      "WAITING FOR BREAKPOINT"
    );

    const stoppedEvent = await dap.waitForEvent(
      "stopped",
      BREAKPOINT_TIMEOUT_MS
    );

    if (
      !stoppedEvent.body ||
      !stoppedEvent.body.threadId
    ) {
      fail(
        "Stopped event did not include a threadId."
      );
    }

    const threadId =
      stoppedEvent.body.threadId;

    pass(
      `Breakpoint hit. Thread ID: ${threadId}`
    );

    section(
      "THREADS"
    );

    const threadsResponse = await dap.sendRequest(
      "threads",
      {}
    );

    const threads =
      threadsResponse.body &&
        Array.isArray(
          threadsResponse.body.threads
        )
        ? threadsResponse.body.threads
        : [];

    if (threads.length === 0) {
      fail(
        "threads returned no threads."
      );
    }

    pass(
      `Received ${threads.length} thread(s).`
    );

    section(
      "STACK TRACE"
    );

    const stackResponse = await dap.sendRequest(
      "stackTrace",
      {
        threadId,
        startFrame: 0,
        levels: 20
      }
    );

    const stackFrames =
      stackResponse.body &&
        Array.isArray(
          stackResponse.body.stackFrames
        )
        ? stackResponse.body.stackFrames
        : [];

    if (stackFrames.length === 0) {
      fail(
        "stackTrace returned no stack frames."
      );
    }

    const frame =
      stackFrames[0];

    pass(
      `Stack frame received: ${frame.name || frame.id}`
    );

    if (frame.line) {
      info(
        `Top stack frame line: ${frame.line}`
      );
    }

    section(
      "SCOPES"
    );

    const scopesResponse = await dap.sendRequest(
      "scopes",
      {
        frameId: frame.id
      }
    );

    const scopes =
      scopesResponse.body &&
        Array.isArray(
          scopesResponse.body.scopes
        )
        ? scopesResponse.body.scopes
        : [];

    if (scopes.length === 0) {
      fail(
        "scopes returned no scopes."
      );
    }

    pass(
      `Received ${scopes.length} scope(s).`
    );

    for (const scope of scopes) {
      info(
        `Scope: ${scope.name || "(unnamed)"}, variablesReference=${scope.variablesReference || 0}`
      );
    }

    section(
      "VARIABLES"
    );

    let totalVariables = 0;
    let valueVariable = null;
    let valueScope = null;

    for (const scope of scopes) {
      if (
        !scope.variablesReference ||
        scope.variablesReference <= 0
      ) {
        continue;
      }

      const variablesResponse = await dap.sendRequest(
        "variables",
        {
          variablesReference:
            scope.variablesReference
        }
      );

      const variables =
        variablesResponse.body &&
          Array.isArray(
            variablesResponse.body.variables
          )
          ? variablesResponse.body.variables
          : [];

      info(
        `Scope "${scope.name || "(unnamed)"}" returned ${variables.length} variable(s).`
      );

      totalVariables +=
        variables.length;

      for (const variable of variables) {
        info(
          `Variable: ${variable.name} = ${variable.value}`
        );

        if (
          !valueVariable &&
          variable.name === "value"
        ) {
          valueVariable =
            variable;

          valueScope =
            scope;
        }
      }
    }

    if (totalVariables === 0) {
      fail(
        "variables returned no values from any scope."
      );
    }

    pass(
      `Received ${totalVariables} variable(s) across all scopes.`
    );

    if (!valueVariable) {
      fail(
        "Local variable 'value' was not found in any returned scope."
      );
    }

    pass(
      `Found local variable 'value' in scope "${valueScope.name || "(unnamed)"}".`
    );

    if (
      String(valueVariable.value) !== "123"
    ) {
      fail(
        `Expected local variable 'value' to equal 123, but received ${valueVariable.value}.`
      );
    }

    pass(
      "Local variable value = 123"
    );

    section(
      "EVALUATE"
    );

    const evaluateResponse = await dap.sendRequest(
      "evaluate",
      {
        expression: "value + 1",
        frameId: frame.id,
        context: "watch"
      }
    );

    if (
      !evaluateResponse.body ||
      evaluateResponse.body.result === undefined
    ) {
      fail(
        "evaluate did not return a result."
      );
    }

    pass(
      `evaluate returned: ${evaluateResponse.body.result}`
    );

    if (
      String(evaluateResponse.body.result) !== "124"
    ) {
      fail(
        `Expected evaluate result 124, but received ${evaluateResponse.body.result}.`
      );
    }

    pass(
      "Evaluate result verified as 124."
    );

    section(
      "STEP OVER"
    );

    await dap.sendRequest(
      "next",
      {
        threadId,
        singleThread: false
      }
    );

    pass(
      "next accepted."
    );

    const nextStoppedEvent = await dap.waitForEvent(
      "stopped",
      BREAKPOINT_TIMEOUT_MS
    );

    if (
      !nextStoppedEvent.body ||
      !nextStoppedEvent.body.threadId
    ) {
      fail(
        "Step-over stopped event did not contain a threadId."
      );
    }

    pass(
      "Step-over produced another stopped event."
    );

    section(
      "CONTINUE"
    );

    await dap.sendRequest(
      "continue",
      {
        threadId,
        singleThread: false
      }
    );

    pass(
      "continue accepted."
    );

    section(
      "DISCONNECT"
    );

    try {
      await dap.sendRequest(
        "disconnect",
        {
          restart: false,
          terminateDebuggee: true
        }
      );

      pass(
        "disconnect accepted."
      );
    }
    catch (error) {
      info(
        `Adapter terminated during disconnect: ${error.message}`
      );
    }

    await delay(
      500
    );

    if (
      child &&
      child.exitCode === null
    ) {
      child.kill();
    }

    pass(
      "Packaged debugger smoke test completed successfully."
    );
  }
  catch (error) {
    if (
      child &&
      child.exitCode === null
    ) {
      try {
        child.kill();
      }
      catch {
      }
    }

    dumpAdapterLog(
      test.workspaceDirectory,
      dap
        ? dap.stderr
        : ""
    );

    throw error;
  }
  finally {
    killVerifierChrome(
      test.workspaceDirectory
    );
  }
}

async function main() {
  let extractionDirectory = null;

  try {
    section(
      "CLOUDIDEaaS VSIX VERIFICATION"
    );

    const vsixPath = path.resolve(
      process.cwd(),
      VSIX_FILE
    );

    if (!fs.existsSync(vsixPath)) {
      fail(
        `VSIX not found: ${VSIX_FILE}`
      );
    }

    pass(
      `VSIX found: ${VSIX_FILE}`
    );

    const stats = fs.statSync(
      vsixPath
    );

    info(
      `VSIX size: ${formatBytes(stats.size)}`
    );

    extractionDirectory = fs.mkdtempSync(
      path.join(
        os.tmpdir(),
        "cloudideaas-vsix-"
      )
    );

    info(
      `Extracting VSIX to: ${extractionDirectory}`
    );

    const zip = new AdmZip(
      vsixPath
    );

    zip.extractAllTo(
      extractionDirectory,
      true
    );

    pass(
      "VSIX extracted successfully."
    );

    section(
      "VERIFYING REQUIRED VSIX FILES"
    );

    const packageJsonPath = requireFile(
      extractionDirectory,
      path.join(
        "extension",
        "package.json"
      )
    );

    requireFile(
      extractionDirectory,
      path.join(
        "extension",
        "dist",
        "extension.js"
      )
    );

    requireFile(
      extractionDirectory,
      path.join(
        "extension",
        "bin",
        "VSCodeDebugger.exe"
      )
    );

    const runtimeConfigPath = requireFile(
      extractionDirectory,
      path.join(
        "extension",
        "bin",
        "VSCodeDebugger.runtimeconfig.json"
      )
    );

    requireFile(
      extractionDirectory,
      path.join(
        "extension",
        "bin",
        "VSCodeDebugger.deps.json"
      )
    );

    requireFile(
      extractionDirectory,
      path.join(
        "extension",
        "bin",
        "hostfxr.dll"
      )
    );

    requireFile(
      extractionDirectory,
      path.join(
        "extension",
        "bin",
        "hostpolicy.dll"
      )
    );

    requireFile(
      extractionDirectory,
      path.join(
        "extension",
        "bin",
        "coreclr.dll"
      )
    );

    requireFile(
      extractionDirectory,
      path.join(
        "extension",
        "bin",
        "System.Private.CoreLib.dll"
      )
    );

    section(
      "VERIFYING PACKAGE.JSON"
    );

    const packageJson = readJson(
      packageJsonPath
    );

    if (
      packageJson.publisher !==
      "CloudIDEaaS"
    ) {
      fail(
        `Unexpected publisher: ${packageJson.publisher}`
      );
    }

    pass(
      `Publisher: ${packageJson.publisher}`
    );

    if (
      packageJson.name !==
      "cloudideaas-vscode-debugger"
    ) {
      fail(
        `Unexpected extension name: ${packageJson.name}`
      );
    }

    pass(
      `Extension: ${packageJson.name}`
    );

    if (!packageJson.version) {
      fail(
        "package.json does not contain a version."
      );
    }

    pass(
      `Version: ${packageJson.version}`
    );

    if (
      packageJson.main !==
      "./dist/extension.js"
    ) {
      fail(
        `Unexpected entry point: ${packageJson.main}`
      );
    }

    pass(
      `Entry point: ${packageJson.main}`
    );

    section(
      "VERIFYING SELF-CONTAINED .NET RUNTIME"
    );

    const runtimeConfig = readJson(
      runtimeConfigPath
    );

    const runtimeOptions =
      runtimeConfig.runtimeOptions || {};

    if (
      Array.isArray(
        runtimeOptions.frameworks
      ) &&
      runtimeOptions.frameworks.length > 0
    ) {
      fail(
        "runtimeconfig.json contains frameworks. The adapter is framework-dependent."
      );
    }

    const includedFrameworks =
      runtimeOptions.includedFrameworks;

    if (
      !Array.isArray(
        includedFrameworks
      ) ||
      includedFrameworks.length === 0
    ) {
      fail(
        "runtimeconfig.json does not contain includedFrameworks."
      );
    }

    pass(
      "runtimeconfig.json uses includedFrameworks."
    );

    for (const framework of includedFrameworks) {
      pass(
        `${framework.name}: ${framework.version}`
      );
    }

    const extensionDirectory = path.join(
      extractionDirectory,
      "extension"
    );

    await verifyAdapter(
      extensionDirectory
    );

    section(
      "VSIX VERIFICATION PASSED"
    );

    console.log(
      "The packaged VSIX passed structural, runtime, dotnet serve, DAP, Chrome, breakpoint, thread, stack, scope, variable, evaluation, stepping, continue, and disconnect verification."
    );

    process.exitCode = 0;
  }
  catch (error) {
    section(
      "VSIX VERIFICATION FAILED"
    );

    console.error(
      error.stack ||
      error.message ||
      error
    );

    process.exitCode = 1;
  }
  finally {
    if (
      extractionDirectory &&
      fs.existsSync(extractionDirectory)
    ) {
      try {
        fs.rmSync(
          extractionDirectory,
          {
            recursive: true,
            force: true
          }
        );
      }
      catch {
      }
    }
  }
}

main().then(
  () => {
    process.exit(
      process.exitCode || 0
    );
  }
);