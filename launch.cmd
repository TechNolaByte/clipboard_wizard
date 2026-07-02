@echo off
rem Double-clickable launcher for Clipboard Wizard.
rem Runs launch.ps1 hidden and detached so no console window lingers, then exits.
start "" pwsh -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0launch.ps1"
