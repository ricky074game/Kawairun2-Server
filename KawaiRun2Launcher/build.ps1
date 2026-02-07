$ErrorActionPreference = "Stop"
$launchDir = $PSScriptRoot
$projectName = "KawaiRun2Launcher"
$publishDir = Join-Path $launchDir "bin\PublishTemp"
$finalDir = Join-Path $launchDir "KawaiRun2"
$zipFile = Join-Path $launchDir "KawaiRun2_Game.zip"

Write-Host "Cleaning up old junk..." -ForegroundColor Cyan
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $publishDir, $finalDir
Remove-Item -Force -ErrorAction SilentlyContinue $zipFile

Write-Host "Compiling Launcher (Single File)..." -ForegroundColor Cyan
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $publishDir

Write-Host "Assembling the package..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $finalDir | Out-Null

Move-Item (Join-Path $publishDir "$projectName.exe") $finalDir

Copy-Item (Join-Path $launchDir "flash.exe") $finalDir
Copy-Item (Join-Path $launchDir "game.swf") $finalDir

Write-Host "Zipping it up..." -ForegroundColor Cyan
Compress-Archive -Path "$finalDir\*" -DestinationPath $zipFile

Write-Host "Cleaning up temp files..." -ForegroundColor Cyan
Remove-Item -Recurse -Force $publishDir
Remove-Item -Recurse -Force $finalDir

Write-Host "--------------------------------" -ForegroundColor Green
Write-Host "SUCCESS! Zip created at:" -ForegroundColor Green
Write-Host $zipFile -ForegroundColor White