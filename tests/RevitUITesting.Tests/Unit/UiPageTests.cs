using System;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;
using RevitUITesting.Pages;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Unit
{
    [TestFixture]
    public class UiPageTests
    {
        private UIA3Automation _automation;
        private TimeSpan _poll;
        private TimeSpan _default;

        [SetUp]
        public void Prepare()
        {
            _automation = new UIA3Automation();
            _poll = Wait.PollInterval;
            _default = Wait.DefaultTimeout;
            Wait.PollInterval = TimeSpan.FromMilliseconds(10);
            Wait.DefaultTimeout = TimeSpan.FromMilliseconds(100);
        }

        [TearDown]
        public void Cleanup()
        {
            Wait.PollInterval = _poll;
            Wait.DefaultTimeout = _default;
            _automation.Dispose();
        }

        /// <summary>Страница без Revit: корень задаёт тест; RevitApp не используется страницей, поэтому достаточно неинициализированного экземпляра.</summary>
        private sealed class ProbePage : UiPage
        {
            private readonly Func<AutomationElement> _root;

            public ProbePage(Func<AutomationElement> root)
                : base((RevitApp)FormatterServices.GetUninitializedObject(typeof(RevitApp)), "проверочная область")
            {
                _root = root;
            }

            public TimeSpan? LastReadTimeout { get; private set; }

            public TimeSpan CurrentReadTimeout => ReadTimeout;

            protected override AutomationElement ResolveRoot()
            {
                LastReadTimeout = ReadTimeout;
                return _root();
            }
        }

        private static Exception Busy()
        {
            return new COMException("UIA_E_TIMEOUT", UiFault.UiaTimeout);
        }

        [Test]
        public void Busy_tree_does_not_let_AssertAbsent_pass()
        {
            FakeNode child = new FakeNode(_automation, "x");
            child.OnProperty = n => Busy();
            FakeNode root = new FakeNode(_automation, "root").Add(child);
            ProbePage page = new ProbePage(() => root.Element);

            Assert.Throws<AppBusyException>(() => page.AssertAbsent(By.Id("missing"), TimeSpan.FromMilliseconds(20)));
        }

        [Test]
        public void AssertAbsent_passes_on_complete_tree_without_the_element()
        {
            FakeNode root = new FakeNode(_automation, "root").Add(new FakeNode(_automation, "x"));
            ProbePage page = new ProbePage(() => root.Element);

            Assert.DoesNotThrow(() => page.AssertAbsent(By.Id("missing"), TimeSpan.FromMilliseconds(20)));
        }

        [Test]
        public void Explicit_timeout_reaches_ResolveRoot_and_is_restored()
        {
            ProbePage page = new ProbePage(() => null);

            Assert.Catch<UiWaitTimeoutException>(() => page.Find("x", TimeSpan.FromMilliseconds(40)));

            Assert.That(page.LastReadTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(40)));
            Assert.That(page.CurrentReadTimeout, Is.EqualTo(Wait.DefaultTimeout), "таймаут вызова не должен оставаться после возврата");
        }
    }
}
