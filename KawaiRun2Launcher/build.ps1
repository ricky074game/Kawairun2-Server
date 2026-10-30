param(
    [switch]$NoSign,
    # Skip `npm run build` and package whatever is already in ../webrewrite/dist.
    [switch]$SkipWebBuild,
    # Where the per-RID update zips go. Defaults to ../Server/launcher (the deploy folder).
    [string]$OutDir,
    # Package this folder instead of ../webrewrite/dist (testing; implies -SkipWebBuild).
    [string]$WebDist
)
$ErrorActionPreference = "Stop"

# The .NET SDK is not on PATH on this machine; fall back to the per-user install.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $userDotnet = Join-Path $env:USERPROFILE ".dotnet"
    if (Test-Path (Join-Path $userDotnet "dotnet.exe")) {
        $env:DOTNET_ROOT = $userDotnet
        $env:PATH = "$userDotnet;$env:PATH"
        $env:DOTNET_MULTILEVEL_LOOKUP = "0"
    } else {
        throw "dotnet SDK not found on PATH or in $userDotnet"
    }
}

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
$serverLauncherDir = if ($OutDir) { $OutDir } else { Join-Path (Split-Path $launchDir -Parent) "Server\launcher" }
$projectFile = Join-Path $launchDir "KawaiRun2Launcher.csproj"
$webDir = Join-Path (Split-Path $launchDir -Parent) "webrewrite"
$webDist = if ($WebDist) { $WebDist } else { Join-Path $webDir "dist" }
if ($WebDist) { $SkipWebBuild = [switch]$true }
$webZip = Join-Path $launchDir "webgame.zip"
$rids = @("win-x86", "win-x64", "osx-x64", "linux-x64")

# Zip dist/ with FORWARD-SLASH entry names (Compress-Archive writes backslashes, which break on
# macOS/Linux). Media that is already compressed (mp3/webp/png/...) is STORED; everything else uses
# the fastest deflate. The launcher extracts this once per version, so size matters less than speed.
function New-WebGameZip {
    param([string]$SourceDir, [string]$ZipPath)
    Add-Type -AssemblyName System.IO.Compression | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    $root = (Resolve-Path $SourceDir).Path.TrimEnd('\')
    $storedExt = @('.mp3', '.ogg', '.webp', '.png', '.jpg', '.jpeg', '.gif', '.woff2', '.zip', '.mp4', '.webm')
    $fs = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew)
    $zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -Path $root -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($root.Length + 1) -replace '\\', '/'
            $level = if ($storedExt -contains $_.Extension.ToLowerInvariant()) {
                [System.IO.Compression.CompressionLevel]::NoCompression
            } else {
                [System.IO.Compression.CompressionLevel]::Fastest
            }
            $entry = $zip.CreateEntry($rel, $level)
            $es = $entry.Open()
            $in = [System.IO.File]::OpenRead($_.FullName)
            try { $in.CopyTo($es) } finally { $in.Dispose(); $es.Dispose() }
        }
    } finally {
        $zip.Dispose(); $fs.Dispose()
    }
}

# Build and embed the web game
Write-Host "Preparing embedded web game..." -ForegroundColor Cyan
if ($SkipWebBuild) {
    Write-Warning "-SkipWebBuild: packaging the existing $webDist without rebuilding it."
} else {
    if (-not (Test-Path $webDir)) { throw "webrewrite folder not found: $webDir" }
    Push-Location $webDir
    try {
        Write-Host "  npm run build (webrewrite)..." -ForegroundColor DarkCyan
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed in $webDir (exit $LASTEXITCODE)" }
    } finally {
        Pop-Location
    }
}
if (-not (Test-Path (Join-Path $webDist "port.html"))) { throw "Web build output missing: $webDist\port.html" }
Write-Host "  Zipping dist -> webgame.zip..." -ForegroundColor DarkCyan
New-WebGameZip -SourceDir $webDist -ZipPath $webZip
Write-Host ("  webgame.zip: {0:N1} MB" -f ((Get-Item $webZip).Length / 1MB)) -ForegroundColor DarkCyan

Write-Host "Cleaning output folders..." -ForegroundColor Cyan
if (Test-Path $publishRoot) { Remove-Item -Recurse -Force $publishRoot -ErrorAction SilentlyContinue }
if (-not (Test-Path $publishRoot)) { New-Item -ItemType Directory -Path $publishRoot | Out-Null }
if (-not (Test-Path $serverLauncherDir)) { New-Item -ItemType Directory -Path $serverLauncherDir | Out-Null }

foreach ($rid in $rids) {
    $ridDir = Join-Path $publishRoot $rid
    $zipPath = Join-Path $serverLauncherDir "$rid.zip"

    # Windows uses WinForms + WebView2 + the controller support (net8.0-windows); osx/linux use the
    # plain net8.0 target with a Photino window.
    $framework = if ($rid -like 'win-*') { 'net8.0-windows' } else { 'net8.0' }

    Write-Host "Publishing $rid ($framework)..." -ForegroundColor Cyan
    dotnet publish $projectFile -c Release -f $framework -r $rid -o $ridDir /p:DebugType=None
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid"
    }

    # Single-file publish embeds everything; drop any stray files next to the executable.
    Get-ChildItem $ridDir | Where-Object { $_.Name -notmatch '^KawaiRun2Launcher(\.exe)?$' } | Remove-Item -Recurse -Force

    if ($rid -like 'win-*') {
        Sign-Files @((Join-Path $ridDir "KawaiRun2Launcher.exe"))
    }

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    }

    Compress-Archive -Path (Join-Path $ridDir "*") -DestinationPath $zipPath -Force
    Write-Host ("Created package: {0} ({1:N1} MB)" -f $zipPath, ((Get-Item $zipPath).Length / 1MB)) -ForegroundColor Green
}

Write-Host "Build complete." -ForegroundColor Green
Write-Host "Published launcher folders: $publishRoot" -ForegroundColor Yellow
Write-Host "Update packages: $serverLauncherDir" -ForegroundColor Yellow
