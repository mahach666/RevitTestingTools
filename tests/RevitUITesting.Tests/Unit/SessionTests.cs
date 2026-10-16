using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using RevitUITesting.NUnit;
using RevitUITesting.Revit;

namespace RevitUITesting.Tests.Unit
{
    [TestFixture]
    public class SessionTests
    {
        private sealed class FakeApp
        {
            internal FakeApp(int number)
            {
                Number = number;
            }

            internal int Number { get; }

            internal bool Alive { get; set; } = true;

            internal bool Ended { get; set; }
        }

        private int _starts;
        private List<FakeApp> _ended;
        private SessionHolder<FakeApp> _holder;

        [SetUp]
        public void Prepare()
        {
            _starts = 0;
            _ended = new List<FakeApp>();
            _holder = new SessionHolder<FakeApp>(() => new FakeApp(++_starts), a => a.Alive, a =>
            {
                a.Ended = true;
                _ended.Add(a);
            });
        }

        [Test]
        public void One_app_per_run_while_alive()
        {
            FakeApp first = _holder.Acquire(out bool started1);
            FakeApp second = _holder.Acquire(out bool started2);

            Assert.That(second, Is.SameAs(first));
            Assert.That(started1, Is.True);
            Assert.That(started2, Is.False);
            Assert.That(_starts, Is.EqualTo(1));
        }

        [Test]
        public void Dead_app_is_released_and_restarted_by_next_fixture()
        {
            FakeApp first = _holder.Acquire(out bool _);
            first.Alive = false;

            FakeApp second = _holder.Acquire(out bool started);

            Assert.That(second.Number, Is.EqualTo(2));
            Assert.That(started, Is.True);
            Assert.That(first.Ended, Is.True);
        }

        [Test]
        public void End_releases_once_and_next_acquire_starts_again()
        {
            FakeApp first = _holder.Acquire(out bool _);
            _holder.End();
            _holder.End();

            Assert.That(_ended, Is.EqualTo(new[] { first }));
            Assert.That(_holder.Current, Is.Null);
            Assert.That(_holder.Acquire(out bool started).Number, Is.EqualTo(2));
            Assert.That(started, Is.True);
        }

        [Test]
        public void Failed_start_leaves_no_session_and_is_retried()
        {
            bool fail = true;
            SessionHolder<FakeApp> holder = new SessionHolder<FakeApp>(() => fail ? throw new InvalidOperationException("Revit не стартовал") : new FakeApp(1), a => true, a => { });

            Assert.Throws<InvalidOperationException>(() => holder.Acquire(out bool _));
            Assert.That(holder.Current, Is.Null);
            fail = false;
            Assert.That(holder.Acquire(out bool _).Number, Is.EqualTo(1));
        }

        [Test]
        public void End_error_still_clears_session()
        {
            SessionHolder<FakeApp> holder = new SessionHolder<FakeApp>(() => new FakeApp(1), a => true, a => throw new IOException("Revit не закрылся"));
            holder.Acquire(out bool _);

            Assert.Throws<IOException>(() => holder.End());
            Assert.That(holder.Current, Is.Null);
        }

        [Test]
        public void Replace_stops_and_ends_old_and_the_new_app_is_owned_by_the_session()
        {
            FakeApp first = _holder.Acquire(out bool _);
            List<FakeApp> stopped = new List<FakeApp>();

            FakeApp second = _holder.Replace(stopped.Add);

            Assert.That(second.Number, Is.EqualTo(2));
            Assert.That(stopped, Is.EqualTo(new[] { first }));
            Assert.That(first.Ended, Is.True);
            Assert.That(_holder.Current, Is.SameAs(second), "перезапущенный объект — текущий объект сеанса");
            Assert.That(_holder.Acquire(out bool started), Is.SameAs(second));
            Assert.That(started, Is.False);
            _holder.End();
            Assert.That(_ended, Is.EqualTo(new[] { first, second }), "End закрывает перезапущенный объект");
        }

        [Test]
        public void Replace_without_session_is_refused()
        {
            Assert.That(() => _holder.Replace(a => { }), Throws.InvalidOperationException);
            Assert.That(_starts, Is.EqualTo(0));
        }

        [Test]
        public void Replace_with_failed_stop_ends_old_and_does_not_start()
        {
            FakeApp first = _holder.Acquire(out bool _);

            Assert.Throws<IOException>(() => _holder.Replace(a => throw new IOException("Quit не удался")));

            Assert.That(first.Ended, Is.True);
            Assert.That(_holder.Current, Is.Null);
            Assert.That(_starts, Is.EqualTo(1));
        }

