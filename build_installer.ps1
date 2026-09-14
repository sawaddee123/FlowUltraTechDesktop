# PowerShell script to publish and build the Setup.exe installer
[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Building TikTok LIVE Studio Sandbox Setup Installer" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Locate Inno Setup Compiler (ISCC.exe)
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$isccPath = $null
foreach ($candidate in $isccCandidates) {
    if (Test-Path $candidate) {
        $isccPath = $candidate
        break
    }
}

if (-not $isccPath) {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        $isccPath = $cmd.Source
    }
}

if (-not $isccPath) {
    Write-Error "Inno Setup Compiler (ISCC.exe) was not found. Please install Inno Setup 6."
}

Write-Host "Found Inno Setup Compiler: $isccPath" -ForegroundColor Green

# 2. Publish Self-Contained App
$publishDir = Join-Path $ScriptDir "publish\$Runtime"
Write-Host "Publishing project to: $publishDir..." -ForegroundColor Yellow

$projectPath = Join-Path $ScriptDir "src\TiktokStudioSandbox.App\TiktokStudioSandbox.App.csproj"

& dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Publish succeeded." -ForegroundColor Green

# 3. Compile Installer with Inno Setup
$distDir = Join-Path $ScriptDir "dist"
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

$issFile = Join-Path $ScriptDir "installer\setup_script.iss"
Write-Host "Compiling installer script: $issFile..." -ForegroundColor Yellow

& "$isccPath" "$issFile"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

$setupFile = Get-ChildItem -Path $distDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($setupFile) {
    $sizeMb = [math]::Round($setupFile.Length / 1MB, 2)
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " SETUP INSTALLER CREATED SUCCESSFULLY!" -ForegroundColor Green
    Write-Host " File: $($setupFile.FullName)" -ForegroundColor Cyan
    Write-Host " Size: $sizeMb MB" -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    Write-Error "Could not find generated setup file in $distDir"
}
