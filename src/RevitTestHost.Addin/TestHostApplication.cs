using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitTestHost.Addin.Execution;
using RevitTestHost.Addin.Reporting;
using RevitTestHost.Addin.Serve;
using RevitTestHost.Core.Json;
using RevitTestHost.Core.Protocol;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Addin
{
    public class TestHostApplication : IExternalApplication
    {
        /// <summary>"1" — headless-режим: прогнать тесты после инициализации Revit и выйти.</summary>
        public const string CliModeVariable = HostModeSelector.CliVariable;

        /// <summary>Шаблоны тестов через ';' с '*', матч по "Namespace.Class.Method".</summary>
        public const string CliFilterVariable = "REVITTESTHOST_FILTER";

        /// <summary>Куда положить JSON-отчёт (полный путь файла).</summary>
        public const string CliOutputVariable = "REVITTESTHOST_OUTPUT";

        /// <summary>Имя канала режима сервера: Revit остаётся открытым и принимает команды через named pipe.</summary>
        public const string ServeVariable = HostModeSelector.ServeVariable;

        private string? _servePipeName;
        private ServeHost? _serve;

        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "TestHost";
            const string panelName = "Test Runner";

            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // Tab already exists — fine.
            }

            var panel = application.CreateRibbonPanel(tabName, panelName);
            var assemblyPath = Assembly.GetExecutingAssembly().Location;

            var runAllButton = new PushButtonData(
                name: "RunAllTests",
                text: "Run All\nTests",
                assemblyName: assemblyPath,
                className: "RevitTestHost.Addin.Commands.RunAllTestsCommand"
            );
            panel.AddItem(runAllButton);

            var mode = HostModeSelector.Select(
                Environment.GetEnvironmentVariable(CliModeVariable),
                Environment.GetEnvironmentVariable(ServeVariable));

            if (mode.Error != null)
                ReportStartupError(application, mode.Error);

            if (mode.Mode == HostMode.Cli)
            {
                // Headless-прогон: запускаемся когда Revit полностью инициализирован
                application.ControlledApplication.ApplicationInitialized += OnApplicationInitializedRunCli;
            }
            else if (mode.Mode == HostMode.Serve)
            {
                // Idling подписываем здесь: в OnStartup подписка гарантированно разрешена; сервер — после инициализации
                _servePipeName = mode.PipeName;
                application.ControlledApplication.ApplicationInitialized += OnApplicationInitializedStartServer;
                application.Idling += OnIdling;
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            if (_servePipeName != null)
            {
                application.Idling -= OnIdling;
                _serve?.Dispose();
                _serve = null;
            }
            return Result.Succeeded;
        }

        private void OnApplicationInitializedStartServer(object? sender, ApplicationInitializedEventArgs e)
        {
            var app = (Autodesk.Revit.ApplicationServices.Application)sender!;
            try
            {
                _serve = ServeHost.Start(_servePipeName!, app.VersionNumber);
            }
            catch (Exception ex)
            {
                // Revit остаётся рабочим; клиент увидит, что канал не появился, причина — в журналах
                var text = $"Не удалось запустить режим сервера (канал '{_servePipeName}'): {ExceptionText.Describe(ex)}";
                app.WriteJournalComment("RevitTestHost: " + text, true);
                StartupLog().Error(text, ex);
            }
        }

        private void OnIdling(object? sender, IdlingEventArgs e)
        {
            var serve = _serve;
            if (serve == null) return;

            // ServeHost.OnIdling читает IdleObservationPending ДО RequestQueue.OnIdling (тот сбрасывает признак).
            // SetRaiseWithoutDelay — только пока наблюдение простоя ещё нужно (после завершённого запроса):
            // Revit действует так лишь на следующий вызов, поэтому режим не остаётся включённым постоянно
            // и не держит CPU, когда запросов нет.
            if (serve.OnIdling())
                e.SetRaiseWithoutDelay();
        }

        /// <summary>
        /// Ошибка выбора режима: в журнал Revit (Journals) и в <c>%TEMP%\RevitTestHost\serve\startup.log</c>
        /// (temp пользователя, см. <see cref="ServeWorkspace.StartupLogPath"/>).
        /// Если startup.log создать не удалось, причина уходит в журнал Revit, а запуск продолжается (CLI-прогон не должен падать).
        /// </summary>
        private static void ReportStartupError(UIControlledApplication application, string error)
        {
            application.ControlledApplication.WriteJournalComment("RevitTestHost: " + error, true);
            try
            {
                StartupLog().Error(error);
            }
            catch (Exception ex)
            {
                application.ControlledApplication.WriteJournalComment(
                    "RevitTestHost: не удалось записать startup.log режима сервера: " + ExceptionText.Describe(ex), true);
            }
        }

        private static FileServerLog StartupLog() => new FileServerLog(ServeWorkspace.StartupLogPath);

        private void OnApplicationInitializedRunCli(object? sender, ApplicationInitializedEventArgs e)
        {
            var output = Environment.GetEnvironmentVariable(CliOutputVariable);
            try
            {
                var app = (Autodesk.Revit.ApplicationServices.Application)sender!;
                var uiApp = new UIApplication(app);
                var filter = Environment.GetEnvironmentVariable(CliFilterVariable);

                var report = TestRunOrchestrator.Run(uiApp, filter);
                var jsonPath = JsonReporter.Write(report);

                if (!string.IsNullOrWhiteSpace(output))
                {
                    var dir = Path.GetDirectoryName(output);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Copy(jsonPath, output, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                // Отчёт не состоялся — оставляем CLI маркер фатальной ошибки вместо результатов
                try
                {
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        var dir = Path.GetDirectoryName(output);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllText(output, new JsonWriter().BeginObject().Property("error", ex.ToString()).EndObject().ToString());
                    }
                }
                catch
                {
                }
            }
            finally
            {
                // Штатного API для выхода из Revit нет — завершаем процесс.
                // Отчёт уже записан; несохранённых пользовательских данных в headless-прогоне нет.
                Process.GetCurrentProcess().Kill();
            }
        }
    }
}
