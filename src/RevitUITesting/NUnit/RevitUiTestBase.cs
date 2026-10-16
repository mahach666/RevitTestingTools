using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.NUnit
{
    /// <summary>
    /// Базовый класс NUnit-фикстуры UI-тестов Revit: Revit один на прогон (<see cref="RevitSession"/>), модель на класс
    /// (<see cref="RevitUiModelAttribute"/>), перед тестом — журнал шагов, отметка журналов, уборка окон и ожидание простоя Revit,
    /// после теста — артефакты падения, уборка окон, возвраты <see cref="AfterTest"/> в обратном порядке, отчёт об ошибках Revit без провала теста.
    /// </summary>
    public abstract class RevitUiTestBase
    {
        private readonly List<(string Description, Action Restore)> _afterTest = new List<(string, Action)>();
        private readonly List<(string Description, Action Restore)> _afterTests = new List<(string, Action)>();
        private readonly List<LocalFileBackup> _files = new List<LocalFileBackup>();
        private LogWatch _logs;
        private bool _setupCompleted;

        protected RevitUiConfig Config { get; private set; }

        /// <summary>Revit сеанса (<see cref="RevitSession.Current"/>): после <see cref="RevitSession.Restart"/> — уже новый объект. Сеанса нет — InvalidOperationException.</summary>
        protected RevitApp App => RevitSession.Current ?? throw new InvalidOperationException("Revit сеанса нет: RevitSession.End() уже вызван или запуск (перезапуск) Revit не удался.");

        protected RevitHostClient Host => App.Host;

        /// <summary>Модель класса (из <see cref="RevitUiModelAttribute"/>); null — атрибута нет.</summary>
        protected HostDocument Model { get; private set; }

        protected TimeSpan Timeout => Config.DefaultTimeout;

        [OneTimeSetUp]
        public void RevitOneTimeSetUp()
        {
            Config = RevitSession.Config ?? RevitUiConfig.Load(TestContext.CurrentContext.TestDirectory);
            UiLog.Sink = line => TestContext.Progress.WriteLine(line);
            UiLog.Action("конфигурация: " + Config.Describe());
            foreach (string problem in Config.Validate())
            {
                UiLog.Warn("конфигурация: " + problem);
            }
            RevitSession.Acquire(Config);
            TestArtifacts.Prune(Config);
            _logs = CreateLogWatch();
            OpenFixtureModel();
            _setupCompleted = true;
            OnBeforeTests();
        }

        [OneTimeTearDown]
        public void RevitOneTimeTearDown()
        {
            List<Exception> errors = new List<Exception>();
            if (_setupCompleted)
            {
                Try(OnAfterTests, errors);
            }
            RunRestores(_afterTests, errors);
            foreach (LocalFileBackup backup in _files)
            {
                Try(backup.Restore, errors);
            }
            _files.Clear();
            Rethrow(errors, "Уборка после тестов класса завершилась ошибками");
        }

        [SetUp]
        public void RevitSetUp()
        {
            UiLog.BeginTest(TestContext.CurrentContext.Test.FullName);
            App.EnsureAlive();
            _logs?.Mark();
            ResetOrThrow("перед тестом");
            Host.WaitIdle();
        }

        [TearDown]
        public void RevitTearDown()
        {
            TestStatus status = TestContext.CurrentContext.Result.Outcome.Status;
            if (status == TestStatus.Failed || status == TestStatus.Inconclusive)
            {
                Diagnostic(SaveFailureArtifacts, "артефакты падения не сохранены");
            }
            List<Exception> errors = new List<Exception>();
            UiResetReport report = null;
            RevitApp app = RevitSession.Current;
            if (app != null && app.IsAlive)
            {
                Try(() => report = app.Windows.ResetUi(), errors);
            }
            RunRestores(_afterTest, errors);
            Diagnostic(() => ReportProblems(report), "отчёт об окнах и журналах после теста не составлен");
            if (report != null && !report.Clean)
            {
                errors.Add(new InvalidOperationException("После теста в Revit осталось окно, которое уборка не закрывает: " + string.Join("; ", report.Remaining)));
            }
            Rethrow(errors, "Уборка после теста завершилась ошибками");
        }

        /// <summary>Журналы плагина для отметки, проверок и артефактов (например, <c>new LogWatch(папка, new[] { "MyPlugin" })</c>); null — не отслеживаются.</summary>
        protected virtual LogWatch CreateLogWatch()
        {
            return null;
        }

        /// <summary>Подготовка перед всеми тестами класса (после открытия модели).</summary>
        protected virtual void OnBeforeTests()
        {
        }

        /// <summary>Очистка после всех тестов класса. Не вызывается, если подготовка класса не завершилась.</summary>
        protected virtual void OnAfterTests()
        {
        }

        /// <summary>Шаг сценария в журнал (виден в выводе теста и в артефактах при падении).</summary>
        protected void Step(string text)
        {
            UiLog.Step(text);
        }

        /// <summary>Вернуть состояние после теста (в том числе упавшего): в обратном порядке регистрации, после уборки окон; ошибки роняют TearDown.</summary>
        protected void AfterTest(string description, Action restore)
        {
            _afterTest.Add((description ?? throw new ArgumentNullException(nameof(description)), restore ?? throw new ArgumentNullException(nameof(restore))));
        }

        /// <summary>Вернуть состояние после всех тестов класса: в обратном порядке, после <see cref="OnAfterTests"/> и до возврата файлов.</summary>
        protected void AfterTests(string description, Action restore)
        {
            _afterTests.Add((description ?? throw new ArgumentNullException(nameof(description)), restore ?? throw new ArgumentNullException(nameof(restore))));
        }

        /// <summary>Снять копию локального файла сейчас и вернуть его после всех тестов класса (побайтно).</summary>
        protected void RestoreFileAfterTests(string path)
        {
            _files.Add(LocalFileBackup.Take(path));
        }

        /// <summary>Проверка в модели: Revit-тесты RevitTestHost по фильтру на активном документе с параметрами (провал — тексты упавших проверок).</summary>
        protected void AssertInRevit(string filter, IDictionary<string, string> parameters = null)
        {
            App.EnsureAlive();
            Host.AssertRun(filter, parameters);
        }

        /// <summary>Нет открытых окон ошибок и предупреждений Revit (иначе — провал с их текстами).</summary>
        protected void AssertNoRevitErrors()
        {
            IReadOnlyList<string> open = App.Windows.RevitErrors();
            if (open.Count > 0)
            {
                Assert.Fail("Открыты окна ошибок и предупреждений Revit: " + string.Join("; ", open));
            }
        }

        /// <summary>В журналах (<see cref="CreateLogWatch"/>) с начала теста нет ошибок (по умолчанию — уровень ERROR или FATAL).</summary>
        protected void AssertNoLogErrors(Func<LogLine, bool> isError = null)
        {
            Func<LogLine, bool> error = isError ?? IsErrorLine;
            LogLine[] found = LogLines(error).ToArray();
            if (found.Length > 0)
            {
                Assert.Fail("Ошибки в журналах плагина:" + Environment.NewLine + string.Join(Environment.NewLine, found.Select(l => l.ToString())));
            }
        }

        /// <summary>Строки журналов с начала теста, подходящие под условие (null — все).</summary>
        protected IReadOnlyList<LogLine> LogLines(Func<LogLine, bool> match = null)
        {
            IReadOnlyList<LogLine> lines = _logs?.NewLines() ?? new LogLine[0];
            return match == null ? lines : lines.Where(match).ToList();
        }

        /// <summary>Дождаться строки журналов с начала теста; timeout null — DefaultTimeout.</summary>
        protected LogLine WaitLog(Func<LogLine, bool> match, TimeSpan? timeout = null, [CallerArgumentExpression("match")] string description = null)
        {
            if (_logs == null)
            {
                throw new InvalidOperationException("Журналы не отслеживаются: переопределите CreateLogWatch().");
            }
            return _logs.WaitLine(match, timeout ?? Config.DefaultTimeout, description);
        }

        /// <summary>Путь для файла, который создаст тест: в папке артефактов теста; файла там ещё нет.</summary>
        protected string NewOutputPath(string fileName)
        {
            string folder = Path.Combine(Config.ResolvedArtifactsDir, "output", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + TestArtifacts.Sanitize(TestContext.CurrentContext.Test.Name, 80));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, fileName);
            if (File.Exists(path))
            {
                throw new IOException("Файл уже существует: " + path);
            }
            return path;
        }

        private void OpenFixtureModel()
        {
            RevitUiModelAttribute attribute = GetType().GetCustomAttribute<RevitUiModelAttribute>(true);
            if (attribute == null)
            {
                Model = null;
                return;
            }
            string target = attribute.Resolve(TestContext.CurrentContext.TestDirectory);
            ResetOrThrow("перед открытием модели класса");
            Host.WaitIdle();
            HostDocument previous = RevitSession.LastModel;
            Model = Host.Open(target, attribute.CopyToTemp && !attribute.IsNew);
            UiLog.Action("модель класса: " + Model);
            if (previous != null && !SameDocument(previous, Model))
            {
                // у нового проекта («@new», ещё не сохранён) пути нет — он закрывается по заголовку
                bool byPath = !string.IsNullOrEmpty(previous.Path);
                string key = byPath ? previous.Path : previous.Title;
                try
                {
                    if (byPath)
                    {
                        Host.ClosePath(key);
                    }
                    else
                    {
                        Host.Close(key);
                    }
                }
                catch (HostErrorException ex) when (ex.Code == HostErrorCodes.NotFound)
                {
                    UiLog.Action($"модель прошлого класса уже закрыта: {key}");
                }
            }
            RevitSession.LastModel = Model;
        }

        private void ResetOrThrow(string when)
        {
            UiResetReport report = App.Windows.ResetUi();
            foreach (string error in report.RevitErrors)
            {
                TestContext.Out.WriteLine($"Окно Revit ({when}): {error}");
            }
            if (!report.Clean)
            {
                string screen = App.Screenshot("reset-ui");
                throw new InvalidOperationException($"{when}: в Revit осталось окно, которое уборка не закрывает — тест не запущен. {string.Join("; ", report.Remaining)}. Снимок: {screen}");
            }
        }

        private void SaveFailureArtifacts()
        {
            TestContext.TestAdapter test = TestContext.CurrentContext.Test;
            TestArtifacts artifacts = TestArtifacts.For(Config, test.ClassName, test.Name, DateTime.Now);
            List<string> failed = new List<string>();
            Save(() => artifacts.WriteText("exception.txt", TestContext.CurrentContext.Result.Message + Environment.NewLine + TestContext.CurrentContext.Result.StackTrace, "Сообщение и стек"), failed, "exception.txt");
            Save(() => artifacts.WriteText("steps.log", UiLog.Dump(), "Журнал шагов теста"), failed, "steps.log");
            Save(() => artifacts.Screen(), failed, "screen.png");
            Save(() => artifacts.WriteText("revit-journal.txt", RevitJournal.Tail(RevitJournal.DirectoryFor(Config.RevitVersion), 300), "Хвост журнала Revit"), failed, "revit-journal.txt");
            Save(() => artifacts.WriteText("logs.txt", string.Join(Environment.NewLine, LogLines().Select(l => l.ToString())), "Журналы плагина за тест"), failed, "logs.txt");
            if (App.IsAlive)
            {
                Save(() => artifacts.WriteText("revit-dialogs.txt", App.Windows.DescribeDialogs(), "Окна и TaskDialog Revit"), failed, "revit-dialogs.txt");
                Save(() => artifacts.WriteText("host.txt", HostSnapshot(), "Сервер RevitTestHost: ping и документы"), failed, "host.txt");
                Save(() => artifacts.WriteText("windows.txt", DumpWindows(App.TopLevelWindows()), "Дерево UI Automation окон приложения"), failed, "windows.txt");
            }
            else
            {
                Save(() => artifacts.WriteText("host.txt", $"Revit pid {App.ProcessId} завершён: окна и сервер недоступны.", "Сервер RevitTestHost"), failed, "host.txt");
            }
            TestContext.Out.WriteLine("Артефакты: " + artifacts.Folder + (failed.Count > 0 ? "; не сохранены: " + string.Join("; ", failed) : ""));
        }

        private string HostSnapshot()
        {
            try
            {
                HostPing ping = Host.Ping(TimeSpan.FromSeconds(5));
                IReadOnlyList<HostDocument> documents = Host.Documents(TimeSpan.FromSeconds(5));
                return "ping: " + ping + Environment.NewLine + "документы:" + Environment.NewLine + string.Join(Environment.NewLine, documents.Select(d => "  " + d));
            }
            catch (RevitBusyException ex)
            {
                return "Сервер не ответил за 5 с (Revit занят): " + ex.Message;
            }
        }

        private void ReportProblems(UiResetReport report)
        {
            foreach (string error in report?.RevitErrors ?? new string[0])
            {
                TestContext.Out.WriteLine("Окно Revit закрыто после теста: " + error);
            }
            foreach (LogLine line in LogLines(IsErrorLine))
            {
                TestContext.Out.WriteLine("Журнал плагина: " + line);
            }
        }

        private static bool IsErrorLine(LogLine line)
        {
            return line.Level == "ERROR" || line.Level == "FATAL";
        }

        private static void Save(Func<string> save, List<string> failed, string what)
        {
            try
            {
                save();
            }
            catch (Exception ex)
            {
                // артефакт — диагностика: его сбой не заслоняет причину падения теста и не прерывает уборку, но и не пропадает молча —
                // он в выводе теста и в журнале шагов
                string problem = what + ": " + Describe(ex);
                failed.Add(problem);
                TestContext.Out.WriteLine("Артефакт не сохранён — " + problem);
                UiLog.Warn("артефакт не сохранён — " + problem);
            }
        }

        /// <summary>
        /// Дамп окон для windows.txt: каждое окно — отдельно, сбой дампа одного окна (окно закрылось, Revit не отвечает) не теряет
        /// дампы остальных; он записывается на месте окна, в вывод теста и в журнал шагов.
        /// </summary>
        private static string DumpWindows(IEnumerable<AutomationElement> windows)
        {
            StringBuilder sb = new StringBuilder();
            int number = 0;
            foreach (AutomationElement window in windows)
            {
                number++;
                try
                {
                    sb.AppendLine(window.DumpTree(40, true));
                }
                catch (Exception ex)
                {
                    // дамп окна — диагностика: сбой одного окна отмечается в файле и выводе, остальные окна дампятся дальше
                    string problem = $"окно {number}: дамп не получен — {Describe(ex)}";
                    sb.AppendLine(problem);
                    TestContext.Out.WriteLine("windows.txt: " + problem);
                    UiLog.Warn("windows.txt: " + problem);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// Диагностика после теста (артефакты, отчёт): сбой не роняет TearDown и не прерывает уборку, а пишется в вывод теста и журнал шагов.
        /// </summary>
        private static void Diagnostic(Action action, string what)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // диагностика не заслоняет причину падения теста и не мешает уборке окон и возвратам; сбой виден в выводе и журнале
                string problem = what + ": " + Describe(ex);
                TestContext.Out.WriteLine(problem);
                UiLog.Warn(problem);
            }
        }

        private static string Describe(Exception ex)
        {
            return ex.GetType().FullName + ": " + ex.Message;
        }

        /// <summary>Тот же документ: совпадают пути (без учёта регистра) или, у несохранённых проектов без пути, заголовки.</summary>
        private static bool SameDocument(HostDocument a, HostDocument b)
        {
            if (string.IsNullOrEmpty(a.Path) || string.IsNullOrEmpty(b.Path))
            {
                return string.IsNullOrEmpty(a.Path) && string.IsNullOrEmpty(b.Path) && string.Equals(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        }

        private static void RunRestores(List<(string Description, Action Restore)> restores, List<Exception> errors)
        {
            for (int i = restores.Count - 1; i >= 0; i--)
            {
                (string description, Action restore) = restores[i];
                UiLog.Action("возврат: " + description);
                Try(() =>
                {
                    try
                    {
                        restore();
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("Не удалось вернуть «" + description + "»: " + ex.Message, ex);
                    }
                }, errors);
            }
            restores.Clear();
        }

        private static void Try(Action action, List<Exception> errors)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        private static void Rethrow(List<Exception> errors, string message)
        {
            if (errors.Count == 1)
            {
                ExceptionDispatchInfo.Capture(errors[0]).Throw();
            }
            if (errors.Count > 1)
            {
                throw new AggregateException(message, errors);
            }
        }
    }
}
