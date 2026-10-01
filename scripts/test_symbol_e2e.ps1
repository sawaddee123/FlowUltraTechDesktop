[CmdletBinding()]
param([string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$resultDirectory = Join-Path $repoRoot 'TestResults\symbol-e2e'
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$portProbe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$portProbe.Start()
$testPort = $portProbe.LocalEndpoint.Port
$portProbe.Stop()
$testProfile = Join-Path $env:TEMP ('FlowUltraTech_SymbolE2E_' + [Guid]::NewGuid().ToString('N'))
$browserInfo = $null
try {
    $browserInfo = & (Join-Path $repoRoot 'start_mock_symbol.ps1') -Headless -Port $testPort -UserDataDirectory $testProfile -PassThru
    & $DotnetPath run --project (Join-Path $repoRoot 'tests\FlowUltraTech.Captcha.E2E\FlowUltraTech.Captcha.E2E.csproj') -- $repoRoot $testPort | Tee-Object -FilePath (Join-Path $resultDirectory 'run.log')
    if ($LASTEXITCODE -ne 0) { throw "Symbol E2E failed (exit $LASTEXITCODE)." }
} finally {
    # Stop only the process this runner launched, including on build failure.
    if ($browserInfo.BrowserProcessId) {
        $ownedBrowser = Get-Process -Id $browserInfo.BrowserProcessId -ErrorAction SilentlyContinue
        if ($ownedBrowser) { Stop-Process -InputObject $ownedBrowser -Force }
    }
    $resolvedProfile = [IO.Path]::GetFullPath($testProfile)
    $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if ($resolvedProfile.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path $resolvedProfile -Leaf).StartsWith('FlowUltraTech_SymbolE2E_')) {
        Remove-Item -LiteralPath $resolvedProfile -Recurse -Force -ErrorAction SilentlyContinue
    }
}
