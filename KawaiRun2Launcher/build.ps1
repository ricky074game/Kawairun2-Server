$ErrorActionPreference = "Stop"
$launchDir = $PSScriptRoot
$finalDir = Join-Path $launchDir "KawaiRun2"
$zipFile = Join-Path $launchDir "KawaiRun2_Game.zip"
$exeName = "KawaiRun2Launcher.exe"
if (-not (Get-Command "cl.exe" -ErrorAction SilentlyContinue)) {
    Write-Error "CL.EXE not found! You must run this script from the 'Developer PowerShell for VS 2022'."
}

Write-Host "Cleaning up..." -ForegroundColor Cyan
if (Test-Path $finalDir) { Remove-Item -Recurse -Force $finalDir }
if (Test-Path $zipFile) { Remove-Item -Force $zipFile }
New-Item -ItemType Directory -Path $finalDir | Out-Null

Write-Host "Compiling C++ Launcher..." -ForegroundColor Cyan
cl.exe /nologo /O2 /std:c++17 "main.cpp" /Fe:"$finalDir\$exeName" /link /SUBSYSTEM:WINDOWS user32.lib
if ($LASTEXITCODE -ne 0) { throw "Compilation failed." }
Remove-Item "$finalDir\*.obj" -ErrorAction SilentlyContinue

Write-Host "Copying Game Files..." -ForegroundColor Cyan
Copy-Item (Join-Path $launchDir "flash.exe") $finalDir
Copy-Item (Join-Path $launchDir "game.swf") $finalDir

Write-Host "Zipping it up..." -ForegroundColor Cyan
Compress-Archive -Path "$finalDir\*" -DestinationPath $zipFile

Write-Host "--------------------------------" -ForegroundColor Green
Write-Host "DONE. Size check:" -ForegroundColor Green
$size = (Get-Item "$finalDir\$exeName").Length / 1KB
Write-Host "$exeName is just $size KB." -ForegroundColor Yellow
Write-Host "Zip file created at: $zipFile" -ForegroundColor White