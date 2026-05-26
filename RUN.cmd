@echo off
rem Smart launcher for VideoEditor.
rem - If .NET 8 SDK is installed, builds and runs from source.
rem - If not, downloads the latest ready-to-run release from GitHub and launches it.
rem Just calls the PowerShell launcher so the user does not have to deal with execution policy.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0RUN.ps1" %*
