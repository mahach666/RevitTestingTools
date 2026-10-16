using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RevitUITesting.Revit;

namespace RevitUITesting.Tests.Unit
{
    [TestFixture]
    [NonParallelizable]
    public class RevitLaunchTests
    {
        private static readonly DateTime Started = new DateTime(2026, 10, 7, 20, 0, 0, DateTimeKind.Utc);
        private const string SessionPath = @"C:\Temp\RevitUITesting\session.json";

        [Test]
        public void Language_and_pipe_are_passed()
        {
            ProcessStartInfo info = RevitLaunch.StartInfo(@"C:\Program Files\Autodesk\Revit 2022\Revit.exe", "ENU", "RevitTestHost-abc");

            Assert.That(info.Arguments, Is.EqualTo("/language ENU"));
            Assert.That(info.UseShellExecute, Is.False);
            Assert.That(info.WorkingDirectory, Is.EqualTo(@"C:\Program Files\Autodesk\Revit 2022"));
            Assert.That(info.Environment[RevitApp.ServeVariable], Is.EqualTo("RevitTestHost-abc"));
        }

        [Test]
        public void Empty_language_means_installation_language()
        {
            Assert.That(RevitLaunch.StartInfo(@"C:\R\Revit.exe", null, "p").Arguments, Is.EqualTo(""));
        }

        [TestCase("EN U")]
        [TestCase("ENU /nosplash")]
        [TestCase("EN")]
        public void Suspicious_language_is_rejected(string language)
        {
            Assert.That(() => RevitLaunch.StartInfo(@"C:\R\Revit.exe", language, "p"), Throws.ArgumentException.With.Message.Contains("language"));
        }

        [Test]
        public void Inherited_cli_variables_are_removed()
        {
            string[] names = { "REVITTESTHOST_CLI", "REVITTESTHOST_FILTER", "REVITTESTHOST_OUTPUT" };
            string[] saved = Array.ConvertAll(names, Environment.GetEnvironmentVariable);
            try
            {
                foreach (string name in names)
                {
                    Environment.SetEnvironmentVariable(name, "1");
                }

                ProcessStartInfo info = RevitLaunch.StartInfo(@"C:\R\Revit.exe", "ENU", "p");

                foreach (string name in names)
                {
                    Assert.That(info.Environment.ContainsKey(name), Is.False, name + " попал бы в окружение Revit");
                }
            }
            finally
            {
                for (int i = 0; i < names.Length; i++)
                {
                    Environment.SetEnvironmentVariable(names[i], saved[i]);
                }
            }
        }

        [Test]
        public void Environment_block_is_sorted_case_insensitively_and_double_nul_terminated()
        {
            Dictionary<string, string> environment = new Dictionary<string, string> { ["b"] = "2", ["A"] = "1", ["c"] = "3" };

            Assert.That(RevitLaunch.EnvironmentBlock(environment), Is.EqualTo("A=1\0b=2\0c=3\0\0"));
        }

        [Test]
        public void Empty_environment_block_is_two_nuls()
        {
            Assert.That(RevitLaunch.EnvironmentBlock(new Dictionary<string, string>()), Is.EqualTo("\0\0"));
        }

        [Test]
        public void Environment_block_keeps_non_ascii_values_and_empty_values()
        {
            Dictionary<string, string> environment = new Dictionary<string, string> { ["ПАПКА"] = @"C:\Проекты\Модель", ["EMPTY"] = null };

            Assert.That(RevitLaunch.EnvironmentBlock(environment), Is.EqualTo("EMPTY=\0ПАПКА=C:\\Проекты\\Модель\0\0"));
        }

        [TestCase("A=B", "1")]
        [TestCase("A\0B", "1")]
        [TestCase("", "1")]
        [TestCase("A", "1\02")]
        public void Environment_block_rejects_names_and_values_that_break_the_block(string name, string value)
        {
            Assert.That(() => RevitLaunch.EnvironmentBlock(new Dictionary<string, string> { [name] = value }), Throws.ArgumentException);
        }

