# CloudIDEaaS JavaScript Debugger

[![Version](https://vsmarketplacebadges.dev/version/CloudIDEaaS.cloudideaas-vscode-debugger.svg)](https://marketplace.visualstudio.com/items?itemName=CloudIDEaaS.cloudideaas-vscode-debugger)
[![Installs](https://vsmarketplacebadges.dev/installs/CloudIDEaaS.cloudideaas-vscode-debugger.svg)](https://marketplace.visualstudio.com/items?itemName=CloudIDEaaS.cloudideaas-vscode-debugger)
[![Downloads](https://vsmarketplacebadges.dev/downloads/CloudIDEaaS.cloudideaas-vscode-debugger.svg)](https://marketplace.visualstudio.com/items?itemName=CloudIDEaaS.cloudideaas-vscode-debugger)
[![Rating](https://vsmarketplacebadges.dev/rating/CloudIDEaaS.cloudideaas-vscode-debugger.svg)](https://marketplace.visualstudio.com/items?itemName=CloudIDEaaS.cloudideaas-vscode-debugger)

**Press F5 and debug your browser JavaScript directly from Visual Studio Code.**

CloudIDEaaS JavaScript Debugger gives you a straightforward debugging workflow for JavaScript and HTML projects: it starts your local web server, launches Chrome, connects the debugger, and lets you use breakpoints, stepping, variables, call stacks, and expression evaluation from inside VS Code.

Built around **convention over configuration**, it's designed for developers who want to spend their time debugging their application—not debugging their debugging environment.

## JavaScript Debugging Without the Debugging Hassle

![CloudIDEaaS JavaScript Debugger paused at a JavaScript breakpoint in Visual Studio Code](Media/cloudideaas-javascript-debugger-breakpoint.jpg)

Set a breakpoint, press **F5**, and debug your browser JavaScript without assembling a separate debugging environment.

## Why CloudIDEaaS?

CloudIDEaaS is built around **convention over configuration**: fewer moving pieces between your code and an active browser debugging session.

If you'd like to see the complete breakdown of the features, advantages, benefits, ideal use cases, and how CloudIDEaaS fits alongside other debugging tools:

**[Why CloudIDEaaS JavaScript Debugger?](https://cloudideaas.blogspot.com/p/why-cloudideaas-javascript-debugger.html)**

## Features

- **F5 browser debugging** — Start your web application and Chrome debugging session directly from VS Code.
- **Built-in local web server** — Get straightforward JavaScript and HTML projects running without configuring a separate development server.
- **Startup breakpoints** — Breakpoints are configured before your application loads, helping you catch problems in startup code.
- **Full stepping controls** — Step over, step into, step out, continue, and pause execution.
- **Runtime inspection** — View call stacks, scopes, local variables, and object values while your application is running.
- **Expression evaluation** — Evaluate expressions and modify supported variable values while paused.
- **Advanced breakpoints** — Supports source breakpoints, conditional breakpoints, and exception breakpoint configuration.
- **Chrome DevTools Protocol** — Communicates directly with Chrome through CDP while providing the VS Code debugging experience.

## Getting Started

### Start Debugging

Create a `.vscode/launch.json` file with a CloudIDEaaS debugger configuration:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Debug in Chrome",
            "type": "cloudideaas-vscode-debugger",
            "request": "launch",
            "url": "http://localhost:8000/index.html"
        }
    ]
}
```

Set a breakpoint in your JavaScript and press **F5**.

CloudIDEaaS starts the local web server, launches Chrome, connects the debugger, configures your breakpoints, and then loads your application.

### Want to Learn More?

Read our free online book, **Visual Studio Code Browser Debugging from the Ground Up**, for a complete guide to using the debugger and understanding how browser debugging works.

[![Visual Studio Code Browser Debugging from the Ground Up](Media/Book.png)](https://publications.lavedajones.com/vscode-debugger/index.html)

## Requirements

- **Windows:** 64-bit Windows (x64)
- **Visual Studio Code:** Version 1.108.0 or later
- **Browser:** Google Chrome

The extension includes the CloudIDEaaS C# debug adapter and its required runtime components. No separate .NET installation is required.

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

For a deeper technical walkthrough:

**[How a VS Code Debugger Actually Works: DAP, CDP, and a C# Debug Adapter](https://dev.to/kenlnetherland/how-a-vs-code-debugger-actually-works-dap-cdp-and-a-c-debug-adapter-2ddl)**

## Roadmap

[![CloudIDEaaS JavaScript Debugger Roadmap](media/roadmap/cloudideaas-javascript-debugger-roadmap-hero.png)](https://publications.lavedajones.com/vscode-debugger/roadmap.html)

CloudIDEaaS JavaScript Debugger started with a simple goal: **press F5 and debug browser JavaScript without turning the debugging environment into its own project.**

The roadmap expands that idea while keeping the same focus on simplicity and reducing developer friction.

Planned directions include:

- Support for additional browsers and devices
- More web server options and `launch.json` configuration
- Advanced debugging capabilities
- Plugin-based support for additional languages and runtimes
- Integrated support for frameworks such as React, Angular, Vue, and Flutter
- Container and cross-machine debugging
- Expanded DAP and CDP protocol support
- Pluggable browser and runtime debugging protocols
- Support for additional IDEs
- Additional VS Code utilities built on CDP domains
- Playwright integration
- AI-assisted development of new debugging capabilities

The long-term direction is to evolve CloudIDEaaS from a focused JavaScript debugger into a more extensible debugging platform — without losing the straightforward **F5 → breakpoint → debug** experience that motivated the project in the first place.

➡️ **[Explore the Full CloudIDEaaS JavaScript Debugger Roadmap](https://publications.lavedajones.com/vscode-debugger/roadmap.html)**
## Known Limitations

CloudIDEaaS is deliberately focused on straightforward browser-debugging workflows and is not intended to duplicate every feature of Microsoft's JavaScript debugger.

Current limitations include:

- Windows x64 only.
- Advanced source-map and bundled-application scenarios may require additional support.
- Multi-target debugging such as workers, multiple tabs, and complex browser target topologies is limited.
- Reverse debugging, instruction breakpoints, data breakpoints, and disassembly are not currently provided.

## Contributing

Contributions, bug reports, ideas, and technical feedback are welcome.

If you want to contribute without including dependent projects, change references to:

```text
Extension\cloudideaas-vscode-debugger\bin\
```

Also set the following in `VSCodeDebugger.csproj` to `false`:

```xml
<TrimUnusedAssemblies>false</TrimUnusedAssemblies>
```

You will need to perform the above steps when working from a fork without the full dependent solution.

If you want to contribute using the full solution, contact us and we'll help you get the development environment configured.

## Building and Publishing the Extension

The extension project includes npm scripts that automate the release workflow. Run these commands from the `Extension\cloudideaas-vscode-debugger` directory.

The normal release workflow can:

```text
Bump the extension version
        |
        v
Publish the C# debug adapter as Release / x64
        |
        v
Bundle extension.js with esbuild
        |
        v
List the files that VSCE will package
        |
        v
Create the win32-x64 VSIX
        |
        v
Verify the packaged VSIX
        |
        v
Optionally publish that same VSIX to the Visual Studio Marketplace
```

### Create a VSIX Without Publishing

For a normal patch release:

```cmd
npm run release:patch
```

This bumps the patch version, publishes the C# debug adapter, bundles the extension, displays the VSCE file list, creates the VSIX, and verifies the packaged artifact without publishing it to the Marketplace.

For minor or major releases:

```cmd
npm run release:minor
npm run release:major
```

### Create a VSIX Without Rebuilding the C# Adapter

If the C# debug adapter has already been published and the extension's `bin` directory contains the files you want to package, use the `skip-adapter` variant:

```cmd
npm run release:patch:skip-adapter
```

Minor and major versions are also available:

```cmd
npm run release:minor:skip-adapter
npm run release:major:skip-adapter
```

These commands leave the existing adapter files in place and perform the version bump, JavaScript bundle, VSCE file listing, VSIX packaging, and verification.

### Package and Publish to the Marketplace

To perform the complete workflow and publish the resulting verified VSIX to the Visual Studio Marketplace:

```cmd
npm run publish:patch
```

For minor or major releases:

```cmd
npm run publish:minor
npm run publish:major
```

The publish scripts package and verify the extension first and then publish that same generated VSIX.

### Publish Without Rebuilding the C# Adapter

If the adapter files are already prepared and tested:

```cmd
npm run publish:patch:skip-adapter
```

Minor and major variants are also available:

```cmd
npm run publish:minor:skip-adapter
npm run publish:major:skip-adapter
```

### Recommended Release Workflow

For a release that you want to test before publishing:

```cmd
npm run release:patch
```

Install and test the generated VSIX locally before publishing it.

For a fully automated release when the adapter and extension have already been validated:

```cmd
npm run publish:patch
```

Use `minor` or `major` in place of `patch` when appropriate.

## Troubleshooting the Debug Adapter

The extension includes a PowerShell troubleshooting script at:

```text
scripts\Test-DebugAdapter.ps1
```

This script exercises the C# debug adapter directly using the Debug Adapter Protocol (DAP), independently of Visual Studio Code. It can help determine whether a problem is occurring in the packaged debug adapter/runtime or in the Visual Studio Code extension integration.

The simulator performs a basic debugging sequence through launch and shutdown:

```text
initialize
launch
setBreakpoints
setExceptionBreakpoints
configurationDone
disconnect
```

### Finding the Installed Extension Directory

Visual Studio Code normally installs extensions under:

```text
%USERPROFILE%\.vscode\extensions
```

Open that directory in File Explorer, or from PowerShell run:

```powershell
explorer "$env:USERPROFILE\.vscode\extensions"
```

Look for the CloudIDEaaS JavaScript Debugger folder. Its name will include the publisher, extension name, version, and may include the target platform, for example:

```text
cloudideaas.cloudideaas-vscode-debugger-0.1.35-win32-x64
```

You can also locate the extension from Visual Studio Code:

1. Open the **Extensions** view.
2. Find **CloudIDEaaS JavaScript Debugger** under installed extensions.
3. Open the extension's gear/menu.
4. Choose **Open Extension Folder** if that option is available.

Once you have located the installed extension directory, open PowerShell in that directory. You should see folders such as `bin`, `dist`, `resources`, and `scripts`.

### Running the Troubleshooting Script

From the installed extension directory, run:

```powershell
.\scripts\Test-DebugAdapter.ps1 `
    -DebuggerPath ".\bin\VSCodeDebugger.exe" `
    -Url "http://localhost:8000/index.html" `
    -WebRoot "C:\Path\To\Your\Project"
```

To test a specific breakpoint, also provide the source file and line number:

```powershell
.\scripts\Test-DebugAdapter.ps1 `
    -DebuggerPath ".\bin\VSCodeDebugger.exe" `
    -Url "http://localhost:8000/index.html" `
    -WebRoot "C:\Path\To\Your\Project" `
    -BreakpointFile "C:\Path\To\Your\Project\index.html" `
    -BreakpointLine 25
```

If `-BreakpointFile` is omitted, the script uses `index.html` under the specified `WebRoot`.

### Interpreting the Results

If the script successfully completes the DAP launch sequence and disconnects normally, the packaged C# debug adapter and its supporting runtime are able to start and respond to the basic DAP requests. A problem that occurs only when debugging through Visual Studio Code is therefore more likely to involve extension integration, launch configuration, Chrome startup, or the specific debugging scenario.

If the script fails before completing the launch sequence, include its console output when reporting the issue. This can help identify missing runtime files, adapter startup failures, DAP request failures, or other packaging/runtime problems.

## Reporting Issues

When reporting a debugger problem, please include:

- Visual Studio Code version.
- Windows version.
- Chrome version.
- Relevant `launch.json` configuration.
- Whether the issue occurs during launch, breakpoint setup, stepping, variable inspection, or shutdown.
- Any relevant extension/debug-adapter log output.
- Output from `scripts\Test-DebugAdapter.ps1`, if the troubleshooting script also reproduces the problem.

Report issues on the **[CloudIDEaaS JavaScript Debugger GitHub Issues page](https://github.com/CloudIDEaaS/cloudideaas-vscode-debugger/issues)**.

## Learn More

### Why CloudIDEaaS?

For the complete features, advantages, benefits, use cases, and product positioning:

**[Why CloudIDEaaS JavaScript Debugger?](https://cloudideaas.blogspot.com/p/why-cloudideaas-javascript-debugger.html)**

### How the Debugger Works

For a technical walkthrough of DAP, CDP, startup breakpoints, state management, variables, scopes, and the C# debug adapter:

**[How a VS Code Debugger Actually Works: DAP, CDP, and a C# Debug Adapter](https://dev.to/kenlnetherland/how-a-vs-code-debugger-actually-works-dap-cdp-and-a-c-debug-adapter-2ddl)**

### Development Philosophy

For the broader philosophy behind reducing developer-tool complexity:

**[When Developer Tools Become the Problem: Why I Built a Simpler JavaScript Debugger](https://publications.lavedajones.com/vscode-debugger/index.html)**

## Release Notes

See [CHANGELOG.md](CHANGELOG.md) for release history.

## License

See the repository's `LICENSE` file for licensing information.