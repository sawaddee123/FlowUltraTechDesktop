# Build and Package Script for TikTok Studio Sandbox Installer
$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  TikTok Studio Sandbox - Build & Package Installer" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$rootDir = $PSScriptRoot
$projectPath = Join-Path $rootDir "src\TiktokStudioSandbox.App\TiktokStudioSandbox.App.csproj"
$distAppDir = Join-Path $rootDir "dist\app"
$installerOutputDir = Join-Path $rootDir "dist\installer"
$issPath = Join-Path $rootDir "installer\setup.iss"

# Locate Inno Setup Compiler
$isccCandidates = @(
    "C:\Users\$env:USERNAME\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
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
    $cmd = Get-Command "iscc" -ErrorAction SilentlyContinue
    if ($cmd) {
        $isccPath = $cmd.Source
    }
}

if (-not $isccPath) {
    Write-Error "Could not find Inno Setup Compiler (ISCC.exe). Please install Inno Setup 6."
    exit 1
}

Write-Host "`n[1/3] Publishing application (win-x64 self-contained)..." -ForegroundColor Yellow
if (Test-Path $distAppDir) {
    Remove-Item -Recurse -Force $distAppDir
}

dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $distAppDir

if (-not (Test-Path (Join-Path $distAppDir "TiktokStudioSandbox.App.exe"))) {
    Write-Error "Published executable not found!"
    exit 1
}

# Ensure Assets folder exists in published app
$assetsDir = Join-Path $distAppDir "Assets"
if (-not (Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
}
$sourceWav = Join-Path $rootDir "src\TiktokStudioSandbox.App\bin\Debug\net8.0-windows\Assets\captcha_alert.wav"
if (Test-Path $sourceWav) {
    Copy-Item $sourceWav (Join-Path $assetsDir "captcha_alert.wav") -Force
}

Write-Host "`n[2/3] Compiling installer with Inno Setup..." -ForegroundColor Yellow
if (-not (Test-Path $installerOutputDir)) {
    New-Item -ItemType Directory -Path $installerOutputDir -Force | Out-Null
}

& $isccPath $issPath

Write-Host "`n[3/3] Verifying installer output..." -ForegroundColor Yellow
$installerFiles = Get-ChildItem -Path $installerOutputDir -Filter "*.exe"

if ($installerFiles.Count -gt 0) {
    $latestInstaller = $installerFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $sizeMb = [math]::Round($latestInstaller.Length / 1MB, 2)
    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host "  SUCCESS! Installer generated successfully:" -ForegroundColor Green
    Write-Host "  Path: $($latestInstaller.FullName)" -ForegroundColor White
    Write-Host "  Size: $sizeMb MB" -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    Write-Error "No installer executable was found in $installerOutputDir"
    exit 1
}
