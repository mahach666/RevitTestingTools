using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using FlaUI.Core.WindowsAPI;

namespace RevitUITesting.Uia
{
    /// <summary>
    /// Действия над элементами по правилу «одно действие — один вызов»: нажатие не повторяется при сбое
    /// (повтор мог бы нажать кнопку дважды — второй раз создать объект); результат проверяет вызывающий по постусловию.
    /// Invoke выполняется в фоне: синхронный обработчик команды в приложении держит вызов до нескольких секунд.
    /// Мышь и клавиатура — только если в точке клика / в фокусе окно процесса приложения, иначе ввод ушёл бы в чужое окно.
    /// </summary>
    internal static class UiAct
    {
        /// <summary>Сколько ждать возврата Invoke, прежде чем продолжить (обработчик выполняется дальше в фоне).</summary>
        internal static TimeSpan InvokeGrace { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>Отклик окна по дескриптору (SendMessageTimeout); подменяется в тестах — зависшее окно в них не воспроизвести.</summary>
        internal static Func<IntPtr, TimeSpan, bool> UntilResponsive { get; set; } = FlaUI.Core.Input.Wait.UntilResponsive;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extraInfo);

        private const uint MouseEventMove = 0x0001;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out ClientRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        [StructLayout(LayoutKind.Sequential)]
        private struct ClientRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Перенести курсор в центр клиентской области окна приложения (у главного окна Revit это область вида — в стороне от ленты), чтобы
        /// оставленный на ленте курсор не вызывал всплывающих подсказок. Только если окно принадлежит processId и в этой точке окно того же
        /// процесса; иначе курсор не трогается (ввод в чужие приложения не уходит) — false с записью в журнал. Клика нет, только перемещение.
        /// </summary>
        internal static bool ParkMouse(IntPtr window, int processId, string why)
        {
            if (window == IntPtr.Zero || OwnerPid(window) != processId)
            {
                UiLog.Action($"курсор не перенесён ({why}): окно {window} не принадлежит процессу {processId}");
                return false;
            }
            if (!GetClientRect(window, out ClientRect rect))
            {
                UiLog.Warn($"курсор не перенесён ({why}): GetClientRect окна {window} — ошибка Win32 {Marshal.GetLastWin32Error()}");
                return false;
            }
            Point point = new Point((rect.Right - rect.Left) / 2, (rect.Bottom - rect.Top) / 2);
            if (!ClientToScreen(window, ref point))
            {
                UiLog.Warn($"курсор не перенесён ({why}): ClientToScreen окна {window} — ошибка Win32 {Marshal.GetLastWin32Error()}");
                return false;
            }
            uint owner = OwnerPid(WindowFromPoint(point));
            if (owner != processId)
            {
                UiLog.Action($"курсор не перенесён ({why}): в точке ({point.X},{point.Y}) окно процесса {owner}, а не приложения ({processId})");
                return false;
            }
            if (!SetCursorPos(point.X, point.Y))
            {
                UiLog.Warn($"курсор не перенесён ({why}): SetCursorPos({point.X},{point.Y}) — ошибка Win32 {Marshal.GetLastWin32Error()}");
                return false;
            }
            UiLog.Action($"курсор перенесён в центр окна приложения ({point.X},{point.Y}): {why}");
            return true;
        }

        /// <summary>
        /// Нажать: Invoke; нет Invoke — Toggle; нет и его — клик мышью (требует DPI-осведомлённости).
        /// Элемент недоступен — исключение до действия.
        /// </summary>
        internal static void Press(AutomationElement element, string description)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            TimeSpan read = Wait.DefaultTimeout;
            if (!UiQuery.Read(() => element.Properties.IsEnabled.ValueOrDefault, read, "доступность " + description))
            {
                throw new InvalidOperationException($"Нельзя нажать {description}: элемент недоступен (IsEnabled = false).");
            }
            IInvokePattern invoke = UiQuery.Read(() => element.Patterns.Invoke.PatternOrDefault, read, "InvokePattern " + description);
            if (invoke != null)
            {
                RunOnce(element, description, invoke.Invoke, "Invoke");
                return;
            }
            ITogglePattern toggle = UiQuery.Read(() => element.Patterns.Toggle.PatternOrDefault, read, "TogglePattern " + description);
            if (toggle != null)
            {
                RunOnce(element, description, toggle.Toggle, "Toggle");
                return;
            }
            Click(element, description, MouseButton.Left, false);
        }

