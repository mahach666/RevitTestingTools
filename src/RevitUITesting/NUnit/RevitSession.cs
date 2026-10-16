using System;
using RevitUITesting.Revit;

namespace RevitUITesting.NUnit
{
    /// <summary>
    /// Revit один на прогон: запускается первым классом тестов (<see cref="Acquire"/>), закрывается в [SetUpFixture] потребителя
    /// (<see cref="End"/>). Без него Revit, запущенный тестами, завершается в AppDomain.ProcessExit принудительно (Kill). Revit умер —
    /// следующий класс тестов запускает новый.
    /// </summary>
    public static class RevitSession
    {
        private static readonly object Sync = new object();
        private static SessionHolder<RevitApp> _holder;
        private static bool _exitHooked;

        /// <summary>Конфигурация первого класса тестов прогона (null — сеанса ещё не было).</summary>
        public static RevitUiConfig Config { get; private set; }

        /// <summary>Текущий Revit сеанса; null — не запущен или закрыт.</summary>
        public static RevitApp Current => _holder?.Current;

        /// <summary>Модель последнего класса тестов (её закрывает следующий класс после открытия своей).</summary>
        internal static HostDocument LastModel { get; set; }

        public static RevitApp Acquire(RevitUiConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            lock (Sync)
            {
                if (Config == null)
                {
                    Config = config;
                }
                if (_holder == null)
                {
                    _holder = new SessionHolder<RevitApp>(StartOwned, app => app.IsAlive, app => app.Dispose());
                }
                if (!_exitHooked)
                {
                    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                    _exitHooked = true;
                }
                RevitApp app = _holder.Acquire(out bool started);
                if (started)
                {
                    LastModel = null;
                }
                return app;
            }
        }

        /// <summary>
        /// Перезапустить Revit сеанса: <see cref="RevitApp.Quit"/> текущего (закрывается и подхваченный, и оставляемый keepRevitOpen),
        /// его освобождение, затем запуск или подхват нового по <see cref="Config"/> — всё под замком сеанса. Новый объект становится
        /// <see cref="Current"/>, его закрывают <see cref="End"/> и выход процесса тестов. <see cref="RevitUiTestBase"/> читает App из сеанса,
        /// поэтому App после перезапуска — уже новый; модель класса (Model) заново не открывается. Сеанса нет — InvalidOperationException;
        /// сбой закрытия или запуска — исключение, сеанса нет (следующий класс тестов запустит Revit заново).
        /// </summary>
        public static RevitApp Restart()
        {
            lock (Sync)
            {
                if (_holder?.Current == null)
                {
                    throw new InvalidOperationException("Сеанса Revit нет: перезапускать нечего — Revit сеанса запускает RevitSession.Acquire (базовый класс — в [OneTimeSetUp]).");
                }
                LastModel = null;
                return _holder.Replace(app =>
                {
                    UiLog.Action($"перезапуск Revit сеанса (pid {app.ProcessId})");
                    app.Quit();
                });
            }
        }

        /// <summary>Закрыть Revit сеанса (по правилам <see cref="RevitApp.Dispose"/>: запущенный тестами и без keepRevitOpen). Без сеанса — ничего.</summary>
        public static void End()
        {
            lock (Sync)
            {
                LastModel = null;
                _holder?.End();
            }
        }

        /// <summary>Запуск или подхват Revit сеанса; объект помечается как принадлежащий сеансу (его <see cref="RevitApp.Restart"/> отказывает).</summary>
        private static RevitApp StartOwned()
        {
            RevitApp app = RevitApp.StartOrAttach(Config);
            app.OwnedBySession = true;
            return app;
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            RevitApp app = _holder?.Current;
            if (app == null || !app.StartedByUs || app.Config.KeepRevitOpen || !app.IsAlive)
            {
                return;
            }
            try
            {
                app.Kill("процесс тестов завершается, а RevitSession.End() не вызван — добавьте [SetUpFixture] с RevitSession.End(), тогда Revit закроется штатно");
            }
            catch (Exception ex)
            {
                // исключение из ProcessExit некуда передать: оно пишется в поток ошибок процесса тестов. Kill идёт до записи в журнал,
                // поэтому сбой приёмника журнала (TestContext при выходе процесса) не оставляет Revit запущенным
                string state = app.Process.HasExited ? "Revit завершён, но причина не записана в журнал шагов" : "Revit не завершён";
                Console.Error.WriteLine($"RevitSession: при выходе процесса тестов {state}: " + ex);
            }
        }
    }
}
