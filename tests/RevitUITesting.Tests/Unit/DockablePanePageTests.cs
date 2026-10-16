using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
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
    [NonParallelizable]
    public class DockablePanePageTests
    {
        private UIA3Automation _automation;
        private TimeSpan _poll;
        private TimeSpan _default;
        private FakeNode _main;
        private FakeNode _pane;
        private int _searches;
        private bool _windowVisible;
        private int _visibilityChecks;

        [SetUp]
        public void Prepare()
        {
            _automation = new UIA3Automation();
            _poll = Wait.PollInterval;
            _default = Wait.DefaultTimeout;
            Wait.PollInterval = TimeSpan.FromMilliseconds(10);
            Wait.DefaultTimeout = TimeSpan.FromMilliseconds(100);
            _pane = Node("a", ControlType.Pane, "Поиск семейств").Add(Node("query", ControlType.Edit));
            _pane.FakeHandle = new IntPtr(0x1001);
            _main = Node("main", ControlType.Window, "Autodesk Revit 2022.1").Add(Node("other", ControlType.Pane, "Properties"), _pane);
            _searches = 0;
            _windowVisible = true;
            _visibilityChecks = 0;
        }

        [TearDown]
        public void Cleanup()
        {
            Wait.PollInterval = _poll;
            Wait.DefaultTimeout = _default;
            _automation.Dispose();
        }

        private FakeNode Node(string id, ControlType type, string name = "")
        {
            return new FakeNode(_automation, id, type, name);
        }

        /// <summary>
        /// Страница без Revit: полный поиск — тот же, что у RevitPanes (ShownPane по главному окну), с подсчётом вызовов; видимость окна рамки —
        /// поле _windowVisible вместо IsWindowVisible.
        /// </summary>
        private DockablePanePage Page(AutomationElement found = null)
        {
            RevitApp app = (RevitApp)FormatterServices.GetUninitializedObject(typeof(RevitApp));
            return new DockablePanePage(app, "Поиск семейств", found, timeout =>
            {
                _searches++;
                return RevitPanes.ShownPane(new[] { _main.Element }, "Поиск семейств", 12, timeout);
            }, handle =>
            {
                _visibilityChecks++;
                return handle != IntPtr.Zero && _windowVisible;
            });
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
        public void Second_access_checks_the_remembered_pane_without_walking_the_tree()
        {
            DockablePanePage page = Page();
            Assert.That(page.Root.Properties.AutomationId.Value, Is.EqualTo("a"));
            int walks = _main.FindChildrenCalls;
            int reads = _pane.OffscreenReads;

            Assert.That(page.Root.Properties.AutomationId.Value, Is.EqualTo("a"));
            Assert.That(page.IsShown, Is.True);
            Assert.That(page.Has("query"), Is.True);

            Assert.That(_searches, Is.EqualTo(1), "полный поиск — только при первом обращении");
            Assert.That(_main.FindChildrenCalls, Is.EqualTo(walks), "главное окно больше не обходится");
            Assert.That(_pane.OffscreenReads, Is.EqualTo(reads + 3), "одно чтение IsOffscreen на обращение");
        }

        [Test]
        public void Vanished_pane_is_searched_again_exactly_once()
        {
            DockablePanePage page = Page();
            Assert.That(page.IsShown, Is.True);
            FakeNode recreated = Node("a2", ControlType.Pane, "Поиск семейств");
            recreated.FakeHandle = new IntPtr(0x1002);
            _pane.OnOffscreen = n => Gone();
            _main.Kids.Remove(_pane);
            _main.Kids.Add(recreated);

            Assert.That(page.Root.Properties.AutomationId.Value, Is.EqualTo("a2"));
            Assert.That(_searches, Is.EqualTo(2));

            Assert.That(page.Root.Properties.AutomationId.Value, Is.EqualTo("a2"));
            Assert.That(_searches, Is.EqualTo(2), "пересозданная панель запоминается заново");
        }

        [Test]
        public void Offscreen_pane_is_searched_again_and_hidden_pane_is_not_shown()
        {
            DockablePanePage page = Page();
            Assert.That(page.IsShown, Is.True);
            _pane.FakeOffscreen = true;

            Assert.That(page.IsShown, Is.False);
            Assert.That(_searches, Is.EqualTo(2));
        }

        /// <summary>
        /// Живой прогон (Evidence/closing-run-2022.txt): скрытая панель Properties исчезла из дерева, а её запомненная рамка — окно MFC — на
        /// чтение IsOffscreen отвечала false. Решает видимость окна рамки: окно скрыто — полный поиск, панели в дереве нет — не показана.
        /// </summary>
        [Test]
        public void Remembered_pane_with_hidden_window_is_searched_again_and_is_not_shown()
        {
            DockablePanePage page = Page();
            Assert.That(page.IsShown, Is.True);
            int reads = _pane.OffscreenReads;
            _windowVisible = false;
            _main.Kids.Remove(_pane);

            Assert.That(page.IsShown, Is.False, "окно рамки скрыто, хотя IsOffscreen = false");
            Assert.That(_searches, Is.EqualTo(2));
            Assert.That(_pane.OffscreenReads, Is.EqualTo(reads), "IsOffscreen скрытого окна не читается: решает видимость окна");
        }

        [Test]
        public void Shown_again_pane_is_remembered_anew_after_hidden_window()
        {
            DockablePanePage page = Page();
            Assert.That(page.IsShown, Is.True);
            _windowVisible = false;
            _main.Kids.Remove(_pane);
            Assert.That(page.IsShown, Is.False);

            FakeNode shown = Node("a2", ControlType.Pane, "Поиск семейств");
            shown.FakeHandle = new IntPtr(0x1002);
            _main.Kids.Add(shown);
            _windowVisible = true;

            Assert.That(page.Root.Properties.AutomationId.Value, Is.EqualTo("a2"));
            Assert.That(page.IsShown, Is.True);
            Assert.That(_searches, Is.EqualTo(3), "поиск после скрытия (панели нет), поиск после показа, затем снова показанная рамка запомнена");
        }

        [Test]
        public void Pane_without_window_handle_is_searched_on_every_access()
        {
            _pane.FakeHandle = IntPtr.Zero;
            DockablePanePage page = Page();

            Assert.That(page.IsShown, Is.True);
            Assert.That(page.IsShown, Is.True);

            Assert.That(_searches, Is.EqualTo(2), "рамку без окна нельзя проверить видимостью окна — только полным поиском");
        }

        [Test]
        public void Remembered_pane_with_visible_window_and_offscreen_is_searched_again()
        {
            DockablePanePage page = Page();
            Assert.That(page.IsShown, Is.True);
            int checks = _visibilityChecks;
            _pane.FakeOffscreen = true;

            Assert.That(page.IsShown, Is.False);
            Assert.That(_visibilityChecks, Is.GreaterThan(checks), "сначала видимость окна, затем IsOffscreen");
        }

        [Test]
        public void Busy_remembered_pane_is_AppBusyException_not_a_new_search()
        {
            DockablePanePage page = Page();
            Assert.That(page.IsShown, Is.True);
            _pane.OnOffscreen = n => Busy();

            Assert.Throws<AppBusyException>(() => _ = page.IsShown);
            Assert.That(_searches, Is.EqualTo(1));

            _pane.OnOffscreen = null;
            Assert.That(page.IsShown, Is.True, "после занятости запомненная панель та же");
            Assert.That(_searches, Is.EqualTo(1));
        }

        [Test]
        public void Pane_found_by_RevitPanes_is_not_searched_again()
        {
            DockablePanePage page = Page(_pane.Element);

            Assert.That(page.Root.Properties.AutomationId.Value, Is.EqualTo("a"));
            Assert.That(_searches, Is.EqualTo(0));
            Assert.That(_main.FindChildrenCalls, Is.EqualTo(0));
        }

        [Test]
        public void Pane_titles_for_diagnostics_take_one_bounded_pass()
        {
            FakeNode main = Node("main", ControlType.Window, "R");
            for (int i = 1; i <= 5; i++)
            {
                main.Add(Node("p" + i, ControlType.Pane, "Панель " + i));
            }

            string titles = RevitPanes.DescribeTitles(new[] { main.Element }, 12, 3);

            Assert.That(titles, Does.Contain("Панель 1").And.Contain("Панель 3"));
            Assert.That(titles, Does.Not.Contain("Панель 4"));
            Assert.That(titles, Does.Contain("первые 3"));
        }

        [Test]
        public void Pane_titles_for_diagnostics_do_not_wait_for_busy_Revit()
        {
            FakeNode busy = Node("busy", ControlType.Pane, "Properties");
            busy.OnFindChildren = n => Busy();
            FakeNode main = Node("main", ControlType.Window, "R").Add(busy);

            string titles = RevitPanes.DescribeTitles(new[] { main.Element }, 12, 40);

            Assert.That(titles, Does.Contain("Properties").And.Contain("неполный"));
            Assert.That(busy.FindChildrenCalls, Is.EqualTo(1), "без повторов обхода");
        }

        [Test]
        public void Pane_titles_list_floating_pane_root_and_dedupe()
        {
            FakeNode floating = Node("float", ControlType.Window, "Properties");
            FakeNode main = Node("main", ControlType.Window, "R").Add(Node("p", ControlType.Pane, "Properties"), Node("t", ControlType.Text, "Текст"));

            string titles = RevitPanes.DescribeTitles(new[] { main.Element, floating.Element }, 12, 40);

            Assert.That(titles, Is.EqualTo("Properties"));
            Assert.That(titles.Split(',').Count(), Is.EqualTo(1));
        }
    }
}