        [Test]
        public void Environment_block_rejects_names_differing_only_by_case()
        {
            Dictionary<string, string> environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["Path"] = "1", ["PATH"] = "2" };

            Assert.That(() => RevitLaunch.EnvironmentBlock(environment), Throws.ArgumentException.With.Message.Contains("PATH"));
        }

        [Test]
        public void Environment_block_of_start_info_has_serve_pipe_and_no_cli_variables()
        {
            string saved = Environment.GetEnvironmentVariable("REVITTESTHOST_CLI");
            try
            {
                Environment.SetEnvironmentVariable("REVITTESTHOST_CLI", "1");

                string block = RevitLaunch.EnvironmentBlock(RevitLaunch.StartInfo(@"C:\R\Revit.exe", "ENU", "RevitTestHost-abc").Environment);

                string[] entries = block.TrimEnd('\0').Split('\0');
                Assert.That(entries, Has.Member(RevitApp.ServeVariable + "=RevitTestHost-abc"));
                Assert.That(entries.Any(e => e.StartsWith(RevitApp.CliVariable + "=", StringComparison.OrdinalIgnoreCase)), Is.False);
                Assert.That(block, Does.EndWith("\0\0"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("REVITTESTHOST_CLI", saved);
            }
        }

        [Test]
        public void Command_line_quotes_the_executable()
        {
            Assert.That(RevitLaunch.CommandLine(RevitLaunch.StartInfo(@"C:\Program Files\R\Revit.exe", "ENU", "p")), Is.EqualTo("\"C:\\Program Files\\R\\Revit.exe\" /language ENU"));
            Assert.That(RevitLaunch.CommandLine(RevitLaunch.StartInfo(@"C:\R\Revit.exe", null, "p")), Is.EqualTo("\"C:\\R\\Revit.exe\""));
        }

        /// <summary>
        /// Запущенный процесс не наследует дескрипторы процесса тестов: иначе Revit с keepRevitOpen держит открытыми каналы stdout/stderr
        /// вызывающего, и «dotnet test» с перенаправленным выводом не завершается, пока жив Revit (задача 10).
        /// </summary>
        [Test]
        public void Started_process_does_not_inherit_handles_of_the_test_process()
        {
            string ping = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "PING.EXE");
            ProcessStartInfo info = new ProcessStartInfo(ping, "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(ping) };
            using (AnonymousPipeServerStream pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable))
            {
                Process child = RevitLaunch.Start(info);
                try
                {
                    pipe.DisposeLocalCopyOfClientHandle();
                    Task<int> read = Task.Run(() => pipe.Read(new byte[1], 0, 1));

                    bool eof = read.Wait(TimeSpan.FromSeconds(5));

                    Assert.That(child.HasExited, Is.False, "потомок должен быть жив во время проверки");
                    Assert.That(eof, Is.True, "канал не закрылся: потомок унаследовал дескриптор записи процесса тестов");
                    Assert.That(read.Result, Is.EqualTo(0));
                }
                finally
                {
                    if (!child.HasExited)
                    {
                        child.Kill();
                        child.WaitForExit(10000);
                    }
                    child.Dispose();
                }
            }
        }

        [Test]
        public void Started_process_is_tracked_with_start_time_and_exit_code()
        {
            string cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            ProcessStartInfo info = new ProcessStartInfo(cmd, "/c ping -n 2 127.0.0.1 >nul & exit 7") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(cmd) };

            using (Process child = RevitLaunch.Start(info))
            {
                Assert.That(child.WaitForExit(10000), Is.True);
                Assert.That(child.ExitCode, Is.EqualTo(7));
                Assert.That(child.StartTime, Is.GreaterThan(DateTime.Now.AddMinutes(-1)));
            }
        }

        [Test]
        public void Failed_start_names_the_command_line()
        {
            ProcessStartInfo info = new ProcessStartInfo(@"C:\нет такой папки\Revit.exe", "/language ENU") { UseShellExecute = false };

            Assert.That(() => RevitLaunch.Start(info), Throws.TypeOf<Win32Exception>().With.Message.Contains(@"C:\нет такой папки\Revit.exe"));
        }

