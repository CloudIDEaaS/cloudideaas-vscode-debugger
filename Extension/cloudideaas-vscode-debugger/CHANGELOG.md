# Change Log

All notable changes to CloudIDEaaS VSCode Debugger will be documented in this file.

## 0.1.0

Initial public release.

### Added

- Visual Studio Code debug adapter integration for browser-based JavaScript, TypeScript, and HTML projects.
- C# Debug Adapter Protocol implementation.
- Chrome DevTools Protocol connection over WebSocket.
- Integrated local web-server startup for debugging static browser projects.
- Chrome launch and target discovery.
- Startup sequencing that configures breakpoints before navigating to the application URL.
- Source breakpoints with breakpoint-resolution synchronization back to VS Code.
- Conditional breakpoint support.
- Exception breakpoint configuration.
- Continue, pause, step over, step into, and step out operations.
- Thread and stack-trace support.
- Scope and variable inspection.
- Expression evaluation.
- Variable modification support for supported scopes.
- Loaded-source and source retrieval support.
- Breakpoint-location discovery.
- Frame restart support.
- Debug session termination and disconnect handling.
- Asynchronous call-stack tracking through Chrome DevTools Protocol.
- Start Debugging and Stop Debugging commands in the editor title area.
- Newtonsoft.Json-based JSON handling throughout the C# CDP implementation.

### Platform

- Initial Marketplace package targets Windows x64.

### Known Limitations

- Advanced source-map scenarios are not yet fully supported.
- Multi-target browser debugging, including workers and multiple tabs, is limited.
- Some advanced Debug Adapter Protocol capabilities are intentionally not implemented in this release.
