using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>Обход дерева UiQuery при сбоях UI Automation: «занят» прерывает обход, «исчез» ниже корня — неполный обход, у корня — «области нет».</summary>
    [TestFixture]
    [NonParallelizable]
    public class UiQueryTests
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

        private FakeNode Node(string id)
        {
            return new FakeNode(_automation, id);
        }

        private static Exception Busy()
        {
            return new COMException("UIA_E_TIMEOUT", UiFault.UiaTimeout);
        }

        private static Exception Gone()
        {
            return new COMException("UIA_E_ELEMENTNOTAVAILABLE", UiFault.ElementNotAvailable);
        }

        [Test]
        public void Complete_search_finds_nested_element()
        {
            FakeNode target = Node("target");
            FakeNode root = Node("root").Add(Node("a").Add(Node("a1")), Node("b").Add(target));
            UiQueryResult result = UiQuery.Find(root.Element, By.Id("target"), limit: 1);
            Assert.That(result.Complete, Is.True);
            Assert.That(result.Matches.Single().Properties.AutomationId.Value, Is.EqualTo("target"));
        }

        [Test]
        public void Busy_fault_stops_the_pass_at_once()
        {
            FakeNode a = Node("a").Add(Node("a1"));
            FakeNode b = Node("b");
            FakeNode c = Node("c");
            a.OnProperty = n => Busy();
            FakeNode root = Node("root").Add(a, b, c);

            UiQueryResult result = UiQuery.Find(root.Element, By.Id("x"));

            Assert.That(result.Complete, Is.False);
            Assert.That(result.BusyFaults, Is.EqualTo(1));
            Assert.That(b.PropertyReads + c.PropertyReads, Is.Zero, "после «занят» зависшее приложение больше не опрашивается");
            Assert.That(a.FindChildrenCalls, Is.Zero);
        }

        [Test]
        public void Gone_below_root_makes_pass_incomplete()
        {
            FakeNode a = Node("a");
            a.OnFindChildren = n => Gone();
            FakeNode root = Node("root").Add(a);

            UiQueryResult result = UiQuery.Find(root.Element, By.Id("x"));

            Assert.That(result.Complete, Is.False, "строка пересоздана во время обхода — «точно нет» сказать нельзя");
            Assert.That(result.StaleFaults, Is.EqualTo(1));
            Assert.That(result.BusyFaults, Is.Zero);
        }

        [Test]
        public void Gone_child_property_makes_pass_incomplete()
        {
            FakeNode a = Node("a");
            a.OnProperty = n => Gone();
            UiQueryResult result = UiQuery.Find(Node("root").Add(a).Element, By.Id("x"));
            Assert.That(result.Complete, Is.False);
            Assert.That(result.StaleFaults, Is.EqualTo(1));
        }

        [Test]
        public void Gone_root_means_no_area_and_complete_pass()
        {
            FakeNode root = Node("root");
            root.OnFindChildren = n => Gone();
            UiQueryResult result = UiQuery.Find(root.Element, By.Id("x"));
            Assert.That(result.Complete, Is.True);
            Assert.That(result.Matches, Is.Empty);
        }

        [Test]
        public void TryFind_repeats_until_pass_is_complete()
        {
            FakeNode a = Node("a").Add(Node("target"));
            a.OnFindChildren = n => n == 1 ? Busy() : null;
            AutomationElement found = UiQuery.TryFind(Node("root").Add(a).Element, By.Id("target"), TimeSpan.FromSeconds(5));
            Assert.That(found, Is.Not.Null);
            Assert.That(a.FindChildrenCalls, Is.EqualTo(2));
        }

        [Test]
        public void TryFind_with_busy_app_gives_app_busy()
        {
            FakeNode a = Node("a");
            a.OnFindChildren = n => Busy();
            Assert.Throws<AppBusyException>(() => UiQuery.TryFind(Node("root").Add(a).Element, By.Id("x"), TimeSpan.FromMilliseconds(50)));
        }

        [Test]
        public void TryFind_with_only_vanishing_nodes_is_timeout_not_app_busy()
        {
            FakeNode a = Node("a");
            a.OnFindChildren = n => Gone();
            UiWaitTimeoutException ex = Assert.Throws<UiWaitTimeoutException>(() => UiQuery.TryFind(Node("root").Add(a).Element, By.Id("x"), TimeSpan.FromMilliseconds(50)));
            Assert.That(ex, Is.Not.InstanceOf<AppBusyException>());
            Assert.That(ex.Message, Does.Contain("менялось"));
        }

        [Test]
        public void WaitFind_without_any_answer_about_area_is_app_busy()
        {
            Assert.Throws<AppBusyException>(() => UiQuery.WaitFind(() => throw Busy(), By.Id("x"), TimeSpan.FromMilliseconds(50), "область"));
        }

        [Test]
        public void WaitFind_without_area_is_not_found()
        {
            ElementNotFoundException ex = Assert.Throws<ElementNotFoundException>(() => UiQuery.WaitFind(() => null, By.Id("x"), TimeSpan.FromMilliseconds(50), "область"));
            Assert.That(ex.Selector, Does.Contain("область поиска"));
        }

        [Test]
        public void IdsIn_stops_after_limit_nodes()
        {
            FakeNode root = Node("root");
            for (int i = 0; i < 10; i++)
            {
                root.Add(Node("n" + i));
            }
            Assert.That(UiQuery.IdsIn(root.Element, 30, 3), Is.EqualTo(new[] { "n0", "n1", "n2" }));
            Assert.That(root.Kids.Skip(3).Sum(k => k.PropertyReads), Is.Zero);
        }

        [Test]
        public void IdsIn_propagates_non_transient_exceptions()
        {
            FakeNode broken = Node("broken");
            broken.OnProperty = n => new InvalidOperationException("ошибка не UI Automation");
            Assert.Throws<InvalidOperationException>(() => UiQuery.IdsIn(Node("root").Add(broken).Element));
        }

        [Test]
        public void IdsIn_marks_incomplete_walk_when_app_is_busy()
        {
            FakeNode busy = Node("busy");
            busy.OnProperty = n => Busy();
            IReadOnlyList<string> ids = UiQuery.IdsIn(Node("root").Add(Node("a"), busy).Element);
            Assert.That(ids, Is.EqualTo(new[] { "a", "(обход неполный: занят 1, исчезло 0)" }));
        }

        [Test]
        public void IdsInComplete_repeats_after_busy_and_returns_all_ids()
        {
            FakeNode b = Node("b");
            b.OnProperty = n => n == 1 ? Busy() : null;
            FakeNode root = Node("root").Add(Node("a"), b, Node("c"));
            Assert.That(UiQuery.IdsInComplete(root.Element, TimeSpan.FromSeconds(5), 5), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void IdsInComplete_with_busy_app_gives_app_busy()
        {
            FakeNode b = Node("b");
            b.OnProperty = n => Busy();
            Assert.Throws<AppBusyException>(() => UiQuery.IdsInComplete(Node("root").Add(b).Element, TimeSpan.FromMilliseconds(50), 5));
        }
    }
}
