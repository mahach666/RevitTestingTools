using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using RevitUITesting.NUnit;
using RevitUITesting.Uia;

namespace RevitUITesting.Revit
{
    /// <summary>
    /// Сеанс Revit: запуск (или подхват по session.json) в режиме сервера RevitTestHost, готовность по главному окну и ответу ping,
    /// канал <see cref="Host"/>, окна <see cref="Windows"/>, закрытие документа и Revit через UI. Dispose закрывает Revit, только если он
    /// запущен этим сеансом и keepRevitOpen = false.
    /// </summary>
    public sealed class RevitApp : IDisposable
    {
        public const string ServeVariable = "REVITTESTHOST_SERVE";

        public const string CliVariable = "REVITTESTHOST_CLI";

        /// <summary>Таймаут ping и documents внутри цикла закрытия документа: занятый Revit не задерживает ответ на вопрос о сохранении.</summary>
        private static readonly TimeSpan CloseProbeTimeout = TimeSpan.FromSeconds(5);

        /// <summary>Сколько сбоев закрытия через UI подряд (не «занят» и не «окно исчезло») терпит <see cref="Quit"/> до немедленного Kill.</summary>
        private const int QuitFailureLimit = 3;

        /// <summary>Слова строки журнала <see cref="Kill"/> после pid («Revit pid N завершён принудительно: причина») — по ним контрактные тесты ищут Kill.</summary>
        internal const string KilledText = "завершён принудительно";

        private readonly bool _startedByUs;
        private UIA3Automation _automation;
        private IntPtr _mainHandle;
        private bool _disposed;

        private RevitApp(RevitUiConfig config, Process process, string pipeName, bool startedByUs)
        {
            Config = config;
            Process = process;
            PipeName = pipeName;
            _startedByUs = startedByUs;
            _automation = new UIA3Automation();
            Windows = new RevitWindows(_automation, process.Id, MainWindowHandle, RevitDialogs.Catalog(config.KnownDialogs));
            Host = new RevitHostClient(pipeName, config.HostTimeout, config.ModelOpenTimeout, Windows.DescribeOnce, EnsureAlive);
            Panes = new RevitPanes(this);
            Ribbon = new RevitRibbon(this);
        }

        public RevitUiConfig Config { get; }

        public Process Process { get; }

        public int ProcessId => Process.Id;

        /// <summary>Имя канала RevitTestHost (REVITTESTHOST_SERVE).</summary>
        public string PipeName { get; }

        /// <summary>Revit запущен этим сеансом (а не подхвачен).</summary>
        public bool StartedByUs => _startedByUs;

        public bool IsAlive => !Process.HasExited;

        /// <summary>Объект принадлежит <see cref="RevitSession"/>: закрывает его сеанс, перезапуск — только <see cref="RevitSession.Restart"/>.</summary>
        internal bool OwnedBySession { get; set; }

        public AutomationBase Automation => _automation;

        public RevitHostClient Host { get; }

        public RevitWindows Windows { get; }

        /// <summary>Панели Revit (DockablePane) по заголовку.</summary>
        public RevitPanes Panes { get; }

        /// <summary>Лента Revit (AdWindows).</summary>
        public RevitRibbon Ribbon { get; }

        /// <summary>Главное окно Revit (ждёт не дольше <see cref="Wait.DefaultTimeout"/>; Revit завершён — <see cref="RevitExitedException"/>).</summary>
        public Window MainWindow => Wait.ForStrict(() =>
        {
            EnsureAlive();
            return TryMainWindow()?.AsWindow();
        }, Wait.DefaultTimeout, "главное окно Revit", Windows.DescribeOnce);

