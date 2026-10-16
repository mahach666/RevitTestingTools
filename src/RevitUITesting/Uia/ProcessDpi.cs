using System;
using System.Runtime.InteropServices;

namespace RevitUITesting.Uia
{
    /// <summary>Фактический режим DPI процесса тестов.</summary>
    public sealed class DpiStatus
    {
        internal DpiStatus(bool isPerMonitor, int systemDpi, string how)
        {
            IsPerMonitor = isPerMonitor;
            SystemDpi = systemDpi;
            How = how;
        }

        /// <summary>Процесс осведомлён о DPI по мониторам: координаты UI Automation и мыши совпадают на любом масштабе.</summary>
        public bool IsPerMonitor { get; }

        /// <summary>Системный DPI (96 = 100 %, 168 = 175 %).</summary>
        public int SystemDpi { get; }

        /// <summary>Как получен режим (для журнала).</summary>
        public string How { get; }

        /// <summary>Клики мышью попадают в элементы: процесс Per-Monitor или масштаб экрана 100 %.</summary>
        public bool MouseCoordinatesMatch => IsPerMonitor || SystemDpi == 96;

        /// <summary>Фактический режим DPI процесса тестов.</summary>
        public static DpiStatus Current => ProcessDpi.Current();

        /// <summary>
        /// Объявить процесс тестов осведомлённым о DPI (Per-Monitor V2) — до первого обращения к UI Automation; итог пишется в журнал.
        /// Нужно инструментам и тестам без сеанса приложения: на экране больше 100 % без этого клики мышью попадают мимо элементов.
        /// </summary>
        public static DpiStatus EnableForProcess()
        {
            UiLog.Action(ProcessDpi.Apply());
            return ProcessDpi.Current();
        }

        public override string ToString()
        {
            return $"DPI {SystemDpi} ({SystemDpi * 100 / 96} %), {(IsPerMonitor ? "Per-Monitor" : "не Per-Monitor")}; {How}";
        }
    }

    /// <summary>
    /// Осведомлённость процесса тестов о DPI.
    /// На экране 175 % процесс без осведомлённости получает от UI Automation логические координаты, а SendInput принимает физические:
    /// клики мышью попадают мимо. С Per-Monitor V2 работают клик, правый и двойной клик (проверено вне Revit).
    /// Включать до создания первого объекта UI Automation.
    /// </summary>
    internal static class ProcessDpi
    {
        private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);
        private static string _how = "режим процесса не менялся";

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        /// <summary>Объявить Per-Monitor V2. Повторный вызов даёт ERROR_ACCESS_DENIED (режим уже задан) — это не сбой.</summary>
        internal static string Apply()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(PerMonitorAwareV2))
                {
                    return _how = "осведомлённость о DPI включена: Per-Monitor V2";
                }
                int error = Marshal.GetLastWin32Error();
                if (error == 5)
                {
                    return _how = "осведомлённость о DPI уже была задана до вызова";
                }
                if (SetProcessDPIAware())
                {
                    return _how = "осведомлённость о DPI: System (Per-Monitor V2 недоступен, код " + error + ")";
                }
                return _how = "осведомлённость о DPI не включена (коды " + error + " и " + Marshal.GetLastWin32Error() + ")";
            }
            catch (EntryPointNotFoundException)
            {
                // SetProcessDpiAwarenessContext появился в Windows 10 1703
                return _how = SetProcessDPIAware()
                    ? "осведомлённость о DPI: System (SetProcessDpiAwarenessContext отсутствует)"
                    : "осведомлённость о DPI не включена: функции user32 недоступны";
            }
        }

        internal static DpiStatus Current()
        {
            try
            {
                int awareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
                // DPI_AWARENESS: 0 unaware, 1 system aware, 2 per monitor aware
                return new DpiStatus(awareness == 2, (int)GetDpiForSystem(), _how);
            }
            catch (EntryPointNotFoundException)
            {
                // функции есть начиная с Windows 10 1607; на более старых системах режим не определить
                return new DpiStatus(false, 0, _how + "; режим не определён (нет функций Windows 10 1607)");
            }
        }

        /// <summary>Мышь допустима только при совпадении координат; иначе — исключение с инструкцией, а не промах.</summary>
        internal static void RequireForMouse(string action)
        {
            DpiStatus status = Current();
            if (!status.MouseCoordinatesMatch)
            {
                throw new InvalidOperationException(
                    $"Действие «{action}» выполняется мышью, но координаты UI Automation и мыши не совпадают ({status}). " +
                    "Включите \"dpiAware\": true в revit-ui-tests.json (или REVIT_UI_DPI_AWARE=1): осведомлённость о DPI должна быть задана до первого обращения к UI Automation.");
            }
        }
    }
}
