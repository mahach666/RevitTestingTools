using System;
using System.IO;
using NUnit.Framework;
using RevitUITesting.NUnit;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Contract
{
    /// <summary>
    /// Запуск, ping, файл сеанса, подхват и закрытие живого Revit (ENU). Каждый тест сам запускает и закрывает Revit (минуты на тест).
    /// dotnet test --filter "Category=RevitContract&amp;FullyQualifiedName~RevitAppContractTests"
    /// </summary>
    [TestFixture]
    [Explicit("живой Revit (версия из revit-ui-tests.json, язык ENU)")]
    [Category("RevitContract")]
    [NonParallelizable]
    public class RevitAppContractTests
    {
        [OneTimeSetUp]
        public void NoSessionRevit()
        {
            UiLog.Sink = line => TestContext.Progress.WriteLine(line);
            RevitSession.End();
        }

        private static RevitUiConfig Config(bool keepOpen = false, bool attach = false)
        {
            RevitUiConfig config = RevitUiConfig.Load(TestContext.CurrentContext.TestDirectory);
            config.KeepRevitOpen = keepOpen;
            config.AttachToRunning = attach;
            return config;
        }

        [Test]
        [Order(1)]
        public void Start_ping_session_file_and_quit()
        {
            RevitUiConfig config = Config();
            RevitApp app = RevitApp.StartOrAttach(config);
            int pid = app.ProcessId;
            try
            {
                HostPing ping = app.Host.Ping();
                Assert.That(ping.Pid, Is.EqualTo(pid));
                Assert.That(ping.RevitVersion, Does.StartWith(config.RevitVersion));
                Assert.That(ping.ServerVersion, Is.Not.Empty);
                RevitSessionFile session = RevitSessionFile.Read(RevitSessionFile.DefaultPath);
                Assert.That(session.Pid, Is.EqualTo(pid));
                Assert.That(session.Pipe, Is.EqualTo(app.PipeName));
                string title = app.MainWindow.Name;
                Assert.That(title, Does.StartWith(RevitTitles.Windows.MainPrefix + config.RevitVersion));
                Evidence.Write("main-window", $"Name: {title}{Environment.NewLine}ClassName: {app.MainWindow.ClassName}{Environment.NewLine}ping: {ping}{Environment.NewLine}{app.Windows.DescribeDialogs()}");
            }
            finally
            {
                app.Quit();
                app.Dispose();
            }
            Assert.That(app.Process.HasExited, Is.True);
            Assert.That(File.Exists(RevitSessionFile.DefaultPath), Is.False, "файл сеанса удаляется при Quit");
        }

        [Test]
        [Order(2)]
        public void Kept_open_revit_is_refused_without_attach_and_attached_with_it()
        {
            RevitApp first = RevitApp.StartOrAttach(Config(keepOpen: true));
            int pid = first.ProcessId;
            RevitApp second = null;
            try
            {
                first.Dispose();
                Assert.That(first.Process.HasExited, Is.False, "keepRevitOpen оставляет Revit открытым");

                Assert.That(() => RevitApp.StartOrAttach(Config()), Throws.InvalidOperationException.With.Message.Contains("attachToRunning"));

                second = RevitApp.StartOrAttach(Config(attach: true));
                try
                {
                    Assert.That(second.ProcessId, Is.EqualTo(pid));
                    Assert.That(second.StartedByUs, Is.False);
                    Assert.That(second.Host.Ping().Pid, Is.EqualTo(pid), "канал из session.json обслуживает подхваченный Revit");
                }
                finally
                {
                    second.Quit();
                    second.Dispose();
                }
            }
            finally
            {
                // тест упал до подхвата: Revit, оставленный first (keepRevitOpen), запущен этим тестом — без Kill он остался бы сиротой
                if (second == null)
                {
                    first.Kill("контрактный тест упал до подхвата оставленного Revit");
                }
            }
            Assert.That(second.Process.HasExited, Is.True);
        }

        /// <summary>Оставляет Revit с новым проектом открытым для инспектора (задача 10, шаги 6–8); закрывает его тест Attach_and_quit.</summary>
        [Test]
        [Order(10)]
        public void Leave_open_with_new_model_for_inspection()
        {
            RevitApp app = RevitApp.StartOrAttach(Config(keepOpen: true));
            bool handedOver = false;
            try
            {
                HostDocument doc = app.Host.Open(RevitHostClient.NewModel, false);
                TestContext.Out.WriteLine($"Revit pid {app.ProcessId} оставлен открытым с документом {doc}");
                handedOver = true;
            }
            finally
            {
                try
                {
                    // модель не открылась: Revit для инспектора не нужен, а keepRevitOpen оставил бы его сиротой
                    if (!handedOver)
                    {
                        app.Kill("контрактный тест упал до передачи Revit инспектору");
                    }
                }
                finally
                {
                    app.Dispose();
                }
            }
            Assert.That(app.Process.HasExited, Is.False);
        }

        [Test]
        [Order(11)]
        public void Attach_and_quit()
        {
            RevitApp app = RevitApp.StartOrAttach(Config(attach: true));
            // в подхваченном Revit может остаться изменённый документ (конец сеанса ContractSession при подхвате) — Quit отвечает на вопрос о сохранении
            ContractModel.AssertClosedWithoutKill(app, () =>
            {
                app.Quit();
                app.Dispose();
            });
        }
    }
}
