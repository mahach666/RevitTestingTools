using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using RevitUITesting.Uia;

namespace RevitUITesting.Revit
{
    /// <summary>Исход PostMessage окну, которое может закрыться в любой момент.</summary>
    internal enum PostOutcome
    {
        /// <summary>Сообщение поставлено в очередь окна.</summary>
        Posted,

        /// <summary>Не поставлено: окна уже нет (закрылось само или ответил другой обработчик) — не сбой.</summary>
        WindowGone,

        /// <summary>Не поставлено, а окно живо — сбой.</summary>
        Failed,
    }

    /// <summary>
    /// user32 для окон Revit: владелец (главное окно его не имеет), доступность (недоступно — открыто модальное окно), WM_CLOSE без ожидания,
    /// перечень окон верхнего уровня без UI Automation и нажатие кнопки TaskDialog сообщением TDM_CLICK_BUTTON.
    /// </summary>
    internal static class RevitNative
    {
        private const uint GwOwner = 4;
        private const uint WmClose = 0x0010;

        /// <summary>TDM_CLICK_BUTTON = WM_USER + 102: wParam — id кнопки TaskDialog.</summary>
        internal const uint TdmClickButton = 0x0466;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("kernel32.dll", EntryPoint = "SetLastError")]
        private static extern void ClearLastError(uint code);

        internal static IntPtr GetOwner(IntPtr hWnd)
        {
            return GetWindow(hWnd, GwOwner);
        }

        /// <summary>
        /// Послать WM_CLOSE и не ждать обработки (поток Revit может быть занят). true — сообщение принято; false — окно закрылось раньше
        /// (PostMessage не прошёл, окна уже нет); живое окно не приняло сообщение — Win32Exception.
        /// </summary>
        internal static bool PostClose(IntPtr hWnd, string description)
        {
            if (hWnd == IntPtr.Zero)
            {
                throw new ArgumentException("Нет HWND окна для WM_CLOSE: " + description, nameof(hWnd));
            }
            return Post(hWnd, WmClose, IntPtr.Zero, "PostMessage(WM_CLOSE) не принят окном " + description);
        }

        /// <summary>
        /// Послать TaskDialog сообщение TDM_CLICK_BUTTON с id кнопки и не ждать обработки. true — сообщение принято; false — окно закрылось раньше;
        /// живое окно не приняло сообщение — Win32Exception с описанием окна.
        /// </summary>
        internal static bool PostTaskDialogClick(IntPtr hWnd, int buttonId, string description)
        {
            if (hWnd == IntPtr.Zero)
            {
                throw new ArgumentException("Нет HWND окна для TDM_CLICK_BUTTON: " + description, nameof(hWnd));
            }
            return Post(hWnd, TdmClickButton, new IntPtr(buttonId), $"PostMessage(TDM_CLICK_BUTTON, {buttonId}) не принят окном {description}");
        }

        /// <summary>
        /// Исход PostMessage: принято — <see cref="PostOutcome.Posted"/>; не принято, а окна уже нет (закрылось между проверкой и отправкой) —
        /// <see cref="PostOutcome.WindowGone"/>; не принято живым окном — <see cref="PostOutcome.Failed"/>.
        /// </summary>
        internal static PostOutcome Outcome(bool posted, bool windowExists)
        {
            if (posted)
            {
                return PostOutcome.Posted;
            }
            return windowExists ? PostOutcome.Failed : PostOutcome.WindowGone;
        }

        private static bool Post(IntPtr hWnd, uint message, IntPtr wParam, string failure)
        {
            bool posted = PostMessage(hWnd, message, wParam, IntPtr.Zero);
            int error = Marshal.GetLastWin32Error();
            switch (Outcome(posted, posted || IsWindow(hWnd)))
            {
                case PostOutcome.Posted:
                    return true;
                case PostOutcome.WindowGone:
                    return false;
                default:
                    throw new Win32Exception(error, failure);
            }
        }