        /// <summary>
        /// Подхватить Revit, запущенный RevitUITesting (attachToRunning и совпадение с session.json), или запустить новый и дождаться готовности.
        /// Осведомлённость о DPI включается здесь, до первого обращения к UI Automation; <see cref="Wait.DefaultTimeout"/> берётся из конфигурации.
        /// Во время ожидания готовности TaskDialog каталога с действием <see cref="KnownDialogAction.TaskDialogButton"/> отвечаются
        /// (диалог «Security - Unsigned Add-In» — «Always Load»: WM_CLOSE равносилен «Do Not Load»); trustAddins — первая линия защиты,
        /// но диалог подавляет не всегда. Окно каталога с действием <see cref="KnownDialogAction.Fail"/> — немедленный отказ.
        /// </summary>
        public static RevitApp StartOrAttach(RevitUiConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (config.DpiAware)
            {
                UiLog.Action(ProcessDpi.Apply());
            }
            Wait.DefaultTimeout = config.DefaultTimeout;
            string sessionPath = RevitSessionFile.DefaultPath;
            List<Process> running = FindRunning();
            Process process = null;
            RevitSessionFile session;
            try
            {
                session = running.Count > 0 ? RevitSessionFile.Read(sessionPath) : null;
                int? attachPid = AttachDecision.Choose(running.Select(p => new RunningRevit(p.Id, p.StartTime.ToUniversalTime())).ToList(), session, sessionPath, config.RevitVersion, config.AttachToRunning);
                process = attachPid == null ? null : running.First(p => p.Id == attachPid.Value);
            }
            finally
            {
                // объекты Process остальных экземпляров Revit держат дескрипторы процессов: нужен только подхватываемый
                foreach (Process other in running.Where(p => !ReferenceEquals(p, process)))
                {
                    other.Dispose();
                }
            }
            if (process == null)
            {
                return Launch(config, sessionPath);
            }
            UiLog.Action($"подхват Revit pid {process.Id}, канал {session.Pipe}");
            RevitApp attached = new RevitApp(config, process, session.Pipe, false);
            try
            {
                attached.WaitReady(config.HostTimeout);
            }
            catch (Exception ex)
            {
                DisposeAfterFailedStart(attached, ex);
                throw;
            }
            return attached;
        }

        public IReadOnlyList<AutomationElement> TopLevelWindows()
        {
            return UiQuery.Read(() => _automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(Process.Id)), Wait.DefaultTimeout, "окна процесса Revit");
        }

        /// <summary>Процесс Revit жив; иначе <see cref="RevitExitedException"/> с кодом выхода и хвостом журнала Revit.</summary>
        public void EnsureAlive()
        {
            if (!Process.HasExited)
            {
                return;
            }
            string tail;
            try
            {
                tail = RevitJournal.Tail(RevitJournal.DirectoryFor(Config.RevitVersion), 40);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                tail = "журнал Revit не прочитан: " + ex.Message;
            }
            throw new RevitExitedException(Process.Id, TryExitCode(), tail);
        }

        /// <summary>Вывести главное окно Revit на передний план (нужно перед вводом с клавиатуры).</summary>
        public void BringToFront()
        {
            UiAct.BringToFront(MainWindow);
        }

        /// <summary>
        /// Нажать клавиши (одновременно) в Revit; ввод уходит, только если переднее окно принадлежит процессу Revit. Клавиши — виртуальные коды:
        /// сочетания клавиш Revit (например, PP) сопоставляются по набранным символам и зависят от активного языка ввода (задача 12: при раскладке
        /// «РУС» два VK_P панель Properties не скрыли) — для команд Revit надёжнее лента или элементы окна.
        /// </summary>
        public void SendKeys(string description, params VirtualKeyShort[] keys)
        {
            BringToFront();
            UiAct.KeysToForeground(Process.Id, description, keys);
        }

