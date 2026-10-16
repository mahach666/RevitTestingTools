<#
.SYNOPSIS
    Живой smoke режима сервера RevitTestHost: запуск Revit с REVITTESTHOST_SERVE, ping до idle,
    run пробы @active без документа (error), open @new (путь в temp пользователя, server.log рядом),
    run пробы @active с параметром, проверка ACTIVE_DOCUMENT, завершение Revit.

.DESCRIPTION
    Требует: установленный Revit, развёрнутые надстройку (Debug-сборка RevitTestHost.Addin) и пример
    RevitTestHost.SampleTests (Debug-сборка samples\RevitTestHost.SampleTests). Revit.exe не должен быть запущен,
    свободный commit — не меньше 5 ГБ. Revit в конце убивается (Kill): файлы new-*.rvt остаются
    в %TEMP%\RevitTestHost\serve\<канал>\, где %TEMP% — временная папка пользователя (TEMP из его окружения,
    иначе %LOCALAPPDATA%\Temp), как в надстройке; не папка сеанса %TEMP%\<guid>\, которую Revit ставит своему процессу.

    Пока канал не появился, скрипт отвечает «Always Load» в диалогах Revit «Security - Unsigned Add-In»
    (окно класса #32770) для надстроек из папки Addins\<версия>\, куда её кладёт Debug-сборка (%ProgramData%\Autodesk\Revit\,
    для 2027 %AppData%\Autodesk\Revit\) (сообщение TDM_CLICK_BUTTON
    с id 1001; закрывать окно нельзя — это равносильно «Do Not Load»). Диалог для надстройки из другой папки
    не трогается. Заголовок и текст кнопки сверяются по-английски: скрипт запускает Revit с /language ENU
    и рассчитывает на него.
