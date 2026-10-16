using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;
using RevitUITesting.Uia;
using UiaControlType = FlaUI.Core.Definitions.ControlType;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>
    /// Действия отказывают до ввода, если его нельзя выполнить честно: элемент недоступен, нет нужного паттерна, нет поставщика
    /// всплывающих окон. Сами нажатия проверяются контрактными тестами в живом приложении.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class ActionsTests
    {
        private UIA3Automation _automation;
        private TimeSpan _poll;
        private TimeSpan _default;
        private TimeSpan _uiBusy;
        private Func<IntPtr, TimeSpan, bool> _untilResponsive;

        [OneTimeSetUp]
        public void CreateAutomation()
        {
            _automation = new UIA3Automation();
        }

        [OneTimeTearDown]
        public void DisposeAutomation()
        {
            _automation.Dispose();
        }

        [SetUp]
        public void FastPolling()
        {
            _poll = Wait.PollInterval;
            _default = Wait.DefaultTimeout;
            Wait.PollInterval = TimeSpan.FromMilliseconds(10);
            Wait.DefaultTimeout = TimeSpan.FromMilliseconds(200);
            _uiBusy = Wait.UiBusyTimeout;
            Wait.UiBusyTimeout = TimeSpan.FromMilliseconds(300);
            _untilResponsive = UiAct.UntilResponsive;
        }

        [TearDown]
        public void RestorePolling()
        {
            Wait.PollInterval = _poll;
            Wait.DefaultTimeout = _default;
            Wait.UiBusyTimeout = _uiBusy;
            UiAct.UntilResponsive = _untilResponsive;
        }

        private AutomationElement Element(UiaControlType type = UiaControlType.Button)
        {
            return new FakeNode(_automation, "el", type, "Элемент").Element;
        }

        [Test]
        public void Press_on_disabled_element_fails_before_any_input()
        {
            // у FakeNode свойство IsEnabled не поддерживается — читается как false
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UiActions.Press(Element(), "кнопка «Загрузить»"));
            Assert.That(ex.Message, Does.Contain("Нельзя нажать кнопка «Загрузить»").And.Contain("недоступен"));
            Assert.Throws<ArgumentNullException>(() => UiActions.Press(null, "кнопка"));
        }

        [Test]
        public void SetValue_without_value_pattern_is_an_error()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UiActions.SetValue(Element(UiaControlType.Edit), "ванна", "поле поиска"));
            Assert.That(ex.Message, Does.Contain("поле поиска: нет ValuePattern"));
        }

        [Test]
        public void SetChecked_without_toggle_pattern_is_an_error()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UiActions.SetChecked(Element(UiaControlType.CheckBox), true, CheckInput.TogglePattern, "флажок"));
            Assert.That(ex.Message, Does.Contain("флажок: нет TogglePattern"));
        }

        [Test]
        public void Choose_requires_popups_provider_and_expandable_combo()
        {
            Func<IEnumerable<AutomationElement>> noPopups = () => new AutomationElement[0];
            Assert.Throws<ArgumentNullException>(() => UiActions.Choose(null, Element(UiaControlType.ComboBox), "Двери", "категория"));
            Assert.Throws<ArgumentNullException>(() => UiActions.ComboItems(null, Element(UiaControlType.ComboBox), "категория"));
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UiActions.Choose(noPopups, Element(UiaControlType.ComboBox), "Двери", "категория"));
            Assert.That(ex.Message, Does.Contain("категория: не раскрывается"));
        }

        [Test]
        public void Grid_without_root_or_grid_pattern_is_an_error()
        {
            UiGrid missing = new UiGrid(() => null, "таблица источников");
            ElementNotFoundException notFound = Assert.Throws<ElementNotFoundException>(() => _ = missing.Root);
            Assert.That(notFound.Scope, Is.EqualTo("таблица источников"));
            UiGrid plain = new UiGrid(() => Element(UiaControlType.DataGrid), "таблица источников");
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => _ = plain.RowCount);
            Assert.That(ex.Message, Does.Contain("не поддерживает GridPattern"));
            Assert.Throws<ArgumentNullException>(() => new UiGrid(null, "таблица"));
        }

        [Test]
        public void SetValue_applied_value_is_verified_by_a_strict_read()
        {
            FakeNode node = new FakeNode(_automation, "ed", UiaControlType.Edit, "Поле") { SupportsValue = true };
            UiActions.SetValue(node.Element, "ванна", "поле поиска");
            Assert.That(node.CurrentValue, Is.EqualTo("ванна"));
        }

        [Test]
        public void SetValue_with_busy_app_during_verification_is_app_busy_not_a_mismatch()
        {
            FakeNode node = new FakeNode(_automation, "ed", UiaControlType.Edit, "Поле") { SupportsValue = true };
            node.OnValueRead = n => new COMException("UIA_E_TIMEOUT", UiFault.UiaTimeout);
            AppBusyException ex = Assert.Throws<AppBusyException>(() => UiActions.SetValue(node.Element, "ванна", "поле поиска"));
            Assert.That(ex.Message, Does.Contain("поле поиска"));
        }

        [Test]
        public void SetValue_with_different_value_in_element_is_value_not_applied()
        {
            FakeNode node = new FakeNode(_automation, "ed", UiaControlType.Edit, "Поле") { SupportsValue = true };
            node.OnValueRead = n => { node.CurrentValue = "старое"; return null; };
            Assert.Throws<ValueNotAppliedException>(() => UiActions.SetValue(node.Element, "ванна", "поле поиска"));
        }

        private static COMException Busy()
        {
            return new COMException("UIA_E_TIMEOUT", UiFault.UiaTimeout);
        }

        [Test]
        public void Action_busy_and_app_never_answers_is_app_busy_not_raw_timeout()
        {
            // свойства элемента всё время отвечают «занят»: приложение так и не ожило за UiBusyTimeout
            FakeNode node = new FakeNode(_automation, "btn", UiaControlType.Button, "Кнопка") { OnProperty = n => Busy() };
            AppBusyException ex = Assert.Throws<AppBusyException>(() => UiAct.RunOnce(node.Element, "кнопка «Загрузить»", () => throw Busy(), "Invoke"));
            Assert.That(ex.Message, Does.Contain("кнопка «Загрузить»"));
        }

        [Test]
        public void Action_busy_then_app_answers_returns_without_repeating_the_action()
        {
            FakeNode node = new FakeNode(_automation, "btn", UiaControlType.Button, "Кнопка") { FakeHandle = (IntPtr)1234, OnProperty = n => n <= 2 ? Busy() : null };
            IntPtr checkedHandle = IntPtr.Zero;
            UiAct.UntilResponsive = (handle, timeout) =>
            {
                checkedHandle = handle;
                return true;
            };
            int calls = 0;
            UiAct.RunOnce(node.Element, "кнопка «Загрузить»", () =>
            {
                calls++;
                throw Busy();
            }, "Invoke");
            Assert.That(calls, Is.EqualTo(1), "действие не повторяется");
            Assert.That(checkedHandle, Is.EqualTo((IntPtr)1234), "отклик проверяется по дескриптору окна элемента");
        }

        [Test]
        public void Action_busy_and_window_stays_unresponsive_is_app_busy()
        {
            FakeNode node = new FakeNode(_automation, "btn", UiaControlType.Button, "Кнопка") { FakeHandle = (IntPtr)1234 };
            UiAct.UntilResponsive = (handle, timeout) => false;
            AppBusyException ex = Assert.Throws<AppBusyException>(() => UiAct.RunOnce(node.Element, "кнопка «Загрузить»", () => throw Busy(), "Invoke"));
            Assert.That(ex.Message, Does.Contain("кнопка «Загрузить»"));
        }

        [Test]
        public void SetValue_busy_during_the_action_is_not_repeated_and_is_verified_by_reading_back()
        {
            FakeNode node = new FakeNode(_automation, "ed", UiaControlType.Edit, "Поле") { SupportsValue = true, FakeHandle = (IntPtr)1234 };
            node.OnSetValue = n => Busy();
            UiAct.UntilResponsive = (handle, timeout) => true;
            ValueNotAppliedException ex = Assert.Throws<ValueNotAppliedException>(() => UiActions.SetValue(node.Element, "ванна", "поле поиска"));
            Assert.That(node.SetValueCalls, Is.EqualTo(1), "запись не повторяется");
            Assert.That(ex.Actual, Is.EqualTo(""));
        }

        [Test]
        public void SetValue_busy_during_the_action_and_app_never_answers_is_app_busy()
        {
            FakeNode node = new FakeNode(_automation, "ed", UiaControlType.Edit, "Поле") { SupportsValue = true };
            node.OnSetValue = n =>
            {
                node.OnProperty = m => Busy();
                return Busy();
            };
            Assert.Throws<AppBusyException>(() => UiActions.SetValue(node.Element, "ванна", "поле поиска"));
            Assert.That(node.SetValueCalls, Is.EqualTo(1));
        }

        [Test]
        public void Chosen_value_in_combo_is_verified()
        {
            FakeNode combo = new FakeNode(_automation, "cb", UiaControlType.ComboBox, "Список") { SupportsValue = true, CurrentValue = "Двери" };
            Assert.DoesNotThrow(() => UiChoice.VerifyChosen(combo.Element, "Двери", "категория"));
            ValueNotAppliedException ex = Assert.Throws<ValueNotAppliedException>(() => UiChoice.VerifyChosen(combo.Element, "Окна", "категория"));
            Assert.That(ex.Actual, Is.EqualTo("Двери"));
        }

        [Test]
        public void Combo_without_value_pattern_is_not_verified()
        {
            FakeNode combo = new FakeNode(_automation, "cb", UiaControlType.ComboBox, "Список");
            Assert.DoesNotThrow(() => UiChoice.VerifyChosen(combo.Element, "Двери", "категория"));
        }

        [Test]
        public void Chosen_value_with_busy_app_is_app_busy_not_a_mismatch_with_empty()
        {
            FakeNode combo = new FakeNode(_automation, "cb", UiaControlType.ComboBox, "Список") { SupportsValue = true, OnValueRead = n => Busy() };
            AppBusyException ex = Assert.Throws<AppBusyException>(() => UiChoice.VerifyChosen(combo.Element, "Двери", "категория"));
            Assert.That(ex.Message, Does.Contain("категория"));
        }

        [TestCase(true, 168, true)]
        [TestCase(false, 96, true)]
        [TestCase(false, 168, false)]
        public void Mouse_is_allowed_only_when_coordinates_match(bool perMonitor, int dpi, bool expected)
        {
            DpiStatus status = new DpiStatus(perMonitor, dpi, "тест");
            Assert.That(status.MouseCoordinatesMatch, Is.EqualTo(expected));
            Assert.That(status.ToString(), Does.Contain("DPI " + dpi));
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out System.Drawing.Point point);

        /// <summary>Курсор переносится только в окно процесса приложения: нет окна или окно чужого процесса — курсор на месте, false.</summary>
        [Test]
        public void Mouse_is_not_parked_in_a_window_of_another_process()
        {
            int self = System.Diagnostics.Process.GetCurrentProcess().Id;
            Assert.That(GetCursorPos(out System.Drawing.Point before), Is.True);

            Assert.That(UiAct.ParkMouse(IntPtr.Zero, self, "тест"), Is.False);
            Assert.That(UiAct.ParkMouse(GetDesktopWindow(), self, "тест"), Is.False, "окно рабочего стола не принадлежит процессу теста");

            Assert.That(GetCursorPos(out System.Drawing.Point after), Is.True);
            Assert.That(after, Is.EqualTo(before));
        }
    }
}
