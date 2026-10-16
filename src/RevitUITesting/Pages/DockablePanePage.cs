using System;
using FlaUI.Core.AutomationElements;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Pages
{
    /// <summary>
    /// Панель Revit (DockablePane) по заголовку: корень — содержимое панели. Найденная рамка запоминается и годится, пока её окно видимо:
    /// NativeWindowHandle не 0, IsWindowVisible по нему и строгое чтение IsOffscreen = false. Скрытая панель Properties исчезает из дерева
    /// UIA, а её запомненная рамка (окно MFC) на чтение IsOffscreen отвечает false (Evidence/closing-run-2022.txt), поэтому одного IsOffscreen
    /// мало. Полный поиск по дереву главного окна — когда запомненной рамки нет, у неё нет окна, окно скрыто, рамка исчезла или ушла за
    /// пределы экрана; рамка без окна поэтому ищется на каждом обращении. Неоднозначность заголовка проверяется только полным поиском.
    /// Скрытую панель page-object плагина открывает сам (кнопкой своей ленты), затем ждёт её через <see cref="RevitPanes.Find"/>.
    /// </summary>
    public class DockablePanePage : UiPage
    {
        private readonly Func<TimeSpan, AutomationElement> _search;
        private readonly Func<IntPtr, bool> _windowShown;
        private AutomationElement _known;

        public DockablePanePage(RevitApp app, string title)
            : this(app, title, null, null, null)
        {
        }

        /// <summary>
        /// found — рамка, уже найденная полным поиском (<see cref="RevitPanes.Find"/>); search — полный поиск (null — <see cref="RevitPanes"/>
        /// приложения); windowShown — окно существует и видимо (null — <see cref="Native.IsShown"/>).
        /// </summary>
        internal DockablePanePage(RevitApp app, string title, AutomationElement found, Func<TimeSpan, AutomationElement> search, Func<IntPtr, bool> windowShown)
            : base(app, "панель «" + title + "»")
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException("Нужен заголовок панели.", nameof(title));
            }
            Title = title;
            _known = found;
            _search = search ?? (timeout => App.Panes.TryElement(Title, timeout));
            _windowShown = windowShown ?? Native.IsShown;
        }

        public string Title { get; }

        /// <summary>Панель сейчас показана (есть в дереве UI Automation и не за пределами экрана).</summary>
        public bool IsShown => ResolveRoot() != null;

        protected override AutomationElement ResolveRoot()
        {
            TimeSpan timeout = ReadTimeout;
            AutomationElement known = _known;
            if (known != null)
            {
                // строгие чтения: «занят» — AppBusyException (запомненная рамка остаётся), «исчезла», «нет окна», «окно скрыто» и «за экраном» — искать заново
                IntPtr handle = UiRead.OrGone(() => known.Properties.NativeWindowHandle.ValueOrDefault, IntPtr.Zero, timeout, $"HWND панели Revit «{Title}»");
                if (handle != IntPtr.Zero && _windowShown(handle)
                    && !UiRead.OrGone(() => known.Properties.IsOffscreen.ValueOrDefault, true, timeout, $"IsOffscreen панели Revit «{Title}»"))
                {
                    return known;
                }
                _known = null;
            }
            _known = _search(timeout);
            return _known;
        }
    }
}
