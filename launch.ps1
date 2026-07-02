#requires -Version 7.0
<#
.SYNOPSIS
    Headless build-and-launch for Clipboard Wizard.

.DESCRIPTION
    Rebuilds only when a source file has changed since the last build, then launches the tray app
    detached (a GUI process that outlives this shell). If the app is already running and nothing
    changed, it does nothing; if a rebuild was needed, it restarts the app onto the fresh build.

    Nothing stays attached to the console: after the GUI process is started this script returns.

.PARAMETER Configuration
    Build configuration (default: Release).

.PARAMETER Force
    Rebuild and restart even if sources are unchanged.

.EXAMPLE
    pwsh -File .\launch.ps1

.EXAMPLE
    # No console window at all (e.g. from a shortcut or at login):
    pwsh -NoProfile -WindowStyle Hidden -File .\launch.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$csproj  = Join-Path $root 'ClipboardWizard.csproj'
$exe     = Join-Path $root "bin\$Configuration\net8.0-windows\ClipboardWizard.exe"
$appName = 'ClipboardWizard'

# --- Detect whether a rebuild is needed (newest source vs the built exe) --------------------------
$sourceExts = '.cs', '.xaml', '.csproj', '.manifest'
$newestSource = Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $sourceExts -contains $_.Extension -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

$needsBuild = $Force -or -not (Test-Path $exe)
if (-not $needsBuild -and $newestSource) {
    $needsBuild = $newestSource.LastWriteTimeUtc -gt (Get-Item $exe).LastWriteTimeUtc
}

$running = Get-Process -Name $appName -ErrorAction SilentlyContinue

# --- Up to date and already running → nothing to do -----------------------------------------------
if (-not $needsBuild -and $running) {
    Write-Host "$appName is already running and up to date (PID $($running.Id))."
    return
}

# --- Rebuild if needed (stop the app first so the exe isn't locked) --------------------------------
if ($needsBuild) {
    if ($running) {
        Write-Host "Stopping running instance to rebuild..."
        $running | Stop-Process -Force
        $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
        $running = $null
    }

    Write-Host "Building ($Configuration)..."
    dotnet build $csproj -c $Configuration --nologo -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed (exit $LASTEXITCODE); not launching."
        exit 1
    }
}

if (-not (Test-Path $exe)) {
    Write-Error "Build reported success but '$exe' is missing."
    exit 1
}

# --- A stale instance may still be up if only some sources changed → restart onto the new build ----
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

# --- Launch detached: a WinExe GUI process; Start-Process returns immediately and the process
#     survives this shell closing. Nothing stays attached to the console. -------------------------
$p = Start-Process -FilePath $exe -PassThru
Write-Host "Launched $appName (PID $($p.Id))."
