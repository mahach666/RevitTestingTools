using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.UIA3;
using NUnit.Framework;
using RevitUITesting.NUnit;
using RevitUITesting.Uia;
using UiaControlType = FlaUI.Core.Definitions.ControlType;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>Каталог для сверки: безусловная константа, условная ([ShownWhen]) и отсутствующая на экране.</summary>
    public static class SampleIds
    {
        public const string Search = "searchBox";

        [ShownWhen("есть результаты поиска")]
        public const string Results = "resultsList";

        public const string Load = "loadButton";
    }

    [TestFixture]
    [NonParallelizable]
    public class NUnitHelpersTests
    {
        private string _dir;
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
        public void Prepare()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RevitUITesting.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _poll = Wait.PollInterval;
            _default = Wait.DefaultTimeout;
            Wait.PollInterval = TimeSpan.FromMilliseconds(10);
            Wait.DefaultTimeout = TimeSpan.FromMilliseconds(200);
        }

        [TearDown]
        public void Cleanup()
        {
            Wait.PollInterval = _poll;
            Wait.DefaultTimeout = _default;
            Directory.Delete(_dir, true);
        }

        private FakeNode Node(string id, UiaControlType type = UiaControlType.Pane, string name = "")
        {
            return new FakeNode(_automation, id, type, name);
        }

        [Test]
        public void Prune_removes_only_old_artifact_folders()
        {
            string[] artifacts = { "20260101_000000_000_A.x", "20260102_000000_000_B.y", "20260103_000000_000_C.z" };
            string[] foreign = { "20261007.1", "output" };
            foreach (string name in artifacts.Concat(foreign))
            {
                Directory.CreateDirectory(Path.Combine(_dir, name));
            }
            TestArtifacts.Prune(new RevitUiConfig { ArtifactsDir = _dir, ArtifactsKeep = 1 });
            Assert.That(Directory.GetDirectories(_dir).Select(Path.GetFileName), Is.EquivalentTo(new[] { "20260103_000000_000_C.z" }.Concat(foreign)));
        }

        [Test]
        public void Prune_with_non_positive_keep_removes_nothing()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "20260101_000000_000_A.x"));
            TestArtifacts.Prune(new RevitUiConfig { ArtifactsDir = _dir, ArtifactsKeep = 0 });
            Assert.That(Directory.GetDirectories(_dir), Has.Length.EqualTo(1));
        }

        [Test]
        public void Artifact_folder_is_named_by_stamp_and_test()
        {
            TestArtifacts artifacts = TestArtifacts.For(new RevitUiConfig { ArtifactsDir = _dir }, "Ns.SearchPaneTests", "Search(\"ванна\")", new DateTime(2026, 10, 7, 12, 30, 15, 42));
            Assert.That(Path.GetDirectoryName(artifacts.Folder), Is.EqualTo(_dir));
            Assert.That(Path.GetFileName(artifacts.Folder), Does.StartWith("20261007_123015_042_Ns.SearchPaneTests.Search(_ванна_)"));
            Assert.That(Directory.Exists(artifacts.Folder), Is.False, "папка создаётся при первом файле");
            string path = artifacts.WriteText("steps.log", "шаг", "Журнал шагов");
            Assert.That(File.ReadAllBytes(path).Take(3), Is.EqualTo(new UTF8Encoding(true).GetPreamble()));
        }

        [Test]
        public void Artifact_names_are_safe()
        {
            Assert.That(TestArtifacts.Sanitize("A.B(\"x\"/y)", 100), Does.Not.Contain("\"").And.Not.Contain("/"));
            Assert.That(TestArtifacts.Sanitize(new string('я', 200), 100), Has.Length.EqualTo(100));
        }

        [Test]
        public void Windows_dump_contains_every_window()
        {
            TestArtifacts artifacts = TestArtifacts.For(new RevitUiConfig { ArtifactsDir = _dir }, "C", "T", DateTime.Now);
            string path = artifacts.Windows(new[] { Node("main", UiaControlType.Window, "Autodesk Revit 2022").Element, Node("dlg", UiaControlType.Window, "Save changes?").Element });
            string text = File.ReadAllText(path);
            Assert.That(text, Does.Contain("Autodesk Revit 2022").And.Contain("Save changes?"));
            Assert.Throws<ArgumentNullException>(() => artifacts.Windows(null));
        }

        [Test]
        public void Catalog_check_separates_found_missing_conditional_and_unknown()
        {
            FakeNode pane = Node("pane").Add(Node("searchBox"), Node("filterCombo"));
            CatalogReport report = CatalogAssert.Check(pane.Element, typeof(SampleIds));
            Assert.That(report.Found, Is.EqualTo(new[] { "Search = searchBox" }));
            Assert.That(report.Skipped, Is.EqualTo(new[] { "Results = resultsList" }));
            Assert.That(report.Missing, Is.EqualTo(new[] { "Load = loadButton" }));
            Assert.That(report.Unknown, Is.EqualTo(new[] { "filterCombo", "pane" }));
            Assert.That(CatalogAssert.Check(pane.Element, typeof(SampleIds), "Load").Missing, Is.Empty, "игнор по имени константы");
            Assert.That(report.ToString(), Does.Contain("нет: Load = loadButton"));
        }

        [Test]
        public void Catalog_check_with_busy_app_fails_instead_of_reporting_missing()
        {
            FakeNode busy = Node("searchBox");
            busy.OnProperty = n => new COMException("UIA_E_TIMEOUT", UiFault.UiaTimeout);
            Assert.Throws<AppBusyException>(() => CatalogAssert.Check(Node("pane").Add(busy).Element, typeof(SampleIds)));
        }

        [Test]
        public void All_present_returns_report_when_nothing_is_missing()
        {
            FakeNode pane = Node("pane").Add(Node("searchBox"), Node("loadButton"));
            CatalogReport report = CatalogAssert.AllPresent(pane.Element, typeof(SampleIds));
            Assert.That(report.Missing, Is.Empty);
        }

        [Test]
        public void All_present_fails_with_missing_ids_listed()
        {
            FakeNode pane = Node("pane").Add(Node("searchBox"));
            AssertionException ex = Assert.Throws<AssertionException>(() => CatalogAssert.AllPresent(pane.Element, typeof(SampleIds)));
            Assert.That(ex.Message, Does.Contain("Load = loadButton"));
            Assert.That(ex.Message, Does.Not.Contain("Results = resultsList"), "условная константа не считается пропавшей");
            Assert.That(ex.Message, Does.Not.Contain("Search = searchBox"));
        }
    }
}
