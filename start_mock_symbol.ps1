# ASCII keeps this script compatible with Windows PowerShell 5.1 without a BOM.
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 9221,
    [switch]$Headless,
    [string]$UserDataDirectory,
    [switch]$PassThru
)
$ErrorActionPreference = 'Stop'
$mockPath = Join-Path $PSScriptRoot 'docs\mocks\mock_captcha_symbol.html'
if (-not (Test-Path -LiteralPath $mockPath)) { throw "Cannot find mock HTML: $mockPath" }
$mockUrl = ([Uri]$mockPath).AbsoluteUri
$cdpUrl = "http://127.0.0.1:$Port"
function Get-CdpJson([string]$Path) {
    Invoke-RestMethod -Uri "$cdpUrl$Path" -TimeoutSec 2 -UseBasicParsing
}
$version = $null
try { $version = Get-CdpJson '/json/version' } catch { }
$browserProcessId = $null
if ($version.webSocketDebuggerUrl) {
    # Reuse an existing debugging browser without closing its tabs.
    $encodedUrl = [Uri]::EscapeDataString($mockUrl)
    Invoke-RestMethod -Uri "$cdpUrl/json/new?$encodedUrl" -Method Put -TimeoutSec 5 -UseBasicParsing | Out-Null
} else {
    $browserCandidates = @(
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
    )
    $browserExe = $browserCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $browserExe) { throw 'Google Chrome or Microsoft Edge is required.' }
    if (-not $UserDataDirectory) {
        $UserDataDirectory = Join-Path $env:TEMP "TiktokStudio_MockProfile_$Port"
    }
    New-Item -ItemType Directory -Path $UserDataDirectory -Force | Out-Null
    $browserArguments = @(
        "--remote-debugging-port=$Port", '--remote-debugging-address=127.0.0.1',
        "--user-data-dir=`"$UserDataDirectory`"", '--no-first-run',
        '--no-default-browser-check', '--window-size=1280,900'
    )
    if ($Headless) { $browserArguments += '--headless=new' }
    $browserArguments += "`"$mockUrl`""
    $startOptions = @{ FilePath = $browserExe; ArgumentList = $browserArguments; PassThru = $true }
    if ($Headless) { $startOptions.WindowStyle = 'Hidden' }
    $browserProcessId = (Start-Process @startOptions).Id
}
$ready = $false
$deadline = (Get-Date).AddSeconds(20)
do {
    try {
        $version = Get-CdpJson '/json/version'
        $pages = @(Get-CdpJson '/json/list')
        $ready = $version.webSocketDebuggerUrl -and @($pages | Where-Object { $_.type -eq 'page' -and $_.url -eq $mockUrl }).Count -gt 0
    } catch { }
    if (-not $ready) { Start-Sleep -Milliseconds 200 }
} until ($ready -or (Get-Date) -gt $deadline)
if (-not $ready) { throw "Mock browser did not become ready on CDP port $Port. Check port availability." }
Write-Host "Symbol mock ready: $mockUrl"
Write-Host "CDP port: $Port. In FlowUltraTech, select this port and scan Captcha."
if ($PassThru) {
    [PSCustomObject]@{ Port = $Port; Url = $mockUrl; BrowserProcessId = $browserProcessId; UserDataDirectory = $UserDataDirectory }
}