        [Test]
        public void Nothing_running_means_start()
        {
            Assert.That(AttachDecision.Choose(new RunningRevit[0], null, SessionPath, "2022", false), Is.Null);
        }

        [Test]
        public void Running_revit_without_attach_is_refused()
        {
            Assert.That(() => AttachDecision.Choose(new[] { new RunningRevit(77, Started) }, null, SessionPath, "2022", false),
                Throws.InvalidOperationException.With.Message.Contains("77").And.Message.Contains("attachToRunning"));
        }

        [Test]
        public void Users_own_revit_without_session_file_is_not_attached()
        {
            Assert.That(() => AttachDecision.Choose(new[] { new RunningRevit(77, Started) }, null, SessionPath, "2022", true),
                Throws.InvalidOperationException.With.Message.Contains("файла сеанса").And.Message.Contains(SessionPath));
        }

        [Test]
        public void Reused_pid_with_other_start_time_is_not_attached()
        {
            RevitSessionFile session = new RevitSessionFile(77, "RevitTestHost-a", "2022", Started.AddHours(-3));
            Assert.That(() => AttachDecision.Choose(new[] { new RunningRevit(77, Started) }, session, SessionPath, "2022", true),
                Throws.InvalidOperationException.With.Message.Contains("не запущен RevitUITesting"));
        }

        [Test]
        public void Other_revit_version_is_not_attached()
        {
            RevitSessionFile session = new RevitSessionFile(77, "RevitTestHost-a", "2024", Started);
            Assert.That(() => AttachDecision.Choose(new[] { new RunningRevit(77, Started) }, session, SessionPath, "2022", true),
                Throws.InvalidOperationException.With.Message.Contains("2024"));
        }

        [Test]
        public void Matching_session_is_attached()
        {
            RevitSessionFile session = new RevitSessionFile(78, "RevitTestHost-a", "2022", Started.AddMilliseconds(400));
            int? pid = AttachDecision.Choose(new[] { new RunningRevit(77, Started.AddHours(-1)), new RunningRevit(78, Started) }, session, SessionPath, "2022", true);
            Assert.That(pid, Is.EqualTo(78));
        }

        [Test]
        public void First_close_is_sent_immediately()
        {
            Assert.That(CloseResend.Due(null, TimeSpan.Zero, CloseResend.Interval), Is.True);
            Assert.That(CloseResend.Due(null, TimeSpan.FromSeconds(42), CloseResend.Interval), Is.True);
        }

        [Test]
        public void Close_is_not_resent_before_interval()
        {
            Assert.That(CloseResend.Interval, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(CloseResend.Due(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), CloseResend.Interval), Is.False);
            Assert.That(CloseResend.Due(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14.9), CloseResend.Interval), Is.False);
        }

        [Test]
        public void Close_is_resent_after_interval()
        {
            Assert.That(CloseResend.Due(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), CloseResend.Interval), Is.True);
            Assert.That(CloseResend.Due(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(31), CloseResend.Interval), Is.True);
        }

        [Test]
        public void Ready_ping_attempt_is_capped_at_five_seconds()
        {
            Assert.That(ReadyPing.MaxAttempt, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(ReadyPing.AttemptTimeout(TimeSpan.FromSeconds(290), TimeSpan.FromSeconds(60)), Is.EqualTo(TimeSpan.FromSeconds(5)));
        }

        [Test]
        public void Ready_ping_attempt_does_not_outlast_remaining_time()
        {
            Assert.That(ReadyPing.AttemptTimeout(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60)), Is.EqualTo(TimeSpan.FromSeconds(2)));
        }

        [Test]
        public void Ready_ping_attempt_respects_shorter_host_timeout()
        {
            Assert.That(ReadyPing.AttemptTimeout(TimeSpan.FromSeconds(290), TimeSpan.FromSeconds(3)), Is.EqualTo(TimeSpan.FromSeconds(3)));
        }
    }
}
