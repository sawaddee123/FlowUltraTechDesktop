# PowerShell Launcher for Mock Captcha Jigsaw Testbed
# Launches Chrome or Edge with Remote Debugging Port 9221 for testing FlowUltraTech

$ErrorActionPreference = "Stop"

$mockPath = Join-Path $PSScriptRoot "docs\mocks\mock_captcha_jigsaw.html"
if (-not (Test-Path $mockPath)) {
    Write-Error "Cannot find mock HTML at: $mockPath"
    exit 1
}

$mockUrl = "file:///" + ($mockPath -replace '\\', '/')

# Search for Chrome or Edge executable
$browserCandidates = @(
    "C:\Program Files\Google\Chrome\Application\chrome.exe",
    "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
    "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "C:\Program Files\Microsoft\Edge\Application\msedge.exe"
)

$browserExe = $null
foreach ($b in $browserCandidates) {
    if (Test-Path $b) {
        $browserExe = $b
        break
    }
}

if (-not $browserExe) {
    Write-Error "Neither Google Chrome nor Microsoft Edge was found on this system."
    exit 1
}

$tempUserData = Join-Path $env:TEMP "TiktokStudio_MockProfile_9221"
if (-not (Test-Path $tempUserData)) {
    New-Item -ItemType Directory -Path $tempUserData -Force | Out-Null
}

$port = 9221
Write-Host ""
Write-Host "==========================================================================" -ForegroundColor Cyan
Write-Host " [MOCK CAPTCHA JIGSAW TESTBED] Launching test browser on port $port..." -ForegroundColor Green
Write-Host "==========================================================================" -ForegroundColor Cyan
Write-Host " Browser: $browserExe" -ForegroundColor White
Write-Host " CDP Debugging Port: $port" -ForegroundColor Yellow
Write-Host " File: $mockPath" -ForegroundColor White
Write-Host " Profile: $tempUserData" -ForegroundColor Gray
Write-Host "==========================================================================" -ForegroundColor Cyan
Write-Host ""

$args = @(
    "--remote-debugging-port=$port",
    "--user-data-dir=`"$tempUserData`"",
    "--no-first-run",
    "--no-default-browser-check",
    "`"$mockUrl`""
)

Start-Process -FilePath $browserExe -ArgumentList $args
Write-Host "Browser launched successfully on port $port!" -ForegroundColor Green
