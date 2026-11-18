<#
.SYNOPSIS
    Builds OverTranslate Low-RAM Edition into a portable ZIP (self-contained, no .NET install needed to run it).

.DESCRIPTION
    1. Runs the logic tests (PerformanceProfile, backoff, cache, glossary, ...) and stops if any fails.
    2. dotnet publish, self-contained, win-x64 (same settings publish-velopack.ps1 uses for the original).
    3. Copies the guides next to the exe and writes dist\OverTranslate-LowRam-<version>-win-x64.zip + SHA256.

    Needs on the build machine: Windows 10/11, the .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)
    and internet access for the NuGet restore. Nothing is needed to RUN the result.

.EXAMPLE
    .\build-lowram.ps1
    .\build-lowram.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = (Join-Path $PSScriptRoot "dist"),
    [switch]$SkipTests,
    # Tao them file cai dat (installer .exe, dong goi trong ZIP). Can NSIS (https://nsis.sourceforge.io) hoac: choco install nsis
    [switch]$Installer
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "Khong tim thay 'dotnet'. Cai .NET 8 SDK truoc: https://dotnet.microsoft.com/download/dotnet/8.0"
}

$project = Join-Path $PSScriptRoot "src\OverTranslate\OverTranslate.csproj"
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "OverTranslate-LowRam-$version-win-x64"
$publishDir = Join-Path $OutputRoot $name
$zipPath = Join-Path $OutputRoot "$name.zip"

if (-not $SkipTests) {
    Write-Host "== Chay test logic Low-RAM ==" -ForegroundColor Cyan
    & dotnet run --project (Join-Path $PSScriptRoot "src\OverTranslate.LowRamTests\LowRamTests.csproj") -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Test logic that bai - dung build." }
    & dotnet run --project (Join-Path $PSScriptRoot "src\OverTranslate.LowRamIntegration\LowRamIntegration.csproj") -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Test tich hop that bai - dung build." }
}

Write-Host "== dotnet publish (self-contained, win-x64) ==" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

& dotnet publish $project -c $Configuration -r win-x64 -p:SelfContained=true "-p:PublishDir=$publishDir\"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish that bai (exit $LASTEXITCODE)." }

# Same guard as publish-velopack.ps1: a framework-dependent output does not start on a PC without .NET 8.
if (-not (Test-Path (Join-Path $publishDir "coreclr.dll"))) {
    throw "Ban publish khong phai self-contained (thieu coreclr.dll)."
}

# Confirms the GC settings reached the runtime config (they are what the Low-RAM profile relies on at startup).
$runtimeConfig = Get-Content (Join-Path $publishDir "OverTranslate.runtimeconfig.json") -Raw
foreach ($key in "System.GC.Concurrent", "System.GC.ConserveMemory") {
    if ($runtimeConfig -notmatch [regex]::Escape($key)) { Write-Warning "runtimeconfig.json thieu '$key'." }
}

foreach ($doc in "HUONG_DAN.md", "KHOI_PHUC.md", "CHANGELOG.md", "PERFORMANCE_REPORT.md", "LICENSE", "THIRD-PARTY-NOTICES.txt") {
    $path = Join-Path $PSScriptRoot $doc
    if (Test-Path $path) { Copy-Item $path $publishDir -Force }
}
Copy-Item (Join-Path $PSScriptRoot "tools\lowram\measure-performance.ps1") $publishDir -Force

# Marker file the original Portable also has; the app keeps its data in %AppData%\OverTranslate either way.
New-Item -ItemType File -Path (Join-Path $publishDir ".portable") -Force | Out-Null

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
"$hash  $name.zip" | Set-Content (Join-Path $OutputRoot "$name.zip.sha256") -Encoding ASCII

Write-Host ""
Write-Host "Xong: $zipPath" -ForegroundColor Green
Write-Host "SHA256: $hash"
Write-Host ("Dung luong: {0:N1} MB" -f ((Get-Item $zipPath).Length / 1MB))

if ($Installer) {
    Write-Host ""
    Write-Host "== Tao installer (NSIS) ==" -ForegroundColor Cyan
    $makensis = (Get-Command makensis -ErrorAction SilentlyContinue).Source
    if (-not $makensis) {
        foreach ($c in "${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe") {
            if (Test-Path $c) { $makensis = $c; break }
        }
    }
    if (-not $makensis) { throw "Khong tim thay makensis. Cai NSIS (choco install nsis hoac https://nsis.sourceforge.io) roi chay lai voi -Installer." }

    $setupName = "OverTranslate-LowRam-Installer-$version-win-x64"
    $setupExe = Join-Path $OutputRoot "$setupName.exe"
    $setupZip = Join-Path $OutputRoot "$setupName.zip"
    $icon = Join-Path $PSScriptRoot "src\OverTranslate\icons\app.ico"
    $nsi = Join-Path $PSScriptRoot "tools\lowram\installer\OverTranslate-LowRam.nsi"

    # Installer khong can file .portable
    $stage = Join-Path $OutputRoot "$name-installer-stage"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    Copy-Item $publishDir $stage -Recurse
    Remove-Item (Join-Path $stage ".portable") -Force -ErrorAction SilentlyContinue

    if (Test-Path $setupExe) { Remove-Item $setupExe -Force }
    & $makensis /V2 "/DVERSION=$version" "/DSOURCEDIR=$stage" "/DICON=$icon" "/DOUTFILE=$setupExe" $nsi
    if ($LASTEXITCODE -ne 0) { throw "makensis that bai (exit $LASTEXITCODE)." }
    Remove-Item $stage -Recurse -Force

    # Dong goi trong ZIP (kem huong dan) de tranh bi chan/loi khi tai file .exe
    $zipStage = Join-Path $OutputRoot "$setupName-zipstage"
    if (Test-Path $zipStage) { Remove-Item $zipStage -Recurse -Force }
    New-Item -ItemType Directory -Path $zipStage | Out-Null
    Copy-Item $setupExe $zipStage
    foreach ($doc in "HUONG_DAN.md", "KHOI_PHUC.md", "LICENSE") {
        $path = Join-Path $PSScriptRoot $doc
        if (Test-Path $path) { Copy-Item $path $zipStage -Force }
    }
    if (Test-Path $setupZip) { Remove-Item $setupZip -Force }
    Compress-Archive -Path (Join-Path $zipStage "*") -DestinationPath $setupZip -CompressionLevel Optimal
    Remove-Item $zipStage -Recurse -Force
    $h2 = (Get-FileHash $setupZip -Algorithm SHA256).Hash
    "$h2  $setupName.zip" | Set-Content (Join-Path $OutputRoot "$setupName.zip.sha256") -Encoding ASCII

    Write-Host "Installer: $setupExe" -ForegroundColor Green
    Write-Host "ZIP installer: $setupZip" -ForegroundColor Green
    Write-Host "SHA256: $h2"
    Write-Host "Luu y: installer chua ky so (code signing) nen Windows SmartScreen co the canh bao 'Windows protected your PC' -> More info -> Run anyway."
}