        /// <summary>
        /// Окна верхнего уровня процесса processId (EnumWindows) с классом, заголовком и видимостью. Процесс окна читается первым: класс и заголовок —
        /// только у окон processId, окна других процессов не читаются и не могут сорвать перечень. Окно, закрывшееся во время чтения, в перечень
        /// не входит; сбой чтения у живого окна — Win32Exception. processId — чужой процесс (Revit): для окна другого процесса GetWindowText
        /// не посылает WM_GETTEXT и не ждёт его занятый поток.
        /// </summary>
        internal static List<NativeWindowInfo> TopLevelWindows(int processId)
        {
            List<IntPtr> handles = new List<IntPtr>();
            EnumWindowsProc collect = (hWnd, lParam) =>
            {
                handles.Add(hWnd);
                return true;
            };
            bool enumerated = EnumWindows(collect, IntPtr.Zero);
            int error = Marshal.GetLastWin32Error();
            GC.KeepAlive(collect);
            if (!enumerated)
            {
                throw new Win32Exception(error, "EnumWindows не перечислил окна верхнего уровня");
            }
            List<NativeWindowInfo> result = new List<NativeWindowInfo>(handles.Count);
            foreach (IntPtr hWnd in handles)
            {
                NativeWindowInfo info = TryRead(hWnd, processId);
                if (info != null)
                {
                    result.Add(info);
                }
            }
            return result;
        }

        /// <summary>Свойства окна процесса processId; null — окно другого процесса или закрылось во время чтения.</summary>
        private static NativeWindowInfo TryRead(IntPtr hWnd, int processId)
        {
            uint thread = GetWindowThreadProcessId(hWnd, out uint owner);
            if (thread == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (!IsWindow(hWnd))
                {
                    return null;
                }
                // процесс живого окна неизвестен — нельзя решить, наше ли оно, поэтому сбой, а не пропуск
                throw new Win32Exception(error, "GetWindowThreadProcessId не вернул процесс окна " + Hex(hWnd));
            }
            if (unchecked((int)owner) != processId)
            {
                return null;
            }

            // имя класса — до 256 символов, плюс завершающий ноль
            StringBuilder className = new StringBuilder(257);
            if (GetClassName(hWnd, className, className.Capacity) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (!IsWindow(hWnd))
                {
                    return null;
                }
                throw new Win32Exception(error, "GetClassName не вернул класс окна " + Hex(hWnd));
            }

            string title = ReadTitle(hWnd, out bool gone);
            if (gone)
            {
                return null;
            }
            return new NativeWindowInfo(hWnd, processId, className.ToString(), title, Native.IsWindowVisible(hWnd));
        }

        /// <summary>
        /// Заголовок окна. GetWindowTextLength и GetWindowText возвращают 0 и для пустого заголовка, и при сбое: код ошибки сбрасывается перед
        /// вызовом, поэтому 0 без кода — пустой заголовок, 0 с кодом у закрывшегося окна — gone, у живого — Win32Exception.
        /// </summary>
        private static string ReadTitle(IntPtr hWnd, out bool gone)
        {
            gone = false;
            ClearLastError(0);
            int length = GetWindowTextLength(hWnd);
            if (length == 0)
            {
                return EmptyOrFail(hWnd, Marshal.GetLastWin32Error(), "GetWindowTextLength", out gone);
            }
            StringBuilder text = new StringBuilder(length + 1);
            ClearLastError(0);
            int copied = GetWindowText(hWnd, text, text.Capacity);
            if (copied == 0)
            {
                return EmptyOrFail(hWnd, Marshal.GetLastWin32Error(), "GetWindowText", out gone);
            }
            return text.ToString();
        }

        private static string EmptyOrFail(IntPtr hWnd, int error, string call, out bool gone)
        {
            gone = !IsWindow(hWnd);
            if (error != 0 && !gone)
            {
                throw new Win32Exception(error, $"{call} не прочитал заголовок окна {Hex(hWnd)}");
            }
            return "";
        }

        internal static string Hex(IntPtr hWnd)
        {
            return "0x" + hWnd.ToInt64().ToString("X");
        }
    }
}
