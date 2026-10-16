using System;
using System.Linq;
using System.Runtime.InteropServices;
using FlaUI.UIA3;
using NUnit.Framework;
using RevitUITesting.Uia;
using UiaControlType = FlaUI.Core.Definitions.ControlType;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>
    /// Таблица на дереве-заглушке: колонки и ячейки читаются полным обходом. «Занят» — <see cref="AppBusyException"/>,
    /// «исчез» — таймаут ожидания; ни то ни другое не превращается в «колонки нет» и не даёт неполный список.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UiGridTests
    {
        private UIA3Automation _automation;
        private TimeSpan _poll;
        private TimeSpan _default;

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
        }

        [TearDown]
        public void RestorePolling()
        {
            Wait.PollInterval = _poll;
            Wait.DefaultTimeout = _default;
        }

        private static COMException Busy()
        {
            return new COMException("UIA_E_TIMEOUT", UiFault.UiaTimeout);
        }

        private static COMException Gone()
        {
            return new COMException("UIA_E_ELEMENTNOTAVAILABLE", UiFault.ElementNotAvailable);
        }

        private FakeNode HeaderItem(string field, string caption)
        {
            return new FakeNode(_automation, "", UiaControlType.HeaderItem, field).Add(new FakeNode(_automation, "", UiaControlType.Text, caption));
        }

        /// <summary>Таблица с заголовком из двух колонок: Name «Имя» и Count «Кол-во».</summary>
        private (UiGrid Grid, FakeNode Header, FakeNode First, FakeNode Second) Grid()
        {
            FakeNode first = HeaderItem("Name", "Имя");
            FakeNode second = HeaderItem("Count", "Кол-во");
            FakeNode header = new FakeNode(_automation, "hdr", UiaControlType.Header).Add(first, second);
            FakeNode root = new FakeNode(_automation, "grid", UiaControlType.DataGrid).Add(header);
            return (new UiGrid(() => root.Element, "таблица источников"), header, first, second);
        }

        [Test]
        public void Columns_are_read_with_field_names_and_captions()
        {
            var table = Grid();
            var columns = table.Grid.Columns();
            Assert.That(columns.Select(c => c.FieldName), Is.EqualTo(new[] { "Name", "Count" }));
            Assert.That(columns.Select(c => c.Caption), Is.EqualTo(new[] { "Имя", "Кол-во" }));
        }

        [Test]
        public void Columns_with_busy_header_is_app_busy_not_an_empty_list()
        {
            var table = Grid();
            table.Header.OnFindChildren = n => Busy();
            Assert.Throws<AppBusyException>(() => table.Grid.Columns());
        }

        [Test]
        public void Columns_with_busy_header_item_is_app_busy_not_a_dropped_column()
        {
            var table = Grid();
            table.Second.OnProperty = n => Busy();
            Assert.Throws<AppBusyException>(() => table.Grid.Columns());
        }

        [Test]
        public void Columns_with_busy_caption_is_app_busy_not_an_empty_caption()
        {
            var table = Grid();
            table.First.OnFindChildren = n => Busy();
            Assert.Throws<AppBusyException>(() => table.Grid.Columns());
        }

        [Test]
        public void Columns_with_header_item_that_keeps_vanishing_is_a_timeout_not_a_dropped_column()
        {
            var table = Grid();
            table.Second.OnProperty = n => Gone();
            UiWaitTimeoutException ex = Assert.Throws<UiWaitTimeoutException>(() => table.Grid.Columns());
            Assert.That(ex, Is.Not.InstanceOf<AppBusyException>());
        }

        [Test]
        public void Columns_retry_when_header_item_vanishes_between_the_walk_and_the_read()
        {
            var table = Grid();
            // чтения свойств узла: 1 — тип при обходе, 2 — Name при чтении колонки (здесь элемент исчезает), дальше всё читается
            table.First.OnProperty = n => n == 2 ? Gone() : null;
            Assert.That(table.Grid.Columns().Select(c => c.FieldName), Is.EqualTo(new[] { "Name", "Count" }));
        }

        private FakeNode Row(params FakeNode[] cells)
        {
            return new FakeNode(_automation, "row", UiaControlType.DataItem).Add(cells);
        }

        [Test]
        public void Cell_is_found_by_automation_id_in_the_row()
        {
            var table = Grid();
            FakeNode count = new FakeNode(_automation, "Count", UiaControlType.Text);
            FakeNode row = Row(new FakeNode(_automation, "Name", UiaControlType.Text), count);
            Assert.That(table.Grid.CellOf(row.Element, 3, "Count").Properties.AutomationId.Value, Is.EqualTo("Count"));
        }

        [Test]
        public void Cell_missing_in_a_complete_row_lists_the_cells()
        {
            var table = Grid();
            FakeNode row = Row(new FakeNode(_automation, "Name", UiaControlType.Text));
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => table.Grid.CellOf(row.Element, 3, "Count"));
            Assert.That(ex.Message, Does.Contain("в строке 3 нет ячейки Count").And.Contain("Ячейки: Name"));
        }

        [Test]
        public void Cell_with_busy_row_is_app_busy_not_a_missing_cell()
        {
            var table = Grid();
            FakeNode row = Row(new FakeNode(_automation, "Count", UiaControlType.Text));
            row.OnFindChildren = n => Busy();
            Assert.Throws<AppBusyException>(() => table.Grid.CellOf(row.Element, 3, "Count"));
        }

        [Test]
        public void Cell_with_busy_identity_read_is_app_busy_not_a_missing_cell()
        {
            var table = Grid();
            FakeNode row = Row(new FakeNode(_automation, "Count", UiaControlType.Text) { OnProperty = n => Busy() });
            Assert.Throws<AppBusyException>(() => table.Grid.CellOf(row.Element, 3, "Count"));
        }
    }
}