        /// <summary>Выполнить вызов паттерна один раз в фоне; сбой «занят» — дождаться отклика приложения, не повторяя.</summary>
        internal static void RunOnce(AutomationElement element, string description, Action call, string how)
        {
            UiLog.Action(how + " " + description);
            Task task = Task.Run(call);
            bool finished;
            try
            {
                finished = task.Wait(InvokeGrace);
            }
            catch (AggregateException aggregate)
            {
                Exception ex = aggregate.InnerException ?? aggregate;
                switch (UiFault.Classify(ex))
                {
                    case UiFaultKind.Busy:
                        UiLog.Warn($"Приложение было занято во время {how} {description}; повтора нет — результат проверяется ожиданием постусловия.");
                        WaitResponsive(element, description, how);
                        return;
                    case UiFaultKind.Gone:
                        throw new InvalidOperationException($"{description}: элемент исчез из дерева UI Automation до нажатия.", ex);
                    default:
                        if (ex is ElementNotEnabledException)
                        {
                            throw new InvalidOperationException($"{description}: элемент недоступен для нажатия.", ex);
                        }
                        throw new InvalidOperationException($"{how} {description} завершился ошибкой: {ex.Message}", ex);
                }
            }
            if (!finished)
            {
                UiLog.Action($"{how} {description}: обработчик ещё выполняется, продолжаем без ожидания его возврата");
                task.ContinueWith(t => UiLog.Warn($"{how} {description} завершился ошибкой в фоне: {t.Exception?.InnerException}"), TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        /// <summary>Клик мышью в центр элемента (или его кликабельную точку) — для элементов, где Invoke не срабатывает.</summary>
        internal static void Click(AutomationElement element, string description, MouseButton button, bool doubleClick)
        {
            string action = (doubleClick ? "двойной " : "") + (button == MouseButton.Right ? "правый клик" : "клик") + " по " + description;
            ProcessDpi.RequireForMouse(action);
            Point point = ClickPoint(element, description);
            int pid = UiQuery.Read(() => element.Properties.ProcessId.ValueOrDefault, Wait.DefaultTimeout, "процесс " + description);
            EnsurePointBelongsTo(point, pid, element, action);
            UiLog.Action(action + $" ({point.X},{point.Y})");
            if (doubleClick)
            {
                Mouse.DoubleClick(point, button);
            }
            else
            {
                Mouse.Click(point, button);
            }
        }

        /// <summary>Нажать клавиши (одновременно) с фокусом на элементе; фокус должен оказаться в окне процесса приложения.</summary>
        internal static void Keys(AutomationElement focusTarget, string description, params VirtualKeyShort[] keys)
        {
            int pid = UiQuery.Read(() => focusTarget.Properties.ProcessId.ValueOrDefault, Wait.DefaultTimeout, "процесс " + description);
            UiQuery.Read(() =>
            {
                focusTarget.Focus();
                return true;
            }, Wait.DefaultTimeout, "фокус на " + description);
            EnsureForeground(pid, focusTarget, description);
            UiLog.Action("клавиши " + string.Join("+", keys) + " → " + description);
            if (keys.Length == 1)
            {
                Keyboard.Type(keys[0]);
            }
            else
            {
                Keyboard.TypeSimultaneously(keys);
            }
        }

        /// <summary>Набрать текст с клавиатуры в элемент (для полей автодополнения, где нужен настоящий ввод); фокус — в окне приложения.</summary>
        internal static void TypeText(AutomationElement focusTarget, string text, string description)
        {
            int pid = UiQuery.Read(() => focusTarget.Properties.ProcessId.ValueOrDefault, Wait.DefaultTimeout, "процесс " + description);
            UiQuery.Read(() =>
            {
                focusTarget.Focus();
                return true;
            }, Wait.DefaultTimeout, "фокус на " + description);
            EnsureForeground(pid, focusTarget, description);
            UiLog.Action($"ввод «{text}» → {description}");
            Keyboard.Type(text ?? "");
        }

        /// <summary>Нажать клавиши, не меняя фокус (он уже там, где нужно: например, в выделенной строке после перехода по ссылке).</summary>
        internal static void KeysToForeground(int processId, string description, params VirtualKeyShort[] keys)
        {
            EnsureForeground(processId, null, description);
            UiLog.Action("клавиши " + string.Join("+", keys) + " → " + description);
            if (keys.Length == 1)
            {
                Keyboard.Type(keys[0]);
            }
            else
            {
                Keyboard.TypeSimultaneously(keys);
            }
        }

        /// <summary>
        /// Дождаться отклика приложения после сбоя «занят» во время действия (до <see cref="Wait.UiBusyTimeout"/>). Дескриптор окна читается
        /// строгим чтением (пока поток UI занят, свойства UIA отвечают таймаутом — это ожидание, а не сбой теста), затем окно опрашивается
        /// сообщением Windows. Приложение так и не ответило — <see cref="AppBusyException"/>, а не молчаливое продолжение.
        /// </summary>
        internal static void WaitResponsive(AutomationElement element, string description, string how)
        {
            string what = "отклик приложения после " + how + " " + description;
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                IntPtr handle = UiQuery.Read(() => WindowHandle(element), Wait.UiBusyTimeout, what);
                if (handle == IntPtr.Zero)
                {
                    UiLog.Warn($"У {description} и его предков нет дескриптора окна — отклик проверен только чтением свойств UI Automation.");
                    return;
                }
                TimeSpan left = Wait.UiBusyTimeout - sw.Elapsed;
                if (!UntilResponsive(handle, left < MinPing ? MinPing : left))
                {
                    throw new AppBusyException(what + " (окно не отвечает на сообщения Windows)", Wait.UiBusyTimeout, sw.Elapsed, 1, null);
                }
            }
            catch (Exception ex) when (UiFault.Classify(ex) == UiFaultKind.Gone)
            {
                UiLog.Action("окно приложения исчезло — ожидание отклика не требуется");
            }
        }

        private static readonly TimeSpan MinPing = TimeSpan.FromSeconds(1);

        /// <summary>Дескриптор окна элемента или ближайшего предка, у которого он есть (у элементов WPF внутри окна своего HWND нет).</summary>
        private static IntPtr WindowHandle(AutomationElement element)
        {
            for (AutomationElement current = element; current != null; current = current.Parent)
            {
                IntPtr handle = current.Properties.NativeWindowHandle.ValueOrDefault;
                if (handle != IntPtr.Zero)
                {
                    return handle;
                }
            }
            return IntPtr.Zero;
        }

        private static Point ClickPoint(AutomationElement element, string description)
        {
            Rectangle rect = UiQuery.Read(() => element.BoundingRectangle, Wait.DefaultTimeout, "прямоугольник " + description);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                throw new InvalidOperationException($"Нельзя кликнуть по {description}: у элемента пустой прямоугольник ({rect}) — он скрыт или вне экрана.");
            }
            return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        }

        private static void EnsurePointBelongsTo(Point point, int processId, AutomationElement element, string action)
        {
            if (OwnerPid(WindowFromPoint(point)) == processId)
            {
                return;
            }
            BringToFront(element);
            uint owner = OwnerPid(WindowFromPoint(point));
            if (owner != processId)
            {
                throw new InvalidOperationException($"Отмена: {action} — в точке ({point.X},{point.Y}) окно другого процесса (pid {owner}), а не приложения (pid {processId}). Окно приложения перекрыто.");
            }
        }

        private static void EnsureForeground(int processId, AutomationElement element, string description)
        {
            if (OwnerPid(GetForegroundWindow()) == processId)
            {
                return;
            }
            if (element != null)
            {
                BringToFront(element);
            }
            uint owner = OwnerPid(GetForegroundWindow());
            if (owner != processId)
            {
                throw new InvalidOperationException($"Отмена ввода с клавиатуры ({description}): активно окно другого процесса (pid {owner}), а не приложения (pid {processId}). Клавиши не отправлены.");
            }
        }

        internal static void BringToFront(AutomationElement element)
        {
            AutomationElement window = element;
            try
            {
                while (window.Parent != null && window.Parent.Parent != null)
                {
                    window = window.Parent;
                }
                window.SetForeground();
                IntPtr handle = window.Properties.NativeWindowHandle.ValueOrDefault;
                if (handle != IntPtr.Zero && GetForegroundWindow() != handle)
                {
                    // Windows не отдаёт передний план фоновому процессу (запрет переключения фокуса): ввод потока
                    // теста на время присоединяется к потоку текущего переднего окна, тогда SetForegroundWindow разрешён.
                    uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                    uint ownThread = GetCurrentThreadId();
                    bool attached = foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
                    try
                    {
                        // Нулевое движение мыши: процесс теста становится источником последнего ввода, и Windows
                        // разрешает ему сменить передний план. Курсор не сдвигается, ввод в чужое окно не уходит.
                        mouse_event(MouseEventMove, 0, 0, 0, UIntPtr.Zero);
                        BringWindowToTop(handle);
                        SetForegroundWindow(handle);
                    }
                    finally
                    {
                        if (attached)
                        {
                            AttachThreadInput(ownThread, foregroundThread, false);
                        }
                    }

                    UiLog.Action("окно приложения выведено на передний план" + (GetForegroundWindow() == handle ? "" : " — не удалось, передний план у другого окна"));
                }
            }
            catch (Exception ex) when (UiFault.IsTransient(ex))
            {
                UiLog.Warn("Не удалось вывести окно приложения на передний план: " + ex.Message);
            }
        }

        /// <summary>
        /// Записать значение через ValuePattern и дождаться его в элементе (до 3 с); не применилось — <see cref="ValueNotAppliedException"/>.
        /// Значение читается строго: если приложение занято, это <see cref="AppBusyException"/>, а не совпадение с пустой строкой.
        /// </summary>
        internal static void SetValue(AutomationElement element, string value, string target)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            IValuePattern pattern = UiQuery.Read(() => element.Patterns.Value.PatternOrDefault, Wait.DefaultTimeout, "ValuePattern " + target);
            if (pattern == null)
            {
                throw new InvalidOperationException($"{target}: нет ValuePattern — ввод значения через UI Automation невозможен ({element.Describe()}).");
            }
            // запись выполняется один раз и в фоне, как любое действие; сбой «занят» не повторяет её — результат проверяет чтение ниже
            RunOnce(element, target, () => pattern.SetValue(value ?? ""), $"SetValue «{value}» →");
            string actual = "";
            if (!Wait.Holds(() => (actual = UiQuery.Read(() => pattern.Value.ValueOrDefault ?? "", Wait.DefaultTimeout, "значение " + target)) == (value ?? ""), TimeSpan.FromSeconds(3)))
            {
                throw new ValueNotAppliedException(target, value, actual);
            }
        }

        private static uint OwnerPid(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return 0;
            }
            GetWindowThreadProcessId(hwnd, out uint pid);
            return pid;
        }
    }
}
