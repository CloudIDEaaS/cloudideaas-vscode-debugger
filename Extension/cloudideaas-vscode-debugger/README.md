# CloudIDEaaS JavaScript Debugger

[badges]

**Press F5 and debug your browser JavaScript directly from Visual Studio Code.**

CloudIDEaaS JavaScript Debugger gives you a straightforward debugging workflow...

Built around **convention over configuration**, it's designed for developers who want to spend their time debugging their application—not debugging their debugging environment.

## JavaScript Debugging Without the Debugging Hassle

Press **F5** and CloudIDEaaS starts your local web server, launches Chrome, connects the debugger, configures your breakpoints, and loads your application.

![CloudIDEaaS JavaScript Debugger for Visual Studio Code paused at a JavaScript breakpoint in Chrome, showing the call stack, variable values, debugging controls, and Debug Console](Media/cloudideaas-javascript-debugger-breakpoint.jpg)

Set breakpoints, step through your JavaScript, inspect variables and call stacks, and evaluate your application directly from Visual Studio Code.

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

## Requirements

- **Windows:** 64-bit Windows (x64)
- **Visual Studio Code:** Version 1.108.0 or later
- **Browser:** Google Chrome

The extension includes the CloudIDEaaS C# debug adapter and its required runtime components. No separate .NET installation is required.

## Debug Configuration

### `url`

The URL of the web application you want to launch and debug.

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

CloudIDEaaS keeps browser debugging straightforward while using the same industry-standard protocols behind modern debugging tools.

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

The extension communicates with Visual Studio Code using the **Debug Adapter Protocol (DAP)** and with Chrome using the **Chrome DevTools Protocol (CDP)**.

This allows you to use familiar VS Code debugging features while your application runs directly in Chrome.

## Known Limitations

CloudIDEaaS is focused on providing a straightforward browser-debugging experience rather than duplicating every feature of larger JavaScript debugging platforms.

Current limitations include:

- Windows x64 only.
- Advanced source-map and bundled-application scenarios may require additional support.
- Multi-target debugging such as workers and multiple browser tabs is limited.
- Reverse debugging, instruction breakpoints, data breakpoints, and disassembly are not currently supported.

## Contributing

Contributions are welcome.

If you fork the project without the full CloudIDEaaS solution, update the project references to use the assemblies in:

```text
Extension\cloudideaas-vscode-debugger\bin\
```

Also disable dependency trimming in `VSCodeDebugger.csproj`:

```xml
<TrimUnusedAssemblies>false</TrimUnusedAssemblies>
```

If you want to contribute using the full solution and need help getting the development environment running, open an issue and we'll help you get started.

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
Optionally publish the VSIX to the Visual Studio Marketplace
```

### Create a VSIX Without Publishing

For a normal patch release:

```cmd
npm run release:patch
```

This bumps the patch version, publishes the C# debug adapter, bundles the extension, displays the VSCE file list, and creates the VSIX without publishing it to the Marketplace.

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

These commands leave the existing adapter files in place and perform the version bump, JavaScript bundle, VSCE file listing, and VSIX packaging.

### Package and Publish to the Marketplace

To perform the complete workflow and publish the resulting VSIX to the Visual Studio Marketplace:

```cmd
npm run publish:patch
```

For minor or major releases:

```cmd
npm run publish:minor
npm run publish:major
```

The publish scripts package the extension first and then publish that generated VSIX.

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

Look for the CloudIDEaaS VSCode Debugger folder. Its name will include the publisher, extension name, version, and may include the target platform, for example:

```text
cloudideaas.cloudideaas-vscode-debugger-0.1.7-win32-x64
```

You can also locate the extension from Visual Studio Code:

1. Open the **Extensions** view.
2. Find **CloudIDEaaS VSCode Debugger** under installed extensions.
3. Open the extension's gear/menu.
4. Choose **Open Extension Folder** if that option is available.

Once you have located the installed extension directory, open PowerShell in that directory. You should see folders such as `bin`, `dist`, `resources`, and `scripts`.

### Copying Development Builds to the Installed Extension

When developing or troubleshooting the C# debug adapter, it can be useful to copy the latest Visual Studio build output directly into the `bin` directory of an installed CloudIDEaaS VSCode Debugger extension. This allows changes to the C# adapter to be tested through the normally installed Visual Studio Code extension without rebuilding, packaging, publishing, and reinstalling the entire VSIX for every change.

The standard Visual Studio Code extension directory is located under the current Windows user's profile:

```text
%USERPROFILE%\.vscode\extensions
```

In MSBuild, the equivalent user-independent path can be referenced using:

```xml
$(UserProfile)
```

Because Visual Studio Code includes the extension version in the installed directory name, define the version as an MSBuild property rather than embedding it throughout the project:

```xml
<PropertyGroup>
    <InstalledVSCodeExtensionVersion>0.1.33</InstalledVSCodeExtensionVersion>
</PropertyGroup>
```

The following target copies the current project build output into the installed extension after a successful build:

```xml
<Target Name="CopyToInstalledVSCodeExtension" AfterTargets="Build">
    <PropertyGroup>
        <InstalledVSCodeExtensionBin>$(UserProfile)\.vscode\extensions\cloudideaas.cloudideaas-vscode-debugger-$(InstalledVSCodeExtensionVersion)-win32-x64\bin</InstalledVSCodeExtensionBin>
    </PropertyGroup>

    <MakeDir Directories="$(InstalledVSCodeExtensionBin)" />

    <ItemGroup>
        <InstalledExtensionFiles Include="$(TargetDir)*.*" />
    </ItemGroup>

    <Copy
        SourceFiles="@(InstalledExtensionFiles)"
        DestinationFolder="$(InstalledVSCodeExtensionBin)"
        SkipUnchangedFiles="true"
    />
</Target>
```

For example, with:

```xml
<InstalledVSCodeExtensionVersion>0.1.33</InstalledVSCodeExtensionVersion>
```

the destination resolves to a path similar to:

```text
C:\Users\<user>\.vscode\extensions\cloudideaas.cloudideaas-vscode-debugger-0.1.33-win32-x64\bin
```

When a new Marketplace version of the extension is installed, update `InstalledVSCodeExtensionVersion` to match the installed version.

This technique intentionally copies only the current build output over the existing extension `bin` directory. It does not delete the destination directory first. This is important when the installed extension contains self-contained .NET runtime files or other packaged dependencies that are not present in a normal Visual Studio build output.

This target is intended as a development and troubleshooting convenience. Production extension packages should continue to be created through the normal publish and VSIX packaging workflow.

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

## Release Notes

See [CHANGELOG.md](CHANGELOG.md) for release history.

## License

See the repository's `LICENSE` file for licensing information.
