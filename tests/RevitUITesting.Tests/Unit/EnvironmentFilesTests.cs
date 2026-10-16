using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using NUnit.Framework;
using RevitUITesting.Revit;

namespace RevitUITesting.Tests.Unit
{
    [TestFixture]
    [NonParallelizable]
    public class EnvironmentFilesTests
    {
        private string _dir;
        private string _registryKey;

        [SetUp]
        public void Prepare()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RevitUITesting.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _registryKey = @"Software\RevitUITesting.Tests\" + Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void Cleanup()
        {
            Directory.Delete(_dir, true);
            Registry.CurrentUser.DeleteSubKeyTree(_registryKey, false);
        }

        [Test]
        public void Session_file_round_trip()
        {
            string path = Path.Combine(_dir, "session.json");
            DateTime started = new DateTime(2026, 10, 7, 20, 1, 2, 345, DateTimeKind.Utc);
            new RevitSessionFile(4242, "RevitTestHost-abc", "2022", started).Write(path);

            RevitSessionFile read = RevitSessionFile.Read(path);

            Assert.That(read.Pid, Is.EqualTo(4242));
            Assert.That(read.Pipe, Is.EqualTo("RevitTestHost-abc"));
            Assert.That(read.RevitVersion, Is.EqualTo("2022"));
            Assert.That(read.StartedUtc, Is.EqualTo(started));
            Assert.That(read.StartedUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(read.Matches(4242, started.AddSeconds(1)), Is.True);
            Assert.That(read.Matches(4242, started.AddMinutes(5)), Is.False, "pid переиспользован другим процессом");
            Assert.That(read.Matches(4243, started), Is.False);
        }

        [Test]
        public void Missing_session_file_is_null_and_broken_is_explained()
        {
            Assert.That(RevitSessionFile.Read(Path.Combine(_dir, "none.json")), Is.Null);
            string broken = Path.Combine(_dir, "broken.json");
            File.WriteAllText(broken, "{\"pid\":");
            Assert.That(() => RevitSessionFile.Read(broken), Throws.TypeOf<InvalidDataException>().With.Message.Contains(broken));
            File.WriteAllText(broken, "{\"pid\":1}");
            Assert.That(() => RevitSessionFile.Read(broken), Throws.TypeOf<InvalidDataException>().With.Message.Contains("«pipe»"));
        }

        [Test]
        public void Session_file_is_deleted_only_for_its_pid()
        {
            string path = Path.Combine(_dir, "session.json");
            new RevitSessionFile(10, "p", "2022", DateTime.UtcNow).Write(path);

            RevitSessionFile.Delete(path, 11);
            Assert.That(File.Exists(path), Is.True);
            RevitSessionFile.Delete(path, 10);
            Assert.That(File.Exists(path), Is.False);
            Assert.DoesNotThrow(() => RevitSessionFile.Delete(path, 10));
        }

        [Test]
        public void Commit_check_passes_when_enough()
        {
            CommitCheck check = new CommitCheck(() => 6UL * 1024 * 1024 * 1024, () => throw new AssertionException("потребители не нужны"));
            Assert.DoesNotThrow(() => check.Require(5));
        }

        [Test]
        public void Commit_check_lists_top_consumers_when_short()
        {
            CommitCheck check = new CommitCheck(() => (ulong)(3.2 * 1024 * 1024 * 1024), () => new[]
            {
                new MemoryConsumer("vmmemWSL", 900, 15L * 1024 * 1024 * 1024),
                new MemoryConsumer("chrome", 901, 2L * 1024 * 1024 * 1024),
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => check.Require(5));

            Assert.That(ex.Message, Does.Contain("3.2").And.Contain("5").And.Contain("minFreeCommitGb"));
            Assert.That(ex.Message, Does.Contain("vmmemWSL (pid 900) 15.0 ГБ").And.Contain("chrome (pid 901) 2.0 ГБ"));
            Assert.That(ex.Message, Does.Contain("WSL"));
        }

        [Test]
        public void Commit_check_is_disabled_by_zero()
        {
            CommitCheck check = new CommitCheck(() => 0UL, () => new MemoryConsumer[0]);
            Assert.DoesNotThrow(() => check.Require(0));
        }

        [Test]
        public void Addin_trust_sets_dword_and_reports_previous_value()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_registryKey))
            {
                key.SetValue("770c23e2-3b3a-4ea3-89a2-d59899bed9a0", 0, RegistryValueKind.DWord);
            }

            IReadOnlyList<string> first = AddinTrust.ApplyAt(_registryKey, new[] { "{770C23E2-3B3A-4EA3-89A2-D59899BED9A0}", "3f2504e0-4f89-41d3-9a0c-0305e82c3301" });
            IReadOnlyList<string> second = AddinTrust.ApplyAt(_registryKey, new[] { "3f2504e0-4f89-41d3-9a0c-0305e82c3301" });

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_registryKey))
            {
                Assert.That(key.GetValue("770c23e2-3b3a-4ea3-89a2-d59899bed9a0"), Is.EqualTo(1));
                Assert.That(key.GetValueKind("770c23e2-3b3a-4ea3-89a2-d59899bed9a0"), Is.EqualTo(RegistryValueKind.DWord));
                Assert.That(key.GetValue("3f2504e0-4f89-41d3-9a0c-0305e82c3301"), Is.EqualTo(1));
            }
            Assert.That(first[0], Does.Contain("было: 0"));
            Assert.That(first[1], Does.Contain("было: нет"));
            Assert.That(second.Single(), Does.Contain("уже доверена"));
        }

        [Test]
        public void Addin_trust_rejects_non_guid()
        {
            Assert.That(() => AddinTrust.ApplyAt(_registryKey, new[] { "AcmeDev" }), Throws.ArgumentException.With.Message.Contains("«AcmeDev»"));
            Assert.That(AddinTrust.KeyPath("2022"), Is.EqualTo(@"Software\Autodesk\Revit\Autodesk Revit 2022\CodeSigning"));
        }

        [Test]
        public void Journal_tail_takes_newest_file_while_it_is_being_written()
        {
            File.WriteAllText(Path.Combine(_dir, "journal.0001.txt"), "старый\n", Encoding.Default);
            File.SetLastWriteTimeUtc(Path.Combine(_dir, "journal.0001.txt"), DateTime.UtcNow.AddHours(-1));
            string newest = Path.Combine(_dir, "journal.0002.txt");
            File.WriteAllText(newest, string.Join("\r\n", Enumerable.Range(1, 50).Select(i => "' line " + i)) + "\r\n", Encoding.Default);

            using (new FileStream(newest, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                string tail = RevitJournal.Tail(_dir, 3);

                Assert.That(RevitJournal.Latest(_dir), Is.EqualTo(newest));
                Assert.That(tail, Does.StartWith(newest));
                Assert.That(tail, Does.Contain("' line 50").And.Contain("' line 48").And.Not.Contain("' line 47"));
            }
        }

        [Test]
        public void Journal_tail_explains_missing_folder()
        {
            Assert.That(RevitJournal.Tail(Path.Combine(_dir, "nope"), 10), Does.Contain("нет"));
            Assert.That(RevitJournal.DirectoryFor("2022"), Does.EndWith(@"Autodesk\Revit\Autodesk Revit 2022\Journals"));
        }
    }
}
