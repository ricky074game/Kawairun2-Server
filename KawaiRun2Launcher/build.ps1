param([switch]$NoSign)
$ErrorActionPreference = "Stop"

$signScript = Join-Path $env:USERPROFILE "tools\ArtifactSigning\sign.ps1"
$canSign = (-not $NoSign) -and (Test-Path $signScript)
if (-not $canSign) { Write-Warning "Code signing skipped (use Azure Artifact Signing: $signScript missing or -NoSign)." }
function Sign-Files([string[]]$paths) {
    if (-not $canSign) { return }
    Write-Host "Signing $($paths.Count) file(s) with Azure Artifact Signing..." -ForegroundColor Cyan
    & $signScript -Files $paths
}

$launchDir = $PSScriptRoot
$publishRoot = Join-Path $launchDir "publish"
$serverLauncherDir = Join-Path (Split-Path $launchDir -Parent) "Server\launcher"
$projectFile = Join-Path $launchDir "KawaiRun2Launcher.csproj"
$rids = @("win-x86", "win-x64", "osx-x64", "linux-x64")

# Build a zip with FORWARD-SLASH entry names. PowerShell's Compress-Archive (and .NET
# Framework's ZipFile.CreateFromDirectory) writes backslash separators, which macOS/Linux
# unzip as one flat filename instead of a folder tree -> the .app never extracts and the
# launcher can't find the projector. We control the entry names directly to avoid that.
function New-ForwardSlashZip {
    param([string]$SourceAppDir, [string]$ZipPath)
    Add-Type -AssemblyName System.IO.Compression | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    $appDir = (Resolve-Path $SourceAppDir).Path
    $parent = Split-Path $appDir -Parent
    $fs = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew)
    $zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -Path $appDir -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($parent.Length + 1) -replace '\\', '/'
            $entry = $zip.CreateEntry($rel, [System.IO.Compression.CompressionLevel]::Optimal)
            $es = $entry.Open()
            $in = [System.IO.File]::OpenRead($_.FullName)
            try { $in.CopyTo($es) } finally { $in.Dispose(); $es.Dispose() }
        }
    } finally {
        $zip.Dispose(); $fs.Dispose()
    }
}

# Prepare embedded resources
Write-Host "Preparing embedded resources..." -ForegroundColor Cyan
$macAppPath = "$launchDir\mac_flash\Flash Player\Flash Player.app"
if (Test-Path $macAppPath) {
    Write-Host "  Repacking mac_flash.zip (forward-slash safe)..." -ForegroundColor DarkCyan
    New-ForwardSlashZip -SourceAppDir $macAppPath -ZipPath "$launchDir\mac_flash.zip"
} elseif (-not (Test-Path "$launchDir\mac_flash.zip")) {
    Write-Warning "mac_flash source .app not found and no mac_flash.zip present -> osx build will lack a projector."
}
if (-not (Test-Path "$launchDir\linux_flash")) {
    $linuxArchive = "$launchDir\mac_flash\flash_player_sa_linux.x86_64.tar.gz"
    if (Test-Path $linuxArchive) {
        tar -xzf $linuxArchive -C "$launchDir" "flashplayer"
        if (Test-Path "$launchDir\flashplayer") {
            Rename-Item "$launchDir\flashplayer" "linux_flash" -Force
        }
    }
}

if ($canSign) {
    $flashSig = Get-AuthenticodeSignature "$launchDir\flash.exe"
    if ($flashSig.Status -ne 'Valid') { Sign-Files @("$launchDir\flash.exe") }
}

Write-Host "Cleaning output folders..." -ForegroundColor Cyan
if (Test-Path $publishRoot) { Remove-Item -Recurse -Force $publishRoot -ErrorAction SilentlyContinue }
if (-not (Test-Path $publishRoot)) { New-Item -ItemType Directory -Path $publishRoot | Out-Null }
if (-not (Test-Path $serverLauncherDir)) { New-Item -ItemType Directory -Path $serverLauncherDir | Out-Null }

foreach ($rid in $rids) {
    $ridDir = Join-Path $publishRoot $rid
    $zipPath = Join-Path $serverLauncherDir "$rid.zip"

    # Controller support only exists on the net8.0-windows TFM (WinForms UI + pad reading/
    # injection); win-* RIDs publish that target, osx/linux keep publishing plain net8.0 unchanged.
    $framework = if ($rid -like 'win-*') { 'net8.0-windows' } else { 'net8.0' }

    Write-Host "Publishing $rid ($framework)..." -ForegroundColor Cyan
    dotnet publish $projectFile -c Release -f $framework -r $rid -o $ridDir /p:DebugType=None
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid"
    }

    # Clean up non-executables
    Get-ChildItem $ridDir | Where-Object { $_.Name -notmatch '^KawaiRun2Launcher(\.exe)?$' } | Remove-Item -Recurse -Force

    if ($rid -like 'win-*') {
        Sign-Files @((Join-Path $ridDir "KawaiRun2Launcher.exe"))
    }

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    }

    Compress-Archive -Path (Join-Path $ridDir "*") -DestinationPath $zipPath -Force
    Write-Host "Created package: $zipPath" -ForegroundColor Green
}

Write-Host "Build complete." -ForegroundColor Green
Write-Host "Published launcher folders: $publishRoot" -ForegroundColor Yellow
Write-Host "Server update packages: $serverLauncherDir" -ForegroundColor Yellow