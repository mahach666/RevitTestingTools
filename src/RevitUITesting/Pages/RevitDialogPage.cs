using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using RevitUITesting.Revit;
using RevitUITesting.Uia;
using Wait = RevitUITesting.Uia.Wait;

namespace RevitUITesting.Pages
{
    /// <summary>Окно Revit или плагина (модальное или немодальное). Закрытость проверяется по HWND.</summary>
    public class RevitDialogPage : UiPage
    {
        private readonly Window _window;
        private readonly IntPtr _handle;

        public RevitDialogPage(RevitApp app, AutomationElement window)
            : base(app, "окно «" + Describe(Require(window)) + "»")
        {
            _window = window.AsWindow();
            _handle = UiQuery.Read(() => window.Properties.NativeWindowHandle.ValueOrDefault, Wait.DefaultTimeout, "HWND окна");
            Title = window.SafeName();
            Id = window.SafeAutomationId();
            ClassName = window.SafeClassName();
        }

        /// <summary>Для наследников, оборачивающих уже найденное окно.</summary>
        protected RevitDialogPage(RevitDialogPage dialog)
            : this(Require(dialog).App, dialog._window)
        {
        }

        /// <summary>Выполнить действие и обернуть окно, которого до этого не было (сравнение по HWND).</summary>
        public static RevitDialogPage OpenedBy(RevitApp app, Action open, By selector = null, TimeSpan? timeout = null)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }
            return new RevitDialogPage(app, app.Windows.OpenedBy(open, selector, timeout));
        }

        /// <summary>Дождаться окна Revit с заголовком (Name); больше одного — неоднозначность.</summary>
        public static RevitDialogPage WaitFor(RevitApp app, string title, TimeSpan? timeout = null)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException("Нужен заголовок окна.", nameof(title));
            }
            AutomationElement window = Wait.ForStrict(() =>
            {
                // заголовок читается строго; окно, закрывшееся во время перечня, не совпадает (null), занятый Revit — AppBusyException
                AutomationElement[] matches = app.Windows.All().Where(w => UiRead.OrGone(() => w.Properties.Name.ValueOrDefault ?? "", null, Wait.DefaultTimeout, "заголовок окна Revit") == title).ToArray();
                if (matches.Length > 1)
                {
                    throw new AmbiguousElementException($"окно Revit «{title}»", matches.Select(m => m.Describe()).ToList());
                }
                return matches.FirstOrDefault();
            }, timeout ?? Wait.DefaultTimeout, $"окно Revit «{title}»", app.Windows.DescribeOnce);
            return new RevitDialogPage(app, window);
        }

        public Window Window => _window;

        public string Title { get; }

        public string Id { get; }

        public string ClassName { get; }

        public IntPtr Handle => _handle;

        /// <summary>Окно существует и видимо (по HWND; у элемента без HWND — по доступности элемента).</summary>
        public bool IsOpen => _handle != IntPtr.Zero ? Native.IsShown(_handle) : UiRead.OrGone(() =>
        {
            // чтение выбрасывает исключение у исчезнувшего элемента; значение не нужно
            _ = _window.Properties.ProcessId.Value;
            return true;
        }, false, ReadTimeout, "доступность " + Description);

        protected override AutomationElement ResolveRoot()
        {
            return IsOpen ? _window : null;
        }

        public void WaitClosed(TimeSpan? timeout = null)
        {
            Wait.Strict(() => !IsOpen, timeout ?? Wait.DefaultTimeout, "закрытие " + Description, null);
        }

        /// <summary>Закрыть окно (WindowPattern.Close) один раз, без повтора в занятом Revit, и дождаться закрытия.</summary>
        public void Close()
        {
            UiAct.RunOnce(_window, Description, () => _window.Close(), "закрыть");
            WaitClosed();
        }

        public void ClickAndWaitClosed(string automationId)
        {
            Click(automationId);
            WaitClosed();
        }

        public void ClickTextAndWaitClosed(string caption)
        {
            ClickText(caption);
            WaitClosed();
        }

        private static AutomationElement Require(AutomationElement window)
        {
            return window ?? throw new ArgumentNullException(nameof(window));
        }

        private static RevitDialogPage Require(RevitDialogPage dialog)
        {
            return dialog ?? throw new ArgumentNullException(nameof(dialog));
        }

        private static string Describe(AutomationElement window)
        {
            string name = window.SafeName();
            return name.Length > 0 ? name : window.SafeAutomationId();
        }
    }
}
