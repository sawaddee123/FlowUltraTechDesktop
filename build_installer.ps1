param(
    [Parameter(Mandatory = $false)]
    [string]$Version = ""
)

# Build and Package Script for TikTok Studio Sandbox Installer
$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  TikTok Studio Sandbox - Build & Package Installer" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$rootDir = $PSScriptRoot
$projectPath = Join-Path $rootDir "src\TiktokStudioSandbox.App\TiktokStudioSandbox.App.csproj"
$coreProjectPath = Join-Path $rootDir "src\TiktokStudioSandbox.Core\TiktokStudioSandbox.Core.csproj"
$manifestPath = Join-Path $rootDir "src\TiktokStudioSandbox.App\app.manifest"
$distAppDir = Join-Path $rootDir "dist\app"
$installerOutputDir = Join-Path $rootDir "dist\installer"
$issPath = Join-Path $rootDir "installer\setup.iss"

# 1. Detect Current Version from Project File
$currentVersion = "0.1.6"
if (Test-Path $projectPath) {
    $projContent = Get-Content $projectPath -Raw
    if ($projContent -match "<Version>(.*?)</Version>") {
        $currentVersion = $matches[1].Trim()
    }
}

# 2. Interactive Input for Version (Show Current as Default)
if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "`n[Version Setup]" -ForegroundColor Yellow
    Write-Host "Current Version: " -NoNewline -ForegroundColor White
    Write-Host "v$currentVersion" -ForegroundColor Cyan
    
    $prompt = "Enter version to build [Default: $currentVersion]: "
    $inputVersion = Read-Host -Prompt $prompt
    if ([string]::IsNullOrWhiteSpace($inputVersion)) {
        $targetVersion = $currentVersion
    } else {
        $targetVersion = $inputVersion.Trim().TrimStart('v', 'V')
    }
} else {
    $targetVersion = $Version.Trim().TrimStart('v', 'V')
}

# Format 4-part version for AssemblyVersion/FileVersion (e.g., 0.1.5.0)
$parts = $targetVersion.Split('.')
$fourPartList = [System.Collections.Generic.List[string]]::new($parts)
while ($fourPartList.Count -lt 4) {
    $fourPartList.Add("0")
}
$fourPartVersion = ($fourPartList[0..3] -join ".")

Write-Host "`nTarget Release Version: " -NoNewline -ForegroundColor White
Write-Host "v$targetVersion" -ForegroundColor Green
Write-Host "Assembly / File Version: " -NoNewline -ForegroundColor White
Write-Host "$fourPartVersion" -ForegroundColor Gray

# 3. Synchronize Version to all project files
Write-Host "`nSynchronizing version across project files..." -ForegroundColor DarkGray

# (a) TiktokStudioSandbox.App.csproj
if (Test-Path $projectPath) {
    $content = Get-Content $projectPath -Raw
    $content = [regex]::Replace($content, "<Version>.*?</Version>", "<Version>$targetVersion</Version>")
    $content = [regex]::Replace($content, "<AssemblyVersion>.*?</AssemblyVersion>", "<AssemblyVersion>$fourPartVersion</AssemblyVersion>")
    $content = [regex]::Replace($content, "<FileVersion>.*?</FileVersion>", "<FileVersion>$fourPartVersion</FileVersion>")
    Set-Content $projectPath $content -NoNewline
    Write-Host "  [OK] Updated $projectPath" -ForegroundColor DarkGreen
}

# (b) TiktokStudioSandbox.Core.csproj
if (Test-Path $coreProjectPath) {
    $content = Get-Content $coreProjectPath -Raw
    $content = [regex]::Replace($content, "<Version>.*?</Version>", "<Version>$targetVersion</Version>")
    $content = [regex]::Replace($content, "<AssemblyVersion>.*?</AssemblyVersion>", "<AssemblyVersion>$fourPartVersion</AssemblyVersion>")
    $content = [regex]::Replace($content, "<FileVersion>.*?</FileVersion>", "<FileVersion>$fourPartVersion</FileVersion>")
    Set-Content $coreProjectPath $content -NoNewline
    Write-Host "  [OK] Updated $coreProjectPath" -ForegroundColor DarkGreen
}

# (c) app.manifest (only update assemblyIdentity version, NEVER touch xml header)
if (Test-Path $manifestPath) {
    $content = Get-Content $manifestPath -Raw
    $content = [regex]::Replace($content, '<assemblyIdentity\s+version="[^"]*"', "<assemblyIdentity version=`"$fourPartVersion`"")
    Set-Content $manifestPath $content -NoNewline
    Write-Host "  [OK] Updated $manifestPath" -ForegroundColor DarkGreen
}

# (d) installer\setup.iss
if (Test-Path $issPath) {
    $content = Get-Content $issPath -Raw
    $content = [regex]::Replace($content, '#define\s+MyAppVersion\s+"[^"]+"', "#define MyAppVersion `"$targetVersion`"")
    Set-Content $issPath $content -NoNewline
    Write-Host "  [OK] Updated $issPath" -ForegroundColor DarkGreen
}

# 4. Locate Inno Setup Compiler
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

# 5. Publish Application (self-contained win-x64)
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

# 6. Compile Installer with Inno Setup
Write-Host "`n[2/3] Compiling installer with Inno Setup..." -ForegroundColor Yellow
if (-not (Test-Path $installerOutputDir)) {
    New-Item -ItemType Directory -Path $installerOutputDir -Force | Out-Null
}

& $isccPath $issPath

# 7. Verify and Display Output
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

    # Auto-sync to backend storage/releases directory if present
    $backendReleasesDir = "D:\Script\java-script\SimpleSolveCaptchaProject\storage\releases"
    if (Test-Path $backendReleasesDir) {
        $destPath = Join-Path $backendReleasesDir $latestInstaller.Name
        Copy-Item $latestInstaller.FullName $destPath -Force
        Write-Host "  Synced to Backend: $destPath" -ForegroundColor Cyan
    }
} else {
    Write-Error "No installer executable was found in $installerOutputDir"
    exit 1
}
