using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using NUnit.Framework;
using RevitUITesting.Pages;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Unit
{
    [TestFixture]
    public class RevitPanesTests
    {
        private UIA3Automation _automation;

        [SetUp]
        public void Prepare()
        {
            _automation = new UIA3Automation();
        }

        [TearDown]
        public void Cleanup()
        {
            _automation.Dispose();
        }

        private FakeNode Node(string id, ControlType type, string name = "")
        {
            return new FakeNode(_automation, id, type, name);
        }

        [Test]
        public void Innermost_frame_with_the_title_is_the_pane_root()
        {
            FakeNode content = Node("content", ControlType.Pane, "Поиск семейств").Add(Node("query", ControlType.Edit));
            FakeNode frame = Node("frame", ControlType.Window, "Поиск семейств").Add(Node("caption", ControlType.Text, "Поиск семейств"), content);
            FakeNode main = Node("main", ControlType.Window, "Autodesk Revit 2022.1").Add(Node("other", ControlType.Pane, "Properties"), frame);

            List<AutomationElement> found = RevitPanes.FindPanes(new[] { main.Element }, "Поиск семейств", 12);

            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Properties.AutomationId.Value, Is.EqualTo("content"));
        }

        [Test]
        public void Floating_pane_window_itself_is_found()
        {
            FakeNode floating = Node("float", ControlType.Window, "Properties").Add(Node("grid", ControlType.Table));

            List<AutomationElement> found = RevitPanes.FindPanes(new[] { Node("main", ControlType.Window, "Autodesk Revit 2022.1").Element, floating.Element }, "Properties", 12);

            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Properties.AutomationId.Value, Is.EqualTo("float"));
        }

        [Test]
        public void Text_with_the_title_is_not_a_pane()
        {
            FakeNode main = Node("main", ControlType.Window, "Autodesk Revit 2022.1").Add(Node("label", ControlType.Text, "Properties"));
            Assert.That(RevitPanes.FindPanes(new[] { main.Element }, "Properties", 12), Is.Empty);
        }

        [Test]
        public void Two_separate_panes_with_one_title_are_both_returned()
        {
            FakeNode main = Node("main", ControlType.Window, "R").Add(Node("a", ControlType.Pane, "Properties"), Node("b", ControlType.Pane, "Properties"));
            Assert.That(RevitPanes.FindPanes(new[] { main.Element }, "Properties", 12), Has.Count.EqualTo(2), "неоднозначность решает вызывающий");
        }

        [Test]
        public void Dialog_page_rejects_null_window_before_reading_it()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => new RevitDialogPage(null, (AutomationElement)null));
            Assert.That(ex.ParamName, Is.EqualTo("window"));
        }

        private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

        private static Exception Busy()
        {
            return new COMException("UIA_E_TIMEOUT", UiFault.UiaTimeout);
        }

        private static Exception Gone()
        {
            return new COMException("UIA_E_ELEMENTNOTAVAILABLE", UiFault.ElementNotAvailable);
        }

        [Test]
        public void Shown_pane_is_returned_when_not_offscreen()
        {
            FakeNode main = Node("main", ControlType.Window, "R").Add(Node("a", ControlType.Pane, "Properties"));
            AutomationElement pane = RevitPanes.ShownPane(new[] { main.Element }, "Properties", 12, Short);
            Assert.That(pane.Properties.AutomationId.Value, Is.EqualTo("a"));
        }

        [Test]
        public void Offscreen_pane_is_hidden()
        {
            FakeNode hidden = Node("a", ControlType.Pane, "Properties");
            hidden.FakeOffscreen = true;
            FakeNode main = Node("main", ControlType.Window, "R").Add(hidden);
            Assert.That(RevitPanes.ShownPane(new[] { main.Element }, "Properties", 12, Short), Is.Null);
        }

        [Test]
        public void Busy_offscreen_read_is_not_taken_for_a_hidden_pane()
        {
            FakeNode pane = Node("a", ControlType.Pane, "Properties");
            pane.OnOffscreen = n => Busy();
            FakeNode main = Node("main", ControlType.Window, "R").Add(pane);
            Assert.Throws<AppBusyException>(() => RevitPanes.ShownPane(new[] { main.Element }, "Properties", 12, Short));
        }

        [Test]
        public void Busy_offscreen_read_that_recovers_gives_the_pane()
        {
            FakeNode pane = Node("a", ControlType.Pane, "Properties");
            pane.OnOffscreen = n => n == 1 ? Busy() : null;
            FakeNode main = Node("main", ControlType.Window, "R").Add(pane);
            Assert.That(RevitPanes.ShownPane(new[] { main.Element }, "Properties", 12, TimeSpan.FromSeconds(5)), Is.Not.Null);
        }

        [Test]
        public void Pane_that_vanished_while_reading_offscreen_is_not_shown()
        {
            FakeNode pane = Node("a", ControlType.Pane, "Properties");
            pane.OnOffscreen = n => Gone();
            FakeNode main = Node("main", ControlType.Window, "R").Add(pane);
            Assert.That(RevitPanes.ShownPane(new[] { main.Element }, "Properties", 12, Short), Is.Null);
        }

        [Test]
        public void Root_window_that_vanished_is_skipped_without_raw_exception()
        {
            FakeNode closed = Node("closed", ControlType.Window, "Properties");
            closed.OnProperty = n => Gone();
            FakeNode main = Node("main", ControlType.Window, "R").Add(Node("a", ControlType.Pane, "Properties"));

            List<AutomationElement> found = RevitPanes.FindPanes(new[] { closed.Element, main.Element }, "Properties", 12, Short);

            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Properties.AutomationId.Value, Is.EqualTo("a"));
        }

        [Test]
        public void Busy_root_window_is_busy_not_missing()
        {
            FakeNode busy = Node("busy", ControlType.Window, "Properties");
            busy.OnProperty = n => Busy();
            Assert.Throws<AppBusyException>(() => RevitPanes.FindPanes(new[] { busy.Element }, "Properties", 12, Short));
        }
    }
}