        [Test]
        public void Replace_with_failed_start_leaves_no_session()
        {
            bool fail = false;
            int starts = 0;
            SessionHolder<FakeApp> holder = new SessionHolder<FakeApp>(() => fail ? throw new InvalidOperationException("Revit не стартовал") : new FakeApp(++starts), a => true, a => a.Ended = true);
            FakeApp first = holder.Acquire(out bool _);
            fail = true;

            Assert.Throws<InvalidOperationException>(() => holder.Replace(a => { }));

            Assert.That(first.Ended, Is.True);
            Assert.That(holder.Current, Is.Null);
        }

        [Test]
        public void RevitApp_owned_by_session_refuses_its_own_Restart()
        {
            RevitApp app = (RevitApp)FormatterServices.GetUninitializedObject(typeof(RevitApp));
            app.OwnedBySession = true;

            Assert.That(() => app.Restart(), Throws.InvalidOperationException.With.Message.Contains("RevitSession.Restart()"));
        }

        [Test]
        [NonParallelizable]
        public void Kill_happens_before_logging_so_a_failing_log_sink_does_not_keep_the_process()
        {
            // вместо Revit — безобидный долгий процесс; Kill должен завершить его, даже если приёмник журнала бросает (ProcessExit)
            Process dummy = Process.Start(new ProcessStartInfo("ping.exe", "-n 120 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            Action<string> saved = UiLog.Sink;
            try
            {
                RevitApp app = (RevitApp)FormatterServices.GetUninitializedObject(typeof(RevitApp));
                typeof(RevitApp).GetField("<Process>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app, dummy);
                UiLog.Sink = line => throw new InvalidOperationException("приёмник журнала недоступен");

                Assert.That(() => app.Kill("проверка порядка"), Throws.InvalidOperationException.With.Message.Contains("приёмник журнала"));

                Assert.That(dummy.WaitForExit(5000), Is.True, "процесс завершён, хотя запись в журнал упала");
                Assert.That(UiLog.Recent(3).Any(l => l.Contains($"Revit pid {dummy.Id} {RevitApp.KilledText}: проверка порядка")), Is.True, "причина Kill всё равно в журнале шагов");
            }
            finally
            {
                UiLog.Sink = saved;
                if (!dummy.HasExited)
                {
                    dummy.Kill();
                }
                dummy.Dispose();
            }
        }

        [Test]
        public void Session_restart_without_session_is_refused()
        {
            Assume.That(RevitSession.Current, Is.Null, "в модульных тестах сеанса Revit нет");
            Assert.That(() => RevitSession.Restart(), Throws.InvalidOperationException.With.Message.Contains("RevitSession.Acquire"));
        }

        [Test]
        public void Model_attribute_resolves_new_relative_and_absolute_paths()
        {
            string dir = Path.Combine(Path.GetTempPath(), "RevitUITesting.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "Models"));
            string model = Path.Combine(dir, "Models", "sample.rvt");
            File.WriteAllText(model, "");
            try
            {
                Assert.That(new RevitUiModelAttribute("@new").Resolve(dir), Is.EqualTo("@new"));
                Assert.That(new RevitUiModelAttribute("@new").IsNew, Is.True);
                Assert.That(new RevitUiModelAttribute(@"Models\sample.rvt").Resolve(dir), Is.EqualTo(model));
                Assert.That(new RevitUiModelAttribute(model).Resolve(@"C:\other"), Is.EqualTo(model));
                Assert.That(new RevitUiModelAttribute("@new").CopyToTemp, Is.True);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Model_attribute_rejects_missing_wrong_and_long_prefix_paths()
        {
            Assert.That(() => new RevitUiModelAttribute(@"C:\nope\missing.rvt").Resolve(@"C:\"), Throws.TypeOf<FileNotFoundException>());
            Assert.That(() => new RevitUiModelAttribute(@"C:\Windows\notepad.exe").Resolve(@"C:\"), Throws.ArgumentException.With.Message.Contains(".rvt"));
            Assert.That(() => new RevitUiModelAttribute(@"\\?\C:\m.rvt").Resolve(@"C:\"), Throws.ArgumentException.With.Message.Contains("Illegal characters"));
            Assert.That(() => new RevitUiModelAttribute(" "), Throws.ArgumentException);
        }
    }
}
