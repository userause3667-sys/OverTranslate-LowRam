<#
.SYNOPSIS
    Ap dung cau hinh bo nho (GC) cua Low-RAM Edition len ban Portable DANG CO. Khong sua file EXE/DLL.

.DESCRIPTION
    Chi sua 1 file van ban: current\OverTranslate.runtimeconfig.json (them 3 thiet lap cua .NET).
    Ban goc duoc sao luu thanh OverTranslate.runtimeconfig.json.original; dung -Restore de khoi phuc.
    Dat script nay canh OverTranslate.exe (thu muc Portable), DONG OverTranslate truoc khi chay.
#>
[CmdletBinding()]
param(
    [string]$PortableDir = $PSScriptRoot,
    [switch]$Restore
)

$ErrorActionPreference = "Stop"
$config = Join-Path $PortableDir "current\OverTranslate.runtimeconfig.json"
$backup = "$config.original"

if (-not (Test-Path $config)) {
    throw "Khong thay '$config'. Hay dat script canh OverTranslate.exe (thu muc co thu muc 'current')."
}
if (Get-Process -Name "OverTranslate" -ErrorAction SilentlyContinue) {
    throw "OverTranslate dang chay. Hay thoat han (Exit trong khay he thong) roi chay lai script."
}

if ($Restore) {
    if (-not (Test-Path $backup)) { throw "Khong co ban sao luu '$backup' - chua tung ap dung, hoac da khoi phuc." }
    Copy-Item $backup $config -Force
    Remove-Item $backup -Force
    Write-Host "Da khoi phuc cau hinh goc." -ForegroundColor Green
    return
}

if (-not (Test-Path $backup)) { Copy-Item $config $backup }

$json = Get-Content $config -Raw -Encoding UTF8 | ConvertFrom-Json
$props = $json.runtimeOptions.configProperties
if ($null -eq $props) {
    $props = [pscustomobject]@{}
    $json.runtimeOptions | Add-Member -NotePropertyName configProperties -NotePropertyValue $props
}

function Set-Property($name, $value) {
    if ($props.PSObject.Properties[$name]) { $props.$name = $value }
    else { $props | Add-Member -NotePropertyName $name -NotePropertyValue $value }
}

# GC nen (concurrent) tat: bot 1 luong GC nen va bo nho phu tro.
Set-Property "System.GC.Concurrent" $false
# Tat TieredPGO: bot do dac (instrumentation) o tang JIT dau.
Set-Property "System.Runtime.TieredPGO" $false
# 0-9; cao hon = don dep/nen heap gen cu chu dong hon. 0 = mac dinh cua .NET.
Set-Property "System.GC.ConserveMemory" 3

$text = $json | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($config, $text, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Da ap dung cau hinh GC Low-RAM vao:" -ForegroundColor Green
Write-Host "  $config"
Write-Host "Ban goc: $backup"
Write-Host "Khoi phuc: chay lai voi -Restore (hoac dung khoi-phuc-cau-hinh.bat)."
