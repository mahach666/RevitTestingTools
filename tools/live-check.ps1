<#
.SYNOPSIS
    Живая проверка на установленных версиях Revit: надстройка RevitTestHost, смоук режима сервера
    и контрактные тесты RevitUITesting.

.DESCRIPTION
    Для каждого года из -RevitVersion (по умолчанию все установленные из 2020-2027):
      1. собирает в Debug и разворачивает надстройку и пример (Debug-сборка копирует файлы
         в %ProgramData%\Autodesk\Revit\Addins\<год>\, для 2027 в %AppData%\Autodesk\Revit\Addins\2027\);
      2. запускает tools\serve-smoke.ps1;
      3. запускает контрактные тесты RevitUITesting (Category=RevitContract) с REVIT_UI_REVIT_VERSION=<год>.
    Результаты: artifacts\live\<год>\ (журнал смоука, TRX, файлы доказательств) и artifacts\live\summary.md.

    Предусловия на каждый год: Revit этого года установлен, Revit не запущен, свободный commit
    не меньше -MinFreeCommitGb. Год, для которого предусловие не выполнено, пропускается, проверка идёт дальше.
    -DryRun только печатает шаги: ничего не собирает, не разворачивает и не запускает.
#>
param(
    [string[]]$RevitVersion,
    [switch]$SkipContract,
    [double]$MinFreeCommitGb = 5,
    [string]$AutodeskRoot = 'C:\Program Files\Autodesk',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$supported = 2020..2027
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$liveRoot = Join-Path $root 'artifacts\live'

function Get-RevitExe([int]$Year) {
    Join-Path $AutodeskRoot "Revit $Year\Revit.exe"
}

function Get-FreeCommitGb {
    $os = Get-CimInstance Win32_OperatingSystem
    [math]::Round($os.FreeVirtualMemory / 1MB, 1)   # FreeVirtualMemory в КБ
}

function Get-TrxCounts([string]$Path) {
    $xml = [xml](Get-Content -LiteralPath $Path -Raw)
    $c = $xml.TestRun.ResultSummary.Counters
    [pscustomobject]@{
        Total  = [int]$c.total
        Passed = [int]$c.passed
        Failed = [int]$c.failed + [int]$c.error + [int]$c.timeout + [int]$c.aborted
    }
}

function Assert-ExitCode([string]$What) {
    if ($LASTEXITCODE -ne 0) { throw "$What завершилось с кодом $LASTEXITCODE" }
}

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    if ($DryRun) {
        Write-Host "  [dry-run] $Name"
        return 'dry-run'
    }
    Write-Host "  $Name"
    try {
        & $Action
        return 'ok'
    }
    catch {
        Write-Host "    ОШИБКА: $($_.Exception.Message)" -ForegroundColor Red
        return 'FAIL'
    }
}

function Test-YearPreconditions([int]$Year) {
    if (-not (Test-Path -LiteralPath (Get-RevitExe $Year))) { return "Revit $Year не найден: $(Get-RevitExe $Year)" }
    $running = @(Get-Process -Name Revit -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) { return "Revit уже запущен (PID $(($running | ForEach-Object { $_.Id }) -join ', '))" }
    $free = Get-FreeCommitGb
    if ($free -lt $MinFreeCommitGb) { return "Свободный commit $free ГБ меньше $MinFreeCommitGb ГБ" }
    return $null
}

