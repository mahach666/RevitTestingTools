using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>Журналы плагинов (LogWatch) и снимок локальных файлов (LocalFileBackup).</summary>
    [TestFixture]
    [NonParallelizable]
    public class FileAndLogTests
    {
        private string _dir;

        [SetUp]
        public void TempDir()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RevitUITesting.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void Cleanup()
        {
            Directory.Delete(_dir, true);
        }

        [TestCase("log")]
        [TestCase("")]
        [TestCase(null)]
        public void Log_extension_must_start_with_dot(string extension)
        {
            Assert.That(() => new LogWatch(_dir, new[] { "A" }, extension, new System.Text.RegularExpressions.Regex("(?<level>X)")), Throws.ArgumentException);
        }

        [Test]
        public void Log_level_pattern_needs_level_group_and_is_required()
        {
            Assert.That(() => new LogWatch(_dir, new[] { "A" }, ".txt", new System.Text.RegularExpressions.Regex("X")), Throws.ArgumentException);
            Assert.That(() => new LogWatch(_dir, new[] { "A" }, ".txt", null), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Log_returns_only_complete_new_lines_with_level()
        {
            string log = Path.Combine(_dir, "PluginBaseLog.log");
            File.WriteAllText(log, "2026-10-07 00:10:00,000|[1]|INFO |[Menu]|Plugin version: 1.3.0.0\n", Encoding.UTF8);
            LogWatch watch = new LogWatch(_dir, new[] { "PluginBaseLog" });
            watch.Mark();
            File.AppendAllText(log, "2026-10-07 00:11:00,000|[7]|ERROR|[Search]|сбой загрузки\nпродолжение сообщения\n2026-10-07 00:11:01,000|[7]|WARN |[Search]|недописано", Encoding.UTF8);
            IReadOnlyList<LogLine> lines = watch.NewLines();
            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[0].Level, Is.EqualTo("ERROR"));
            Assert.That(lines[0].Text, Does.Contain("сбой загрузки"));
            Assert.That(lines[0].Log, Is.EqualTo("PluginBaseLog"));
            Assert.That(lines[1].Level, Is.EqualTo(""), "строка продолжения без формата log4net");
            Assert.That(watch.LastPluginVersion("PluginBaseLog"), Is.EqualTo("1.3.0.0"));
        }

        [Test]
        public void Log_shorter_than_mark_is_read_from_start()
        {
            string log = Path.Combine(_dir, "Plugin.log");
            File.WriteAllText(log, "старая длинная строка журнала до ротации\n", Encoding.UTF8);
            LogWatch watch = new LogWatch(_dir, new[] { "Plugin" });
            watch.Mark();
            File.WriteAllText(log, "новая\n", Encoding.UTF8);
            Assert.That(watch.NewLines(), Has.Count.EqualTo(1));
        }

        [Test]
        public void Rotation_is_warned_once_not_on_every_read()
        {
            string log = Path.Combine(_dir, "Plugin.log");
            File.WriteAllText(log, "старая длинная строка журнала до ротации\n", Encoding.UTF8);
            LogWatch watch = new LogWatch(_dir, new[] { "Plugin" });
            watch.Mark();
            File.WriteAllText(log, "новая\n", Encoding.UTF8);
            List<string> written = new List<string>();
            Action<string> sink = UiLog.Sink;
            UiLog.Sink = written.Add;
            try
            {
                Assert.That(watch.NewLines(), Has.Count.EqualTo(1));
                File.AppendAllText(log, "ещё одна\n", Encoding.UTF8);
                Assert.That(watch.NewLines(), Has.Count.EqualTo(2), "после ротации читается с начала нового файла");
                Assert.That(watch.NewLines(), Has.Count.EqualTo(2));
            }
            finally
            {
                UiLog.Sink = sink;
            }
            Assert.That(written.Count(l => l.Contains("стал короче метки")), Is.EqualTo(1), "предупреждение — раз на ротацию, а не на каждый опрос");
        }

        [Test]
        public void Second_rotation_is_warned_again()
        {
            string log = Path.Combine(_dir, "Plugin.log");
            File.WriteAllText(log, "первая очень длинная строка журнала до ротации\n", Encoding.UTF8);
            LogWatch watch = new LogWatch(_dir, new[] { "Plugin" });
            watch.Mark();
            List<string> written = new List<string>();
            Action<string> sink = UiLog.Sink;
            UiLog.Sink = written.Add;
            try
            {
                File.WriteAllText(log, "вторая\n", Encoding.UTF8);
                watch.NewLines();
                watch.Mark();
                File.WriteAllText(log, "третья очень длинная строка журнала после второго Mark\n", Encoding.UTF8);
                watch.Mark();
                File.WriteAllText(log, "4\n", Encoding.UTF8);
                watch.NewLines();
            }
            finally
            {
                UiLog.Sink = sink;
            }
            Assert.That(written.Count(l => l.Contains("стал короче метки")), Is.EqualTo(2));
        }

        [Test]
        public void Missing_log_has_no_lines_and_wait_reports_what_was_read()
        {
            TimeSpan poll = Wait.PollInterval;
            Wait.PollInterval = TimeSpan.FromMilliseconds(10);
            try
            {
                LogWatch watch = new LogWatch(_dir, new[] { "Absent" });
                watch.Mark();
                Assert.That(watch.NewLines(), Is.Empty);
                Assert.That(watch.LastPluginVersion("Absent"), Is.Null);
                UiWaitTimeoutException ex = Assert.Throws<UiWaitTimeoutException>(() => watch.WaitLine(l => l.Text.Contains("Анализ завершён"), TimeSpan.FromMilliseconds(50), "анализ"));
                Assert.That(ex.Message, Does.Contain("строка журнала: анализ").And.Contain("новых строк нет"));
            }
            finally
            {
                Wait.PollInterval = poll;
            }
        }

        [Test]
        public void Backup_restores_content_and_absence()
        {
            string existing = Path.Combine(_dir, "BaseSettings.json");
            string absent = Path.Combine(_dir, "b.json");
            byte[] original = new UTF8Encoding(true).GetPreamble();
            File.WriteAllBytes(existing, original);
            File.AppendAllText(existing, "{ \"ServerAdress\": \"http://old/\" }");
            byte[] before = File.ReadAllBytes(existing);
            LocalFileBackup backup = LocalFileBackup.Take(existing, absent);
            File.WriteAllText(existing, "изменено");
            File.WriteAllText(absent, "появился");
            backup.Restore();
            Assert.That(File.ReadAllBytes(existing), Is.EqualTo(before), "побайтно, вместе с BOM");
            Assert.That(File.Exists(absent), Is.False);
            Assert.That(backup.Paths, Has.Count.EqualTo(2));
        }

        [Test]
        public void Backup_restore_with_locked_file_reports_it_and_still_restores_the_others()
        {
            string locked = Path.Combine(_dir, "Locked.json");
            string other = Path.Combine(_dir, "Other.json");
            File.WriteAllText(locked, "исходное", Encoding.UTF8);
            File.WriteAllText(other, "исходное", Encoding.UTF8);
            byte[] otherBefore = File.ReadAllBytes(other);
            LocalFileBackup backup = LocalFileBackup.Take(locked, other);
            File.WriteAllText(other, "изменено");
            using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                IOException ex = Assert.Throws<IOException>(() => backup.Restore());
                Assert.That(ex.Message, Does.StartWith("Локальные файлы не восстановлены").And.Contain(locked));
                Assert.That(ex.InnerException, Is.Not.Null);
            }
            Assert.That(File.ReadAllBytes(other), Is.EqualTo(otherBefore), "остальные файлы возвращены, несмотря на сбой с первым");
        }

        [Test]
        public void Backup_restore_cannot_delete_locked_new_file()
        {
            string created = Path.Combine(_dir, "Created.json");
            LocalFileBackup backup = LocalFileBackup.Take(created);
            File.WriteAllText(created, "появился");
            using (new FileStream(created, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                IOException ex = Assert.Throws<IOException>(() => backup.Restore());
                Assert.That(ex.Message, Does.StartWith("Локальные файлы не восстановлены").And.Contain(created));
            }
        }
    }
}
