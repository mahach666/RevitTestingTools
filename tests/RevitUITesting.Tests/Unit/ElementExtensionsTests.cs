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
    /// <summary>Строгое API поиска и чтения на поддельном дереве: «нет» — только после полного обхода, сбои не превращаются в пустой результат.</summary>
    [TestFixture]
    [NonParallelizable]
    public class ElementExtensionsTests
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

        private FakeNode Node(string id, UiaControlType type = UiaControlType.Pane, string name = "")
        {
            return new FakeNode(_automation, id, type, name);
        }

        private static Exception Busy()
        {
            return new COMException("UIA_E_TIMEOUT", UiFault.UiaTimeout);
        }

        [Test]
        public void Find_returns_nested_element()
        {
            FakeNode root = Node("root").Add(Node("a"), Node("b").Add(Node("target")));
            AutomationElement found = root.Element.Find(By.Id("target"));
            Assert.That(found.Properties.AutomationId.Value, Is.EqualTo("target"));
            Assert.That(root.Element.ById("target").Properties.AutomationId.Value, Is.EqualTo("target"));
        }

        [Test]
        public void Find_without_element_lists_ids_of_the_area()
        {
            FakeNode root = Node("root").Add(Node("searchBox"), Node("resultsList"));
            ElementNotFoundException ex = Assert.Throws<ElementNotFoundException>(() => root.Element.Find(By.Id("serchBox")));
            Assert.That(ex.IdsInScope, Is.EqualTo(new[] { "searchBox", "resultsList" }));
            Assert.That(ex.Message, Does.Contain("serchBox").And.Contain("searchBox"));
        }

        [Test]
        public void TryFind_is_null_only_after_complete_pass()
        {
            Assert.That(Node("root").Add(Node("a")).Element.TryFind(By.Id("x")), Is.Null);
            FakeNode busy = Node("a");
            busy.OnFindChildren = n => Busy();
            Assert.Throws<AppBusyException>(() => Node("root").Add(busy).Element.TryFind(By.Id("x")));
        }

        [Test]
        public void Texts_are_non_empty_names_of_text_nodes()
        {
            FakeNode root = Node("card").Add(
                Node("title", UiaControlType.Text, "Ванна 1700"),
                Node("blank", UiaControlType.Text, " "),
                Node("sizes").Add(Node("size", UiaControlType.Text, "1700x750")),
                Node("button", UiaControlType.Button, "Загрузить"));
            Assert.That(root.Element.Texts(), Is.EqualTo(new[] { "Ванна 1700", "1700x750" }));
        }

        [Test]
        public void Texts_with_busy_app_fail_instead_of_skipping_nodes()
        {
            FakeNode text = Node("title", UiaControlType.Text, "Ванна");
            text.OnProperty = n => Busy();
            Assert.Throws<AppBusyException>(() => Node("card").Add(text).Element.Texts());
        }

        [Test]
        public void Rows_prefer_direct_children_and_row_is_found_by_text_parts()
        {
            FakeNode list = Node("list", UiaControlType.List).Add(
                Node("r1", UiaControlType.ListItem, "item").Add(Node("t1", UiaControlType.Text, "Ванна акриловая")),
                Node("r2", UiaControlType.ListItem, "item").Add(Node("t2", UiaControlType.Text, "Дверь 900")));
            Assert.That(list.Element.Rows(), Has.Count.EqualTo(2));
            Assert.That(list.Element.TryRowContaining("ДВЕРЬ", "900").Properties.AutomationId.Value, Is.EqualTo("r2"));
            Assert.That(list.Element.TryRowContaining("Окно"), Is.Null);
            UiWaitTimeoutException ex = Assert.Throws<UiWaitTimeoutException>(() => list.Element.RowContaining("Окно"));
            Assert.That(ex.Description, Does.Contain("'Окно'"));
        }

        [Test]
        public void Safe_reads_replace_only_transient_faults()
        {
            FakeNode busy = Node("busy");
            busy.OnProperty = n => Busy();
            Assert.That(busy.Element.SafeName(), Is.EqualTo(""));
            Assert.That(busy.Element.SafeIsEnabled(), Is.False);
            FakeNode broken = Node("broken");
            broken.OnProperty = n => new InvalidOperationException("ошибка не UI Automation");
            Assert.Throws<InvalidOperationException>(() => broken.Element.SafeName());
        }

        [Test]
        public void Describe_and_dump_show_type_id_and_name()
        {
            FakeNode root = Node("pane", UiaControlType.Pane, "Поиск семейств").Add(Node("search", UiaControlType.Edit, "Поиск"));
            Assert.That(root.Element.Describe(), Is.EqualTo("[Pane] id=\"pane\" name=\"Поиск семейств\""));
            string dump = root.Element.DumpTree();
            Assert.That(dump, Does.Contain("[Pane] name=\"Поиск семейств\" id=\"pane\"").And.Contain("  [Edit] name=\"Поиск\" id=\"search\""));
            Assert.That(root.Element.DumpTree(5, true), Does.Contain("[Edit] id=\"search\" name=\"Поиск\""));
        }
    }
}
