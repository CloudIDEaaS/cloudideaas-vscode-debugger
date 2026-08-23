# CloudIDEaaS VSCode Debugger

A lightweight Visual Studio Code debugger for browser-based JavaScript, TypeScript, and HTML projects.

CloudIDEaaS VSCode Debugger is designed around **convention over configuration**: start debugging from VS Code, launch the local web application and Chrome debugging session, set breakpoints, step through code, and inspect runtime values without relying on `console.log` or constantly switching to browser DevTools.

## Features

- Launch browser debugging directly from Visual Studio Code.
- Starts a local web server for the current workspace.
- Launches Chrome with the Chrome DevTools Protocol (CDP) enabled.
- Supports source breakpoints.
- Supports conditional breakpoints and exception breakpoint configuration.
- Step over, step into, and step out.
- Continue and pause execution.
- View call stacks and scopes.
- Inspect local and object variable values.
- Evaluate expressions while debugging.
- Modify supported variable values.
- View loaded JavaScript sources.
- Handles breakpoint resolution after the application is loaded.
- Editor-title Start Debugging and Stop Debugging buttons.

## Getting Started

Read our free online book on [![Visual Studio Code Browser Debugging from the Ground Up](Media/Book.png)](https://publications.lavedajones.com/vscode-debugger/index.html)
for a complete guide to using and understanding the debugger.

Create a VS Code debug configuration using the `cloudideaas-vscode-debugger` debugger type and specify the URL for the page you want to debug.

Example `.vscode/launch.json`:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Chrome Explicit",
            "type": "cloudideaas-vscode-debugger",
            "request": "launch",
            "url": "http://localhost:8000/index.html"
        }
    ]
}
```

Set a breakpoint in your browser-side code and press **F5**, or use the Start Debugging button in the editor title area.

The debugger launches Chrome initially without loading the application, establishes the debugging connection, configures your breakpoints, and then navigates to the requested URL. This allows breakpoints in startup code to be active before the application begins executing.

## Requirements

The initial Marketplace release is intended for **64-bit Windows**.

The extension includes its C# debug adapter and supporting runtime assemblies in the extension package. Chrome must be available on the system for browser debugging.

## Debug Configuration

### `url`

The application URL to launch and debug.

Example:

```json
"url": "http://localhost:8000/index.html"
```

### `port`

Chrome remote-debugging port. The default is `9222`.

Example:

```json
"port": 9222
```

## How It Works

The extension provides the Visual Studio Code integration layer while a C# debug adapter communicates with VS Code using the Debug Adapter Protocol (DAP). The adapter communicates with Chrome using the Chrome DevTools Protocol (CDP).

```text
Visual Studio Code
        |
        | DAP
        v
CloudIDEaaS C# Debug Adapter
        |
        | CDP / WebSocket
        v
      Chrome
```

This architecture allows VS Code breakpoints, stepping, scopes, variables, expression evaluation, and other debugging operations to be translated into Chrome debugging operations.

## Known Limitations

This is an early release and is not intended to duplicate every feature of Microsoft's JavaScript debugger.

Current limitations may include:

- Windows x64 only for the initial release.
- Advanced source-map and bundled-application scenarios may require additional support.
- Multi-target debugging such as workers, multiple tabs, and complex browser target topologies is limited.
- Advanced DAP features such as reverse debugging, instruction breakpoints, data breakpoints, and disassembly are not currently provided.

## Reporting Issues

When reporting a debugger problem, please include:

- Visual Studio Code version.
- Windows version.
- Chrome version.
- Relevant `launch.json` configuration.
- Whether the issue occurs during launch, breakpoint setup, stepping, variable inspection, or shutdown.
- Any relevant extension/debug-adapter log output.

## Release Notes

See [CHANGELOG.md](CHANGELOG.md) for release history.

## License

See the repository's `LICENSE` file for licensing information.
