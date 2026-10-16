using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTestHost.Addin.Discovery;
using RevitTestHost.Core.Execution;
using RevitTestHost.Core.Reporting;

namespace RevitTestHost.Addin.Execution
{
    /// <summary>
    /// Общий конвейер прогона: discovery с фильтром → группировка по моделям → выполнение.
    /// Используется кнопкой в ленте, headless CLI-режимом и командой run режима сервера.
    /// Логика без Revit-типов — в <see cref="RunEngine"/> (RevitTestHost.Core).
    /// </summary>
    public static class TestRunOrchestrator
    {
        /// <summary>
        /// Выполняет тесты, отобранные фильтром, без параметров; «@active» — документ, активный в UI (если есть).
        /// </summary>
        /// <param name="uiApp">Живой Revit.</param>
        /// <param name="filter">Шаблоны через ';' с поддержкой '*', матчатся по "Namespace.Class.Method"
        /// без учёта регистра (например "*XYZ*;MyPlugin.RevitTests.Documents.*").
        /// Null/пусто — все тесты.</param>
        public static TestRunReport Run(UIApplication uiApp, string? filter = null)
        {
            return Run(uiApp, filter, RunParameters.Empty, uiApp.ActiveUIDocument?.Document);
        }

        /// <summary>
        /// Выполняет тесты, отобранные фильтром.
        /// </summary>
        /// <param name="uiApp">Живой Revit.</param>
        /// <param name="filter">Шаблоны тестов (см. <see cref="TestFilter"/>).</param>
        /// <param name="parameters">Значение <c>ITestContext.Parameters</c> для всех тестов прогона.</param>
        /// <param name="activeDocument">Документ для тестов с Model = "@active"; null — такие тесты получают статус error.
        /// Хост его не закрывает.</param>
        public static TestRunReport Run(UIApplication uiApp, string? filter,
            IReadOnlyDictionary<string, string> parameters, Document? activeDocument)
        {
            var testFilter = TestFilter.Parse(filter);
            var discovery = TestDiscoverer.DiscoverAll(testFilter);

            TestRunReport report;
            using (var modelManager = new ModelManager(uiApp))
            {
                report = RunEngine.Run<TestDescriptor, Document>(
                    discovery.Tests,
                    testFilter,
                    uiApp.Application.VersionNumber,
                    RunEngine.WithActiveDocument(activeDocument, modelManager.GetOrOpen),
                    (descriptor, document) => TestRunner.Run(descriptor, uiApp, document, parameters));
            }

            // Подходящие под фильтр типы, которые не удалось осмотреть, — результаты error в начале отчёта
            report.Results.InsertRange(0, discovery.Errors);
            report.Warnings.AddRange(discovery.Warnings);
            return report;
        }
    }
}
