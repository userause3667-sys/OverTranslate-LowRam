<#
.SYNOPSIS
    Samples a running OverTranslate process and writes a CSV + a summary, so before/after runs can be compared.

.DESCRIPTION
    Records Working Set, Private Bytes and CPU % of the process every few seconds for a fixed time.
    Run it once with the ORIGINAL build and once with the Low-RAM build, doing the SAME thing during the
    run (see PERFORMANCE_REPORT.md for the scenarios), then compare the two summaries.

    It measures from outside the process, so it cannot see the managed heap: open the in-app
    "Hieu nang / Performance" panel (tray menu) for that number.

.EXAMPLE
    .\measure-performance.ps1 -Label goc-cho      -Seconds 120
    .\measure-performance.ps1 -Label lowram-cho   -Seconds 120
#>
[CmdletBinding()]
param(
    [string]$ProcessName = "OverTranslate",
    [Parameter(Mandatory)][string]$Label,
    [int]$Seconds = 120,
    [int]$IntervalSeconds = 2,
    [string]$OutDir = (Join-Path (Get-Location) "perf")
)

$ErrorActionPreference = "Stop"
$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Sort-Object StartTime | Select-Object -Last 1
if (-not $proc) { throw "Khong thay tien trinh '$ProcessName'. Hay mo OverTranslate truoc." }

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$csv = Join-Path $OutDir "$Label.csv"
$cores = [Environment]::ProcessorCount
$rows = New-Object System.Collections.Generic.List[object]

$lastCpu = $proc.TotalProcessorTime.TotalSeconds
$lastTime = Get-Date
$end = $lastTime.AddSeconds($Seconds)
Write-Host "Do '$Label' tren PID $($proc.Id) trong $Seconds giay..." -ForegroundColor Cyan

while ((Get-Date) -lt $end) {
    Start-Sleep -Seconds $IntervalSeconds
    $proc.Refresh()
    if ($proc.HasExited) { Write-Warning "Tien trinh da thoat."; break }
    $now = Get-Date
    $cpu = $proc.TotalProcessorTime.TotalSeconds
    $elapsed = ($now - $lastTime).TotalSeconds
    $cpuPct = if ($elapsed -gt 0) { ($cpu - $lastCpu) / ($elapsed * $cores) * 100 } else { 0 }
    $lastCpu = $cpu
    $lastTime = $now
    $rows.Add([pscustomobject]@{
        Time         = $now.ToString("HH:mm:ss")
        WorkingSetMB = [math]::Round($proc.WorkingSet64 / 1MB, 1)
        PrivateMB    = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
        CpuPercent   = [math]::Round($cpuPct, 2)
    })
}

if ($rows.Count -eq 0) { throw "Khong lay duoc mau nao." }
$rows | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8

$ws = $rows | Measure-Object WorkingSetMB -Minimum -Average -Maximum
$pv = $rows | Measure-Object PrivateMB -Minimum -Average -Maximum
$cp = $rows | Measure-Object CpuPercent -Average -Maximum
Write-Host ""
Write-Host "== $Label ($($rows.Count) mau, $cores luong CPU) =="
"{0,-14} min {1,8:N1}  tb {2,8:N1}  max {3,8:N1}  MB" -f "Working Set", $ws.Minimum, $ws.Average, $ws.Maximum
"{0,-14} min {1,8:N1}  tb {2,8:N1}  max {3,8:N1}  MB" -f "Private Bytes", $pv.Minimum, $pv.Average, $pv.Maximum
"{0,-14} tb  {1,8:N2}  max {2,8:N2}  % (cua tong CPU)" -f "CPU", $cp.Average, $cp.Maximum
Write-Host "CSV: $csv"
