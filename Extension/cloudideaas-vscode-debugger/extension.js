// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
const vscode = require('vscode');
const path = require('path');
const fs = require('fs');
let workspaceFolder = null;

// This method is called when your extension is activated
// Your extension is activated the very first time the command is executed

/**
 * @param {vscode.ExtensionContext} context
 */
function activate(context) {

	const startCommandHandler = vscode.commands.registerCommand('cloudideaas-vscode-debugger.start', async () => {

		// 1. Shift focus from the sidebar back to the active text editor window
		await vscode.commands.executeCommand('workbench.action.focusActiveEditorGroup');

		// 2. Fire the debugger now that a file context is active
		await vscode.commands.executeCommand('workbench.action.debug.start');
		await vscode.commands.executeCommand('workbench.debug.action.focusRepl');
	});

	context.subscriptions.push(startCommandHandler);

	const stopCommandHandler = vscode.commands.registerCommand('cloudideaas-vscode-debugger.stop', async () => {

		// 1. Shift focus from the sidebar back to the active text editor window
		await vscode.commands.executeCommand('workbench.action.focusActiveEditorGroup');

		// 2. Stop the debugger
		await vscode.commands.executeCommand('workbench.action.debug.stop');
	});

	context.subscriptions.push(stopCommandHandler);

	const provider = {
		resolveDebugConfiguration(folder, config) {

			if (folder) {
				config.workspaceFolder = folder.uri.fsPath;
				workspaceFolder = folder.uri.fsPath;
			}

			return config;
		}
	};

	context.subscriptions.push(
		vscode.debug.registerDebugConfigurationProvider(
			'cloudideaas-vscode-debugger',
			provider
		)
	);

	const factory = {
		createDebugAdapterDescriptor() {

			try {
				const adapterPath = path.join(
					context.extensionPath,
					"bin",
					"VSCodeDebugger.exe"
				);

				if (!fs.existsSync(adapterPath)) {
					vscode.window.showErrorMessage(
						`VSCode Debug Adapter not found: ${adapterPath}`
					);
					return undefined;
				}

				console.error(`[ADAPTER PATH] ${adapterPath}`);
				console.error(`[WORKSPACE] ${workspaceFolder}`);

				return new vscode.DebugAdapterExecutable(
					adapterPath,
					[workspaceFolder]
				);
			}
			catch (error) {
				vscode.window.showErrorMessage(
					`Error creating debug adapter descriptor: ${error.message}`
				);
				return undefined;

			}
		}
	};

	context.subscriptions.push(
		vscode.debug.registerDebugAdapterDescriptorFactory(
			"cloudideaas-vscode-debugger",
			factory
		)
	);

	context.subscriptions.push(
		vscode.debug.registerDebugAdapterTrackerFactory(
			'cloudideaas-vscode-debugger',
			{
				createDebugAdapterTracker(session) {
					return {
						onWillStartSession() {
							console.error('[TRACKER] Debug session starting');
							vscode.commands.executeCommand('setContext', 'cloudideaas.debugging', true);
						},

						onWillReceiveMessage(message) {
							console.error(
								'[VS CODE -> ADAPTER]',
								JSON.stringify(message, null, 2)
							);
						},

						onDidSendMessage(message) {
							console.error(
								'[ADAPTER -> VS CODE]',
								JSON.stringify(message, null, 2)
							);
						},

						onWillStopSession() {
							console.error('[TRACKER] Debug session stopping');
							vscode.commands.executeCommand('setContext', 'cloudideaas.debugging', false);
						},

						onError(error) {
							console.error(
								'[TRACKER ERROR]',
								error
							);
						},

						onExit(code, signal) {
							console.error(
								`[ADAPTER EXIT] code=${code} signal=${signal}`
							);
						}
					};
				}
			}
		)
	);
}

function deactivate() {
	console.log("Deactivating extension");
}

module.exports = {
	activate,
	deactivate
};