<#
  install-context-menu.ps1 - Clipboard Wizard's Explorer context-menu verbs (per-user, HKCU; no admin).

  Idempotent + re-runnable. Run after building (launch.cmd, or: dotnet build -c Release), and
  again any time the repo moves. The fleet's setup-context-menus.ps1 calls this when the repo is
  present, so a fresh machine gets these verbs along with the rest of its context menus.

      .\install-context-menu.ps1              # install / update
      .\install-context-menu.ps1 -Uninstall

  On image files (SystemFileAssociations\image) it maintains:
    * "Rename with intelligent name" -> ClipboardWizard.exe --intelligent-rename "%1"
      Renames each selected image in place to "<yyyy-MM-ddTHH.mm.ss> <AI title>.<ext>" (the
      stamp is the file's last-write time). Explorer launches one process per selected file;
      the app collects them into a single batch (Services\IntelligentName.cs), and
      MultiSelectModel=Player lifts Explorer's 15-item cap on multi-select verbs.
#>
param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'

$exe = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\ClipboardWizard.exe'
$key = 'HKCU:\Software\Classes\SystemFileAssociations\image\shell\ClipboardWizard.IntelligentRename'

if ($Uninstall) {
    if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Recurse -Force }
    Write-Host "removed $key"
    return
}

if (-not (Test-Path -LiteralPath $exe)) {
    throw "ClipboardWizard.exe not built at $exe - run launch.cmd or: dotnet build -c Release"
}

New-Item -Path $key -Force | Out-Null
Set-ItemProperty -LiteralPath $key -Name '(default)'        -Value 'Rename with intelligent name'
Set-ItemProperty -LiteralPath $key -Name 'Icon'             -Value $exe
Set-ItemProperty -LiteralPath $key -Name 'MultiSelectModel' -Value 'Player'
New-Item -Path "$key\command" -Force | Out-Null
Set-ItemProperty -LiteralPath "$key\command" -Name '(default)' -Value ('"{0}" --intelligent-rename "%1"' -f $exe)
Write-Host "installed: $key -> $exe"
