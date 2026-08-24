param
(
    [string]$DebuggerPath = ".\bin\VSCodeDebugger.exe",
    [string]$Url = "http://localhost:8000/index.html",
    [string]$WebRoot = $PWD.Path,
    [string]$BreakpointFile = "",
    [int]$BreakpointLine = 1
)

$ErrorActionPreference = "Stop"

function Send-DapMessage
{
    param
    (
        [System.IO.StreamWriter]$Writer,
        [hashtable]$Message
    )

    $json = $Message | ConvertTo-Json -Depth 20 -Compress
    $length = [System.Text.Encoding]::UTF8.GetByteCount($json)

    $packet = "Content-Length: $length`r`n`r`n$json"

    Write-Host ""
    Write-Host ">>> VS CODE -> ADAPTER"
    Write-Host $json

    $Writer.Write($packet)
    $Writer.Flush()
}

function Read-DapMessage
{
    param
    (
        [System.IO.StreamReader]$Reader
    )

    $contentLength = 0

    while ($true)
    {
        $line = $Reader.ReadLine()

        if ($null -eq $line)
        {
            return $null
        }

        if ($line.Length -eq 0)
        {
            break
        }

        if ($line.StartsWith("Content-Length:", [System.StringComparison]::OrdinalIgnoreCase))
        {
            $contentLength = [int]$line.Substring("Content-Length:".Length).Trim()
        }
    }

    if ($contentLength -le 0)
    {
        return $null
    }

    $buffer = New-Object char[] $contentLength
    $totalRead = 0

    while ($totalRead -lt $contentLength)
    {
        $read = $Reader.Read(
            $buffer,
            $totalRead,
            $contentLength - $totalRead
        )

        if ($read -le 0)
        {
            break
        }

        $totalRead += $read
    }

    $json = -join $buffer[0..($totalRead - 1)]

    Write-Host ""
    Write-Host "<<< ADAPTER -> VS CODE"
    Write-Host $json

    return $json | ConvertFrom-Json
}

function Read-UntilResponse
{
    param
    (
        [System.IO.StreamReader]$Reader,
        [int]$RequestSequence
    )

    while ($true)
    {
        $message = Read-DapMessage -Reader $Reader

        if ($null -eq $message)
        {
            return $null
        }

        if ($message.type -eq "response" -and
            $message.request_seq -eq $RequestSequence)
        {
            return $message
        }
    }
}

if (!(Test-Path $DebuggerPath))
{
    throw "Debugger executable not found: $DebuggerPath"
}

$DebuggerPath = (Resolve-Path $DebuggerPath).Path

if ([string]::IsNullOrWhiteSpace($BreakpointFile))
{
    $BreakpointFile = Join-Path $WebRoot "index.html"
}

Write-Host "Starting:"
Write-Host $DebuggerPath
Write-Host ""

$startInfo = New-Object System.Diagnostics.ProcessStartInfo

$startInfo.FileName = $DebuggerPath
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true

$process = New-Object System.Diagnostics.Process
$process.StartInfo = $startInfo

try
{
    if (!$process.Start())
    {
        throw "Unable to start debugger."
    }

    $writer = $process.StandardInput
    $reader = $process.StandardOutput

    $writer.AutoFlush = $true

    #
    # INITIALIZE
    #

    $sequence = 1

    Send-DapMessage `
        -Writer $writer `
        -Message @{
            seq = $sequence
            type = "request"
            command = "initialize"
            arguments = @{
                clientID = "vscode"
                clientName = "Visual Studio Code"
                adapterID = "cloudideaas-vscode-debugger"
                pathFormat = "path"
                linesStartAt1 = $true
                columnsStartAt1 = $true
                supportsVariableType = $true
                supportsVariablePaging = $true
                supportsRunInTerminalRequest = $false
                locale = "en-US"
            }
        }

    $response = Read-UntilResponse `
        -Reader $reader `
        -RequestSequence $sequence

    if ($null -eq $response -or !$response.success)
    {
        throw "Initialize failed."
    }

    #
    # LAUNCH
    #

    $sequence++

    Send-DapMessage `
        -Writer $writer `
        -Message @{
            seq = $sequence
            type = "request"
            command = "launch"
            arguments = @{
                type = "cloudideaas-vscode-debugger"
                request = "launch"
                name = "PowerShell Simulator"
                url = $Url
                webRoot = $WebRoot
            }
        }

    $response = Read-UntilResponse `
        -Reader $reader `
        -RequestSequence $sequence

    if ($null -eq $response -or !$response.success)
    {
        throw "Launch failed."
    }

    #
    # SET BREAKPOINTS
    #

    $sequence++

    Send-DapMessage `
        -Writer $writer `
        -Message @{
            seq = $sequence
            type = "request"
            command = "setBreakpoints"
            arguments = @{
                source = @{
                    name = [System.IO.Path]::GetFileName($BreakpointFile)
                    path = $BreakpointFile
                }
                breakpoints = @(
                    @{
                        line = $BreakpointLine
                        column = 1
                    }
                )
                sourceModified = $false
            }
        }

    $response = Read-UntilResponse `
        -Reader $reader `
        -RequestSequence $sequence

    if ($null -eq $response -or !$response.success)
    {
        throw "SetBreakpoints failed."
    }

    #
    # SET EXCEPTION BREAKPOINTS
    #

    $sequence++

    Send-DapMessage `
        -Writer $writer `
        -Message @{
            seq = $sequence
            type = "request"
            command = "setExceptionBreakpoints"
            arguments = @{
                filters = @()
            }
        }

    $response = Read-UntilResponse `
        -Reader $reader `
        -RequestSequence $sequence

    if ($null -eq $response -or !$response.success)
    {
        throw "SetExceptionBreakpoints failed."
    }

    #
    # CONFIGURATION DONE
    #

    $sequence++

    Send-DapMessage `
        -Writer $writer `
        -Message @{
            seq = $sequence
            type = "request"
            command = "configurationDone"
            arguments = @{}
        }

    $response = Read-UntilResponse `
        -Reader $reader `
        -RequestSequence $sequence

    if ($null -eq $response -or !$response.success)
    {
        throw "ConfigurationDone failed."
    }

    Write-Host ""
    Write-Host "========================================="
    Write-Host " DAP launch sequence completed."
    Write-Host "========================================="
    Write-Host ""

    Start-Sleep -Seconds 2

    #
    # DISCONNECT
    #

    $sequence++

    Send-DapMessage `
        -Writer $writer `
        -Message @{
            seq = $sequence
            type = "request"
            command = "disconnect"
            arguments = @{
                restart = $false
                terminateDebuggee = $true
                suspendDebuggee = $false
            }
        }

    $response = Read-UntilResponse `
        -Reader $reader `
        -RequestSequence $sequence

    Write-Host ""

    if ($null -ne $response)
    {
        Write-Host "Disconnect response received."
    }
    else
    {
        Write-Host "Adapter closed before returning a disconnect response."
    }

    $writer.Close()

    if (!$process.WaitForExit(5000))
    {
        Write-Host "Adapter did not exit after disconnect. Terminating..."
        $process.Kill($true)
        $process.WaitForExit()
    }

    Write-Host ""
    Write-Host "Simulator completed."
    Write-Host "Exit code: $($process.ExitCode)"
}
finally
{
    if ($null -ne $process -and !$process.HasExited)
    {
        $process.Kill($true)
        $process.WaitForExit()
    }

    if ($null -ne $process)
    {
        $process.Dispose()
    }
}