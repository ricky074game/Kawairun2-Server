# PowerShell build script for KawaiRunExtension
# Usage: .\build.ps1

$ErrorActionPreference = 'Stop'
$PROJECT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$SRC_DIR = Join-Path $PROJECT_DIR 'src'
$OUT_DIR = Join-Path $PROJECT_DIR 'out'
$BUILD_DIR = Join-Path $OUT_DIR 'build'
$JAR_DIR = Join-Path $OUT_DIR 'jar'
$JAR_NAME = 'KawaiRunExtension.jar'

$SFS_LIB = Join-Path $env:USERPROFILE 'SmartFoxServer_2X\SFS2X\lib'

$JAVA_TARGET = 11
$JAVAC = 'javac'
$JAR = 'jar'

Write-Host '=== KawaiRun Extension Build Script ===' -ForegroundColor Cyan
Write-Host "Project Directory: $PROJECT_DIR" -ForegroundColor Gray
Write-Host "Source Directory: $SRC_DIR" -ForegroundColor Gray
Write-Host "Output Directory: $OUT_DIR" -ForegroundColor Gray
Write-Host "Target Java release: $JAVA_TARGET" -ForegroundColor Gray
Write-Host ''

# Ensure output directories
if (Test-Path $OUT_DIR) {
    Write-Host 'Cleaning build directories...' -ForegroundColor Yellow
    Remove-Item -Recurse -Force $OUT_DIR
}
New-Item -ItemType Directory -Path $BUILD_DIR -Force | Out-Null
New-Item -ItemType Directory -Path $JAR_DIR -Force | Out-Null

$classpathParts = @()
if (Test-Path $SFS_LIB) {
    $sfs2x = Join-Path $SFS_LIB 'sfs2x.jar'
    $sfs2xCore = Join-Path $SFS_LIB 'sfs2x-core.jar'
    if (Test-Path $sfs2x) { $classpathParts += $sfs2x }
    if (Test-Path $sfs2xCore) { $classpathParts += $sfs2xCore }
} else {
    Write-Host "Warning: SmartFoxServer lib directory not found at: $SFS_LIB" -ForegroundColor Yellow
    Write-Host 'If compilation fails, set $SFS_LIB to the correct SmartFoxServer 2X lib path.' -ForegroundColor Yellow
}

$CLASSPATH = $classpathParts -join ';'
if (-not $CLASSPATH) { $CLASSPATH = '.' }

$javaFiles = Get-ChildItem -Path $SRC_DIR -Filter '*.java' -Recurse | Where-Object { $_.DirectoryName -notlike '*\META-INF*' }
if ($javaFiles.Count -eq 0) {
    Write-Host "ERROR: No Java source files found in $SRC_DIR" -ForegroundColor Red
    exit 1
}

Write-Host "Found $($javaFiles.Count) Java source file(s):" -ForegroundColor Gray
$javaFiles | ForEach-Object { Write-Host "  - $($_.FullName)" -ForegroundColor DarkGray }

$filesArg = ($javaFiles | ForEach-Object { "`"$($_.FullName)`"" }) -join ' '
$javacArgs = "--release $JAVA_TARGET -cp `"$CLASSPATH`" -d `"$BUILD_DIR`" $filesArg"
Write-Host 'Compiling...' -ForegroundColor Yellow
Write-Host "$JAVAC $javacArgs" -ForegroundColor DarkGray

& $JAVAC @($('--release'), $JAVA_TARGET, '-cp', $CLASSPATH, '-d', $BUILD_DIR) $javaFiles.FullName

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Compilation failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

$metaInfSrc = Join-Path $SRC_DIR 'META-INF'
if (Test-Path $metaInfSrc) {
    Write-Host 'Copying META-INF...' -ForegroundColor Yellow
    Copy-Item -Recurse -Force $metaInfSrc -Destination $BUILD_DIR
}

$jarPath = Join-Path $JAR_DIR $JAR_NAME
Write-Host 'Creating JAR file...' -ForegroundColor Yellow
Push-Location $BUILD_DIR
try {
    & $JAR cvfm ..\$JAR_NAME META-INF\MANIFEST.MF *.class > $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: JAR creation failed with exit code $LASTEXITCODE" -ForegroundColor Red
        Pop-Location
        exit $LASTEXITCODE
    }
} finally {
    Pop-Location
}
