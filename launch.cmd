@echo off
set "CWDIR=%~dp0"
start "" powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "((Get-Content -LiteralPath '%~f0') | Select-Object -Skip 5) -join [Environment]::NewLine | Invoke-Expression"
exit /b
# ===== Clipboard Wizard launcher — PowerShell body below; the batch lines above are skipped =====
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework

$root   = $env:CWDIR.TrimEnd('\')
$cfg    = 'Release'
$csproj = Join-Path $root 'ClipboardWizard.csproj'
$exe    = Join-Path $root ("bin\{0}\net8.0-windows\ClipboardWizard.exe" -f $cfg)

# Rebuild only when a source file changed since the last build.
$exts = '.cs', '.xaml', '.csproj', '.manifest'
$newest = Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $exts -contains $_.Extension -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1

$needsBuild = -not (Test-Path $exe)
if (-not $needsBuild -and $newest) { $needsBuild = $newest.LastWriteTimeUtc -gt (Get-Item $exe).LastWriteTimeUtc }

$running = Get-Process -Name ClipboardWizard -ErrorAction SilentlyContinue

# Up to date and already running → nothing to do.
if (-not $needsBuild -and $running) { exit 0 }

if ($needsBuild) {
    if ($running) { $running | Stop-Process -Force; Start-Sleep -Milliseconds 300 }
    $log = Join-Path $env:TEMP ("clipwiz_build_{0}.log" -f ([guid]::NewGuid().ToString('N')))
    & dotnet build $csproj -c $cfg --nologo -clp:ErrorsOnly *> $log
    if ($LASTEXITCODE -ne 0) {
        [System.Windows.MessageBox]::Show((Get-Content -Raw $log), 'Clipboard Wizard — build failed') | Out-Null
        exit 1
    }
}

if (-not (Test-Path $exe)) {
    [System.Windows.MessageBox]::Show("Build succeeded but the exe is missing:`n$exe", 'Clipboard Wizard') | Out-Null
    exit 1
}

# Launch detached. Single-instance handling in the app takes over any stale instance.
Start-Process -FilePath $exe | Out-Null
