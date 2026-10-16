using System;
using System.Collections.Generic;
using System.Linq;
using RevitTestHost.Core.Reporting;

namespace RevitTestHost.Core.Execution
{
    /// <summary>
    /// Конвейер прогона без Revit-типов: фильтр → группы по модели (сначала тесты без модели) →
    /// получение документа группы один раз → выполнение. Документ — параметр типа, чтобы конвейер
    /// проверялся модульными тестами; надстройка подставляет <c>Autodesk.Revit.DB.Document</c>.
    /// </summary>
    public static class RunEngine
    {
        /// <param name="tests">Найденные тесты.</param>
        /// <param name="filter">Отбор по "Namespace.Class.Method".</param>
        /// <param name="revitVersion">Версия Revit для отчёта.</param>
        /// <param name="acquireModel">Документ для ключа модели (путь, «@new», «@active»); бросает с понятным текстом.</param>
        /// <param name="execute">Выполнение одного теста на документе группы (null — тест без модели).</param>
        public static TestRunReport Run<TTest, TDocument>(
            IEnumerable<TTest> tests,
            TestFilter filter,
            string revitVersion,
            Func<string, TDocument> acquireModel,
            Func<TTest, TDocument?, TestResult> execute)
            where TTest : ITestCase
            where TDocument : class
        {
            if (tests == null) throw new ArgumentNullException(nameof(tests));
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            if (acquireModel == null) throw new ArgumentNullException(nameof(acquireModel));
            if (execute == null) throw new ArgumentNullException(nameof(execute));

            var report = new TestRunReport { RevitVersion = revitVersion };

            var groups = tests
                .Where(t => filter.IsMatch($"{t.ClassFullName}.{t.MethodName}"))
                .GroupBy(t => t.ModelPath)
                .OrderBy(g => g.Key ?? "", StringComparer.Ordinal); // сначала тесты без модели

            foreach (var group in groups)
            {
                TDocument? document = null;
                string? acquireError = null;

                if (group.Key != null)
                {
                    try
                    {
                        document = acquireModel(group.Key);
                    }
                    catch (Exception ex)
                    {
                        acquireError = ex.Message;
                    }
                }

                foreach (var test in group)
                {
                    report.Results.Add(acquireError != null
                        ? new TestResult(test.ClassFullName, test.MethodName, TestStatus.Error, 0,
                            $"Не удалось открыть модель теста '{group.Key}': {acquireError}", model: group.Key)
                        : execute(test, document));
                }
            }

            return report;
        }

        /// <summary>
        /// Источник документов с поддержкой «@active»: для него возвращает <paramref name="activeDocument"/>
        /// или бросает <see cref="InvalidOperationException"/> с <see cref="ModelKeys.NoActiveDocumentMessage"/>;
        /// прочие ключи передаёт в <paramref name="openModel"/>.
        /// </summary>
        public static Func<string, TDocument> WithActiveDocument<TDocument>(TDocument? activeDocument, Func<string, TDocument> openModel)
            where TDocument : class
        {
            if (openModel == null) throw new ArgumentNullException(nameof(openModel));
            return model =>
            {
                if (model != ModelKeys.ActiveDocument) return openModel(model);
                return activeDocument ?? throw new InvalidOperationException(ModelKeys.NoActiveDocumentMessage);
            };
        }
    }
}