function Invoke-Year([int]$Year) {
    $dir = Join-Path $liveRoot "$Year"
    $row = [ordered]@{ Year = $Year; Build = '-'; Smoke = '-'; Contract = '-'; Note = '' }
    Write-Host "== Revit $Year"

    if ($DryRun) {
        if (-not (Test-Path -LiteralPath (Get-RevitExe $Year))) {
            $row.Note = 'Revit не найден на этом устройстве, в реальном прогоне год был бы пропущен'
        }
    }
    else {
        $problem = Test-YearPreconditions $Year
        if ($problem) {
            Write-Host "  пропуск: $problem" -ForegroundColor Yellow
            $row.Note = $problem
            return [pscustomobject]$row
        }
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }

    $row.Build = Invoke-Step "сборка и развёртывание надстройки и примера (Debug, RevitVersion=$Year)" {
        dotnet build (Join-Path $root 'src\RevitTestHost.Addin\RevitTestHost.Addin.csproj') -c Debug "-p:RevitVersion=$Year" --nologo -v q | Out-Host
        Assert-ExitCode 'сборка надстройки'
        dotnet build (Join-Path $root 'samples\RevitTestHost.SampleTests\RevitTestHost.SampleTests.csproj') -c Debug "-p:RevitVersion=$Year" --nologo -v q | Out-Host
        Assert-ExitCode 'сборка примера'
    }
    if ($row.Build -eq 'FAIL') { return [pscustomobject]$row }

    $row.Smoke = Invoke-Step "tools\serve-smoke.ps1 -RevitVersion $Year" {
        powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'serve-smoke.ps1') -RevitVersion "$Year" | Tee-Object -FilePath (Join-Path $dir 'smoke.log') | Out-Host
        Assert-ExitCode 'serve-smoke'
    }

    if (-not $SkipContract) {
        $status = Invoke-Step "контрактные тесты RevitUITesting (REVIT_UI_REVIT_VERSION=$Year)" {
            $env:REVIT_UI_REVIT_VERSION = "$Year"
            try {
                dotnet test (Join-Path $root 'tests\RevitUITesting.Tests\RevitUITesting.Tests.csproj') -c Release --filter 'Category=RevitContract' --logger "trx;LogFileName=contract-$Year.trx" --results-directory $dir --nologo | Out-Host
                $code = $LASTEXITCODE
            }
            finally {
                Remove-Item Env:\REVIT_UI_REVIT_VERSION -ErrorAction SilentlyContinue
            }
            if ($code -ne 0) { throw "dotnet test завершился с кодом $code" }
        }
        $trx = Join-Path $dir "contract-$Year.trx"
        if (-not $DryRun -and (Test-Path -LiteralPath $trx)) {
            $counts = Get-TrxCounts $trx
            $status = "$status ($($counts.Passed) из $($counts.Total))"
            $evidence = Join-Path $root 'tests\RevitUITesting.Tests\Contract\Evidence'
            $files = @(Get-ChildItem -LiteralPath $evidence -Filter "*-$Year.txt" -ErrorAction SilentlyContinue)
            if ($files.Count -gt 0) {
                New-Item -ItemType Directory -Force -Path (Join-Path $dir 'evidence') | Out-Null
                $files | Copy-Item -Destination (Join-Path $dir 'evidence') -Force
            }
        }
        $row.Contract = $status
    }
    [pscustomobject]$row
}

function Format-Summary($Rows) {
    $lines = @('| Revit | Сборка | Смоук | Контракт | Примечание |', '|---|---|---|---|---|')
    foreach ($r in $Rows) { $lines += "| $($r.Year) | $($r.Build) | $($r.Smoke) | $($r.Contract) | $($r.Note) |" }
    $lines
}

# Подключение точкой (. .\tools\live-check.ps1) даёт функции без запуска проверки
if ($MyInvocation.InvocationName -ne '.') {
    # powershell -File не превращает 2020,2022 в массив: принимаем и массив, и строку с запятыми
    $requested = @($RevitVersion | ForEach-Object { $_ -split '[,; ]+' } | Where-Object { $_ } | ForEach-Object { $_ })
    $bad = @($requested | Where-Object { $_ -notmatch '^\d{4}$' -or ([int]$_ -notin $supported) })
    if ($bad.Count -gt 0) { throw "Неподдерживаемые годы: $($bad -join ', '). Допустимы 2020-2027." }

    if ($requested.Count -gt 0) { $years = @($requested | ForEach-Object { [int]$_ }) }
    elseif ($DryRun) { $years = @($supported) }
    else { $years = @($supported | Where-Object { Test-Path -LiteralPath (Get-RevitExe $_) }) }
    if ($years.Count -eq 0) { throw "Не найдено ни одной установленной версии Revit в $AutodeskRoot. Укажите -RevitVersion или -AutodeskRoot." }

    $rows = @($years | ForEach-Object { Invoke-Year $_ })
    $summary = Format-Summary $rows
    Write-Host ''
    $summary | ForEach-Object { Write-Host $_ }
    if (-not $DryRun) {
        New-Item -ItemType Directory -Force -Path $liveRoot | Out-Null
        Set-Content -LiteralPath (Join-Path $liveRoot 'summary.md') -Value $summary -Encoding UTF8
        Write-Host "Сводка: $(Join-Path $liveRoot 'summary.md')"
    }
    if (@($rows | Where-Object { "$($_.Build)$($_.Smoke)$($_.Contract)" -match 'FAIL' }).Count -gt 0) { exit 1 }
}