        /// <summary>
        /// Закрыть активный документ через UI без сохранения: Ctrl+F4 по его видам, на вопрос о сохранении — «No» (каталог диалогов),
        /// пока документа нет в Host.Documents(). Revit API активный документ не закрывает (ACTIVE_DOCUMENT), поэтому только так.
        /// </summary>
        public void CloseActiveDocument()
        {
            EnsureAlive();
            HostDocument active = Host.Ping().ActiveDocument ?? throw new InvalidOperationException("В Revit нет активного документа — закрывать нечего.");
            string title = active.Title;
            UiLog.Action($"закрытие активного документа «{title}» через UI: Ctrl+F4 по видам, на вопрос о сохранении — «No» (TDM_CLICK_BUTTON {RevitIds.Dialogs.TaskDialogNoButtonId})");
            TimeSpan limit = Config.ModelOpenTimeout;
            Stopwatch sw = Stopwatch.StartNew();
            TimeSpan? lastKeys = null;
            int presses = 0;
            while (sw.Elapsed < limit)
            {
                EnsureAlive();
                if (Windows.AnswerKnown(RevitDialogs.SaveChanges))
                {
                    continue;
                }
                if (!Windows.IsMainWindowEnabled)
                {
                    // модальное окно ещё строится или это не вопрос о сохранении: ждём до общего таймаута, затем ошибка с перечнем окон
                    Thread.Sleep(Wait.PollInterval);
                    continue;
                }
                HostPing ping;
                IReadOnlyList<HostDocument> documents;
                try
                {
                    ping = Host.Ping(CloseProbeTimeout);
                    documents = Host.Documents(CloseProbeTimeout);
                }
                catch (RevitBusyException ex)
                {
                    // Revit занят закрытием вида или показал вопрос — следующий круг проверит окна и повторит запросы
                    UiLog.Action("Revit занят при закрытии документа: " + ex.Command);
                    continue;
                }
                if (documents.All(d => !string.Equals(d.Title, title, StringComparison.OrdinalIgnoreCase)))
                {
                    UiLog.Action($"документ «{title}» закрыт");
                    return;
                }
                if (ping.ActiveDocument == null || !string.Equals(ping.ActiveDocument.Title, title, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Активным стал документ «{ping.ActiveDocument?.Title}», а «{title}» ещё открыт — закрытие через UI прервано.");
                }
                if (lastKeys == null || sw.Elapsed - lastKeys.Value > TimeSpan.FromSeconds(3))
                {
                    if (++presses > 50)
                    {
                        break;
                    }
                    SendKeys($"Ctrl+F4: закрыть активный вид документа «{title}»", VirtualKeyShort.CONTROL, VirtualKeyShort.F4);
                    lastKeys = sw.Elapsed;
                }
                Thread.Sleep(Wait.PollInterval);
            }
            throw new UiWaitTimeoutException($"закрытие документа «{title}» через UI (нажатий Ctrl+F4: {presses})", limit, sw.Elapsed, Windows.DescribeOnce(), null);
        }

        /// <summary>
        /// Закрыть Revit: WM_CLOSE главному окну, пока оно есть и доступно, — повтор каждые 5 с (с открытым документом Revit может пропустить
        /// первый WM_CLOSE); на вопросы о сохранении — «No»; не завершился за revitExitTimeoutSeconds — Kill с перечнем окон в журнале.
        /// Неизвестное модальное окно не закрывается (главное окно недоступно, WM_CLOSE не шлётся) — тогда тоже Kill. Сбой закрытия через UI
        /// (не «занят» и не «окно исчезло») пишется в журнал и не оставляет Revit запущенным: закрытие продолжается до срока, а после
        /// <see cref="QuitFailureLimit"/> сбоев подряд — Kill сразу.
        /// </summary>
        public void Quit()
        {
            if (Process.HasExited)
            {
                UiLog.Action($"Revit pid {Process.Id} уже завершён");
                RevitSessionFile.Delete(RevitSessionFile.DefaultPath, Process.Id);
                return;
            }
            TimeSpan limit = Config.RevitExitTimeout;
            UiLog.Action($"закрытие Revit pid {Process.Id}: WM_CLOSE главному окну (повтор каждые {CloseResend.Interval.TotalSeconds:0} с), на вопросы о сохранении — «No»; через {limit.TotalSeconds:0} с — Kill");
            Host.Dispose();
            Stopwatch sw = Stopwatch.StartNew();
            TimeSpan? lastClose = null;
            int closes = 0;
            int failures = 0;
            string stopped = null;
            while (!Process.WaitForExit(0) && sw.Elapsed < limit)
            {
                try
                {
                    QuitRound(sw.Elapsed, ref lastClose, ref closes);
                    failures = 0;
                }
                catch (AppBusyException ex)
                {
                    UiLog.Warn("Revit занят при закрытии: " + ex.Message);
                }
                catch (Exception ex) when (UiFault.Classify(ex) == UiFaultKind.Gone)
                {
                    UiLog.Action("окна Revit исчезают — процесс завершается: " + ex.GetType().Name);
                }
                catch (Exception ex)
                {
                    // любой другой сбой закрытия через UI не должен оставить запущенный Revit: он пишется в журнал, закрытие продолжается
                    // до срока и заканчивается Kill; несколько сбоев подряд — закрыть через UI не получается, Kill сразу
                    failures++;
                    UiLog.Warn($"сбой при закрытии Revit pid {Process.Id} ({failures}-й подряд): {ExceptionText(ex)}");
                    if (failures >= QuitFailureLimit)
                    {
                        stopped = $"{failures} сбоя подряд при закрытии через UI, последний — {ExceptionText(ex)}";
                        break;
                    }
                }
                Thread.Sleep(500);
            }
            if (!Process.HasExited)
            {
                // Kill пишет причину в журнал предупреждением — с перечнем окон, которые помешали закрытию
                string reason = stopped ?? $"не завершился за {limit.TotalSeconds:0} с (WM_CLOSE принят {closes} раз)";
                Kill(reason + "; " + DescribeWindowsForKill());
            }
            RevitSessionFile.Delete(RevitSessionFile.DefaultPath, Process.Id);
        }

        /// <summary>
        /// Закрыть Revit (<see cref="Quit"/>), запустить заново и дождаться готовности. Этот объект освобождается, новый принадлежит вызывающему.
        /// Не для Revit сеанса (<see cref="RevitSession"/>, App базового класса NUnit) — там InvalidOperationException: перезапущенный в обход
        /// сеанса Revit остался бы без владельца и не закрылся бы в конце прогона; используйте <see cref="RevitSession.Restart"/>.
        /// </summary>
        public RevitApp Restart()
        {
            if (OwnedBySession)
            {
                throw new InvalidOperationException("Этот Revit принадлежит RevitSession (App базового класса): перезапускайте его через RevitSession.Restart() — "
                    + "новый Revit станет объектом сеанса и закроется в RevitSession.End(), а App базового класса после перезапуска читается из сеанса.");
            }
            UiLog.Action("перезапуск Revit");
            Quit();
            RevitUiConfig config = Config;
            Dispose();
            return StartOrAttach(config);
        }

        /// <summary>
        /// Завершить процесс Revit принудительно, затем записать причину в журнал. Kill идёт до записи: сбой приёмника журнала
        /// (например, при выходе процесса тестов) не оставляет Revit запущенным, а пробрасывается уже после завершения процесса.
        /// </summary>
        public void Kill(string reason)
        {
            if (Process.HasExited)
            {
                return;
            }
            Process.Kill();
            if (!Process.WaitForExit(30000))
            {
                throw new InvalidOperationException($"Revit pid {Process.Id} не завершился и через 30 с после Kill (причина Kill: {reason}).");
            }
            RevitSessionFile.Delete(RevitSessionFile.DefaultPath, Process.Id);
            UiLog.Warn($"Revit pid {Process.Id} {KilledText}: {reason}");
        }

        /// <summary>Снимок всего экрана в папку артефактов; возвращает путь.</summary>
        public string Screenshot(string name)
        {
            string directory = Config.ResolvedArtifactsDir;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{TestArtifacts.Sanitize(name ?? "screen", 60)}.png");
            Capture.MainScreen().ToFile(path);
            UiLog.Action("снимок экрана: " + path);
            return path;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try
            {
                if (!Process.HasExited)
                {
                    if (_startedByUs && !Config.KeepRevitOpen)
                    {
                        Quit();
                    }
                    else
                    {
                        UiLog.Action($"Revit pid {Process.Id} оставлен открытым ({(_startedByUs ? "keepRevitOpen" : "подхвачен, а не запущен тестами")}); следующий прогон подхватит его при attachToRunning = true");
                    }
                }
            }
            finally
            {
                if (WaitDiagnostics.Provider == (Func<string>)DescribeForDiagnostics)
                {
                    WaitDiagnostics.Provider = null;
                }
                Host.Dispose();
                _automation?.Dispose();
                _automation = null;
            }
        }

        internal IntPtr MainWindowHandle()
        {
            if (_mainHandle != IntPtr.Zero && RevitNative.IsWindow(_mainHandle))
            {
                return _mainHandle;
            }
            AutomationElement main = TryMainWindow();
            _mainHandle = main == null ? IntPtr.Zero : UiQuery.Read(() => main.Properties.NativeWindowHandle.ValueOrDefault, Wait.DefaultTimeout, "HWND главного окна Revit");
            return _mainHandle;
        }

        /// <summary>Главное окно: окно процесса без владельца, заголовок начинается с «Autodesk Revit &lt;версия&gt;»; нет — null.</summary>
        internal AutomationElement TryMainWindow()
        {
            string prefix = RevitTitles.Windows.MainPrefix + Config.RevitVersion;
            List<AutomationElement> mains = new List<AutomationElement>();
            foreach (AutomationElement window in TopLevelWindows())
            {
                string name;
                IntPtr handle;
                try
                {
                    name = UiQuery.Read(() => window.Properties.Name.ValueOrDefault ?? "", Wait.DefaultTimeout, "заголовок окна Revit");
                    handle = UiQuery.Read(() => window.Properties.NativeWindowHandle.ValueOrDefault, Wait.DefaultTimeout, "HWND окна Revit");
                }
                catch (Exception ex) when (UiFault.Classify(ex) == UiFaultKind.Gone)
                {
                    // окно закрылось во время перечня — это не главное окно
                    continue;
                }
                if (handle != IntPtr.Zero && name.StartsWith(prefix, StringComparison.Ordinal) && RevitNative.GetOwner(handle) == IntPtr.Zero)
                {
                    mains.Add(window);
                }
            }
            if (mains.Count > 1)
            {
                throw new AmbiguousElementException("главное окно Revit", mains.Select(m => m.Describe()).ToList());
            }
            return mains.FirstOrDefault();
        }

        private static List<Process> FindRunning()
        {
            int session;
            using (Process current = Process.GetCurrentProcess())
            {
                session = current.SessionId;
            }
            List<Process> all = Process.GetProcessesByName("Revit").ToList();
            List<Process> ours = all.Where(p => p.SessionId == session).ToList();
            // Revit других сессий Windows не рассматривается: их объекты Process сразу освобождаются
            foreach (Process other in all.Except(ours))
            {
                other.Dispose();
            }
            return ours;
        }

        private static RevitApp Launch(RevitUiConfig config, string sessionPath)
        {
            string exe = config.ResolvedRevitExe;
            if (!File.Exists(exe))
            {
                throw new FileNotFoundException($"Не найден Revit.exe (revitVersion = {config.RevitVersion}, revitExe = {config.RevitExe ?? "не задан"}).", exe);
            }
            CommitCheck.Default.Require(config.MinFreeCommitGb);
            foreach (string line in AddinTrust.Apply(config.RevitVersion, config.TrustAddins))
            {
                UiLog.Action(line);
            }
            string pipe = "RevitTestHost-" + Guid.NewGuid().ToString("N");
            ProcessStartInfo info = RevitLaunch.StartInfo(exe, config.Language, pipe);
            UiLog.Action($"запуск {exe} {info.Arguments} с {ServeVariable}={pipe}");
            // не Process.Start: Revit не должен наследовать дескрипторы процесса тестов (каналы stdout/stderr вызывающего)
            Process process = RevitLaunch.Start(info);
            new RevitSessionFile(process.Id, pipe, config.RevitVersion, process.StartTime.ToUniversalTime()).Write(sessionPath);
            RevitApp app = new RevitApp(config, process, pipe, true);
            try
            {
                app.WaitReady(config.RevitStartTimeout);
            }
            catch (Exception ex)
            {
                DisposeAfterFailedStart(app, ex);
                throw;
            }
            return app;
        }

        /// <summary>
        /// Готовность: главное окно, затем ответ ping с pid этого процесса. На каждом круге обеих фаз — TaskDialog каталога с действием
        /// TaskDialogButton отвечаются (ответы пишутся в журнал шагов; «Security - Unsigned Add-In» появляется до главного окна), затем
        /// окно каталога с действием Fail — немедленный отказ.
        /// </summary>
        private void WaitReady(TimeSpan timeout)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Wait.ForStrict(() =>
            {
                EnsureAlive();
                Windows.AnswerTaskDialogs();
                FailOnKnownDialog();
                return TryMainWindow();
            }, timeout, $"главное окно Revit «{RevitTitles.Windows.MainPrefix}{Config.RevitVersion}…» (pid {Process.Id})", Windows.DescribeOnce);
            HostPing ping = null;
            Exception last = null;
            while (ping == null)
            {
                EnsureAlive();
                Windows.AnswerTaskDialogs();
                FailOnKnownDialog();
                TimeSpan left = timeout - sw.Elapsed;
                if (left <= TimeSpan.Zero)
                {
                    throw new UiWaitTimeoutException($"ответ сервера RevitTestHost на «ping» по каналу {PipeName} (развёрнута ли надстройка RevitTestHost с режимом сервера?)",
                        timeout, sw.Elapsed, Windows.DescribeOnce(), last);
                }
                try
                {
                    ping = Host.Ping(ReadyPing.AttemptTimeout(left, Config.HostTimeout));
                }
                catch (TimeoutException ex)
                {
                    // канал ещё не поднят (ApplicationInitialized не наступил) или Revit занят стартом либо модальным окном (RevitBusyException
                    // за попытку не дольше ReadyPing.MaxAttempt) — на следующем круге окна каталога отвечаются, ping повторяется до общего таймаута
                    last = ex;
                    Thread.Sleep(1000);
                }
                catch (IOException ex)
                {
                    // сервер оборвал подключение во время старта — повтор до общего таймаута
                    last = ex;
                    Thread.Sleep(1000);
                }
            }
            if (ping.Pid != Process.Id)
            {
                throw new InvalidOperationException($"Канал {PipeName} обслуживает Revit pid {ping.Pid}, а ожидался pid {Process.Id}.");
            }
            if (!ping.RevitVersion.StartsWith(Config.RevitVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Сервер RevitTestHost сообщает Revit {ping.RevitVersion}, а в конфигурации revitVersion = {Config.RevitVersion}.");
            }
            Maximize();
            WaitDiagnostics.Provider = DescribeForDiagnostics;
            UiLog.Action($"Revit готов за {sw.Elapsed.TotalSeconds:0} с: {ping}");
        }

        private void FailOnKnownDialog()
        {
            string failure = Windows.KnownFailure();
            if (failure != null)
            {
                throw new InvalidOperationException("Revit показал окно, которое тесты не закрывают: " + failure);
            }
        }

        private void Maximize()
        {
            Window main = MainWindow;
            IWindowPattern pattern = UiQuery.Read(() => main.Patterns.Window.PatternOrDefault, Wait.DefaultTimeout, "WindowPattern главного окна Revit");
            if (pattern == null)
            {
                UiLog.Warn("У главного окна Revit нет WindowPattern — окно не развёрнуто, панели ленты могут быть свёрнуты.");
                return;
            }
            if (UiQuery.Read(() => pattern.WindowVisualState.Value, Wait.DefaultTimeout, "состояние главного окна Revit") == WindowVisualState.Maximized)
            {
                return;
            }
            UiQuery.Read(() =>
            {
                pattern.SetWindowVisualState(WindowVisualState.Maximized);
                return true;
            }, Wait.DefaultTimeout, "развернуть главное окно Revit");
            UiLog.Action("главное окно Revit развёрнуто (меньше свёрнутых панелей ленты)");
        }

        /// <summary>Один круг закрытия: вопрос о сохранении — «No»; иначе WM_CLOSE главному окну, если оно доступно и подошёл срок повтора.</summary>
        private void QuitRound(TimeSpan elapsed, ref TimeSpan? lastClose, ref int closes)
        {
            if (Windows.AnswerKnown(RevitDialogs.SaveChanges))
            {
                return;
            }
            IntPtr main = MainWindowHandle();
            if (main == IntPtr.Zero || !Windows.IsMainWindowEnabled || !CloseResend.Due(lastClose, elapsed, CloseResend.Interval))
            {
                return;
            }
            lastClose = elapsed;
            if (RevitNative.PostClose(main, "главное окно Revit " + RevitNative.Hex(main)))
            {
                closes++;
                UiLog.Action(closes == 1 ? "WM_CLOSE главному окну Revit" : $"WM_CLOSE главному окну Revit повторно ({closes}-й раз): Revit ещё не закрылся");
            }
            else
            {
                UiLog.Action("главное окно Revit закрылось до WM_CLOSE — процесс завершается");
            }
        }

        /// <summary>Перечень окон для причины Kill; сбой перечня не должен помешать Kill.</summary>
        private string DescribeWindowsForKill()
        {
            try
            {
                return Windows.DescribeOnce();
            }
            catch (Exception ex)
            {
                // перечень окон — только диагностика в причине Kill: его сбой переводится в текст, Kill выполняется
                return "окна Revit не перечислены: " + ExceptionText(ex);
            }
        }

        /// <summary>
        /// Освободить сеанс после отказа запуска или подхвата (Revit, запущенный этим сеансом, при этом закрывается). Сбой уборки не подменяет
        /// исходную причину отказа: он пишется в журнал, вызывающий пробрасывает исходное исключение.
        /// </summary>
        private static void DisposeAfterFailedStart(RevitApp app, Exception startup)
        {
            try
            {
                app.Dispose();
            }
            catch (Exception cleanup)
            {
                // исходное исключение объясняет отказ запуска — его пробрасывает вызывающий; сбой уборки только в журнал
                UiLog.Warn($"Revit pid {app.ProcessId}: сбой уборки после отказа запуска ({ExceptionText(startup)}): {ExceptionText(cleanup)}");
            }
        }

        private static string ExceptionText(Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }

        private int? TryExitCode()
        {
            try
            {
                return Process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                // процесс подхвачен, и Windows не отдаёт его код выхода — в сообщении будет «код выхода неизвестен»
                return null;
            }
            catch (Win32Exception)
            {
                // нет прав на код выхода — в сообщении будет «код выхода неизвестен»
                return null;
            }
        }

        private string DescribeForDiagnostics()
        {
            return Process.HasExited ? $"Процесс Revit pid {Process.Id} завершён." : "Revit: " + Windows.DescribeOnce();
        }
    }
}