#>
param(
    [string]$RevitVersion = '2022',
    [int]$StartTimeoutSec = 300,
    [int]$ResponseTimeoutSec = 120,
    [double]$MinFreeCommitGb = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- Предусловия ---
$running = @(Get-Process -Name Revit -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw "Revit.exe уже запущен (PID $(($running | ForEach-Object { $_.Id }) -join ', ')). Закройте его перед smoke."
}

$os = Get-CimInstance Win32_OperatingSystem
$freeCommitGb = [math]::Round($os.FreeVirtualMemory / 1MB, 1) # FreeVirtualMemory — в КБ
if ($freeCommitGb -lt $MinFreeCommitGb) {
    throw "Свободный commit $freeCommitGb ГБ меньше $MinFreeCommitGb ГБ. Закройте тяжёлые процессы (docker/vmmemWSL, браузеры) и повторите."
}

$revitExe = "C:\Program Files\Autodesk\Revit $RevitVersion\Revit.exe"
# Как RevitAddinsRoot в build\RevitVersion.props: Revit 2027 не загружает манифесты из %ProgramData%
$addinsBase = if ($RevitVersion -eq '2027') { $env:APPDATA } else { $env:ProgramData }
$addinsDir = Join-Path $addinsBase "Autodesk\Revit\Addins\$RevitVersion\"
$testHostDir = Join-Path $addinsDir 'TestHost'
foreach ($required in @($revitExe, (Join-Path $testHostDir 'RevitTestHost.Core.dll'), (Join-Path $testHostDir 'Tests\RevitTestHost.SampleTests.dll'))) {
    if (-not (Test-Path $required)) { throw "Не найден $required. Соберите и разверните надстройку и пример (Debug)." }
}

Write-Host "Свободный commit: $freeCommitGb ГБ"

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class SmokeWindows {
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    public static List<IntPtr> Find(int processId, string title, string className) {
        var result = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == (uint)processId && IsWindowVisible(h)) {
                var text = new StringBuilder(512);
                GetWindowText(h, text, text.Capacity);
                var cls = new StringBuilder(256);
                GetClassName(h, cls, cls.Capacity);
                if (text.ToString() == title && cls.ToString() == className) result.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
"@

# Диалог Revit о неподписанной надстройке (TaskDialog_Security_Unsigned_File_Loading) блокирует запуск до ответа.
# В дереве UI Automation он не ребёнок корня, а кнопки (DirectUI, AutomationId CommandButton_1001..1003) без InvokePattern:
# окно (заголовок и класс #32770, только процесс Revit) ищем через EnumWindows, текст читаем через UIA,
# «Always Load» нажимаем сообщением TDM_CLICK_BUTTON с id 1001. Окно может закрыться в любой момент между шагами
# (ответил пользователь или другой обработчик) — это не ошибка, диалог считается отвеченным.
$script:answeredDialogs = @{}
$script:dialogAnswers = 0
$TDM_CLICK_BUTTON = 0x0466
$IDALWAYSLOAD = 1001

# Ждёт закрытия окна; true — окно исчезло за $TimeoutMs.
function Wait-WindowGone([IntPtr]$Hwnd, [int]$TimeoutMs) {
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ([SmokeWindows]::IsWindow($Hwnd)) {
        if ((Get-Date) -gt $deadline) { return $false }
        Start-Sleep -Milliseconds 100
    }
    return $true
}

# Текст диалога и кнопка 1001 через UIA; $null — окно исчезло (уже отвечено).
function Read-UnsignedAddInDialog([IntPtr]$Hwnd) {
    if (-not [SmokeWindows]::IsWindow($Hwnd)) { return $null }
    try {
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($Hwnd)
        $elements = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
        $text = (@($elements | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join ' | ') -replace '\s+', ' '
        $button = $elements | Where-Object { $_.Current.AutomationId -eq 'CommandButton_1001' } | Select-Object -First 1
        $buttonName = if ($null -eq $button) { $null } else { $button.Current.Name }
        return @{ Text = $text; ButtonName = $buttonName }
    }
    catch {
        # Окно закрылось посреди чтения: UIA сообщает об этом ElementNotAvailableException, а иногда COM- или
        # ArgumentException. Окно на месте — значит, ошибка настоящая, пробрасываем.
        if ([SmokeWindows]::IsWindow($Hwnd)) { throw }
        return $null
    }
}

function Approve-UnsignedAddInDialogs([int]$ProcessId, [string]$AddinsDir) {
    foreach ($hwnd in [SmokeWindows]::Find($ProcessId, 'Security - Unsigned Add-In', '#32770')) {
        if ($script:answeredDialogs.ContainsKey($hwnd)) { continue }
        $dialog = Read-UnsignedAddInDialog $hwnd
        if ($null -eq $dialog) {
            Write-Host ''
            Write-Host "Диалог о неподписанной надстройке закрылся до ответа скрипта (окно $hwnd)."
            continue
        }
        $text = $dialog.Text
        if ($text.IndexOf($AddinsDir, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            Write-Warning "Диалог о неподписанной надстройке не про $AddinsDir — не отвечаем: $text"
            $script:answeredDialogs[$hwnd] = $false
            continue
        }
        if ($dialog.ButtonName -ne 'Always Load') {
            throw "В диалоге о неподписанной надстройке нет кнопки CommandButton_1001 «Always Load»: $text"
        }

        # Отвечено — только когда окно закрылось; не закрылось за ~2 с — ещё одна попытка, потом ошибка
        $gone = $false
        foreach ($attempt in 1..2) {
            if (-not [SmokeWindows]::IsWindow($hwnd)) { $gone = $true; break }
            if (-not [SmokeWindows]::PostMessage($hwnd, $TDM_CLICK_BUTTON, [IntPtr]$IDALWAYSLOAD, [IntPtr]::Zero)) {
                if (-not [SmokeWindows]::IsWindow($hwnd)) { $gone = $true; break }
                throw "Не удалось отправить TDM_CLICK_BUTTON диалогу о неподписанной надстройке (попытка $attempt): $text"
            }
            if (Wait-WindowGone $hwnd 2000) { $gone = $true; break }
        }
        if (-not $gone) {
            throw "Диалог о неподписанной надстройке не закрылся после двух TDM_CLICK_BUTTON 1001 («Always Load»): $text"
        }
        $script:answeredDialogs[$hwnd] = $true
        $script:dialogAnswers++
        Write-Host ''
        Write-Host "Ответ «Always Load» на диалог о неподписанной надстройке (окно закрылось): $text"
    }
}

# Временная папка пользователя — так же, как UserTemp.Directory в надстройке (не TEMP процесса).
function Get-UserTempDirectory {
    $value = [Environment]::GetEnvironmentVariable('TEMP', [EnvironmentVariableTarget]::User)
    if (-not [string]::IsNullOrWhiteSpace($value)) { return [Environment]::ExpandEnvironmentVariables($value.Trim()) }
    return Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Temp'
}
# --- Запуск Revit в режиме сервера ---
$pipeName = 'RevitTestHost-' + [guid]::NewGuid().ToString('N')
$psi = New-Object System.Diagnostics.ProcessStartInfo $revitExe
$psi.Arguments = '/language ENU'
$psi.UseShellExecute = $false
$psi.EnvironmentVariables['REVITTESTHOST_SERVE'] = $pipeName
$psi.EnvironmentVariables.Remove('REVITTESTHOST_CLI')
$revit = [System.Diagnostics.Process]::Start($psi)
Write-Host "Revit запущен: PID $($revit.Id), канал $pipeName"

$utf8 = New-Object System.Text.UTF8Encoding($false)
$pipe = $null
$reader = $null
$script:nextId = 0

function Invoke-HostRaw([string]$Command, [hashtable]$Arguments) {
    $script:nextId++
    $line = @{ id = $script:nextId; cmd = $Command; args = $Arguments } | ConvertTo-Json -Compress -Depth 6
    $bytes = $utf8.GetBytes($line + "`n")
    $pipe.Write($bytes, 0, $bytes.Length)
    $pipe.Flush()
    $task = $reader.ReadLineAsync()
    if (-not $task.Wait($ResponseTimeoutSec * 1000)) {
        throw "Revit занят: нет ответа на '$Command' за $ResponseTimeoutSec с (модальное окно в Revit?)."
    }
    if ($null -eq $task.Result) { throw "Сервер закрыл канал во время '$Command'." }
    $response = $task.Result | ConvertFrom-Json
    if ($response.id -ne $script:nextId) { throw "Ответ с id $($response.id), ожидался $($script:nextId)." }
    return $response
}

function Invoke-Host([string]$Command, [hashtable]$Arguments = @{}) {
    $response = Invoke-HostRaw $Command $Arguments
    if (-not $response.ok) { throw "$Command -> $($response.error.code): $($response.error.message)" }
    return $response.result
}

function Wait-Idle([int]$TimeoutSec) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($true) {
        $ping = Invoke-Host 'ping'
        if ($ping.idle) { return $ping }
        if ((Get-Date) -gt $deadline) { throw "Revit не перешёл в idle за $TimeoutSec с." }
        Start-Sleep -Milliseconds 500
    }
}

try {
    # Канал появляется после ApplicationInitialized; до этого Connect бросает таймаут
    $deadline = (Get-Date).AddSeconds($StartTimeoutSec)
    while ($true) {
        if ($revit.HasExited) { throw "Revit завершился с кодом $($revit.ExitCode) до появления канала." }
        Approve-UnsignedAddInDialogs $revit.Id $addinsDir
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName, [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
        try {
            $pipe.Connect(2000)
            break
        }
        catch {
            $pipe.Dispose()
            if ((Get-Date) -gt $deadline) { throw "Канал $pipeName не появился за $StartTimeoutSec с: $($_.Exception.Message)" }
            Write-Host -NoNewline '.'
        }
    }
    Write-Host ''
    $reader = New-Object System.IO.StreamReader($pipe, $utf8, $false, 4096, $true)

    $ping = Wait-Idle $ResponseTimeoutSec
    Write-Host "ping: Revit $($ping.revitVersion), pid $($ping.pid), сервер $($ping.serverVersion), idle $($ping.idle)"
    if ($ping.pid -ne $revit.Id) { throw "pid в ping ($($ping.pid)) не совпадает с запущенным Revit ($($revit.Id))." }

    # Без активного документа проба @active получает error с понятным текстом
    $report = Invoke-Host 'run' @{ filter = 'RevitTestHost.SampleTests.*'; parameters = @{ expectedTitle = 'none' } }
    $r = @($report.results)
    Write-Host "run без документа: total $($report.summary.total), errored $($report.summary.errored); $(if ($r.Count -gt 0) { "[$($r[0].status)] $($r[0].message)" })"
    if ($report.summary.total -ne 1 -or $report.summary.errored -ne 1 -or $r[0].message -notlike '*нет активного документа*') {
        throw "run без активного документа: ожидался один error «нет активного документа», получено $($report | ConvertTo-Json -Compress -Depth 6)"
    }

    $opened = Invoke-Host 'open' @{ path = '@new' }
    Write-Host "open @new: '$($opened.title)' -> $($opened.path)"
    $expectedDir = Join-Path (Get-UserTempDirectory) "RevitTestHost\serve\$pipeName"
    $expectedPath = Join-Path $expectedDir 'new-1.rvt'
    if (-not [string]::Equals($opened.path, $expectedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "@new сохранён в $($opened.path), ожидался $expectedPath."
    }
    if (-not (Test-Path (Join-Path $expectedDir 'server.log'))) { throw "Нет журнала сервера $expectedDir\server.log." }
    Write-Host "Папка канала (temp пользователя): $expectedDir — new-1.rvt и server.log на месте"

    $ping = Wait-Idle $ResponseTimeoutSec
    if ($null -eq $ping.activeDocument -or $ping.activeDocument.title -ne $opened.title) {
        throw "Активный документ после open: '$($ping.activeDocument)', ожидался '$($opened.title)'."
    }

    $docs = Invoke-Host 'documents'
    foreach ($d in $docs.documents) { Write-Host "  документ: '$($d.title)' active=$($d.isActive) modified=$($d.isModified) family=$($d.isFamilyDocument)" }

    $report = Invoke-Host 'run' @{ filter = 'RevitTestHost.SampleTests.*'; parameters = @{ expectedTitle = $opened.title } }
    $s = $report.summary
    Write-Host "run: total $($s.total), passed $($s.passed), failed $($s.failed), errored $($s.errored), skipped $($s.skipped)"
    foreach ($r in $report.results) { Write-Host "  [$($r.status)] $($r.class).$($r.method) $(if ($r.PSObject.Properties['message']) { $r.message })" }
    if ($s.total -lt 1 -or ($s.failed + $s.errored) -gt 0) { throw 'Проба @active не прошла.' }

    $close = Invoke-HostRaw 'close' @{ title = $opened.title }
    if ($close.ok -or $close.error.code -ne 'ACTIVE_DOCUMENT') {
        throw "close активного документа: ожидалась ошибка ACTIVE_DOCUMENT, получено $($close | ConvertTo-Json -Compress -Depth 4)"
    }
    Write-Host 'close активного документа -> ACTIVE_DOCUMENT (как ожидается)'

    Write-Host 'SMOKE OK'
}
finally {
    if ($null -ne $reader) { $reader.Dispose() }
    if ($null -ne $pipe) { $pipe.Dispose() }
    if (-not $revit.HasExited) {
        $revit.Kill()
        if (-not $revit.WaitForExit(60000)) { Write-Warning "Revit (PID $($revit.Id)) не завершился за 60 с после Kill." }
    }
    Write-Host "Ответов на диалог о неподписанной надстройке: $($script:dialogAnswers)"
    Write-Host "Журнал сервера: $(Join-Path (Get-UserTempDirectory) "RevitTestHost\serve\$pipeName\server.log")"
}
