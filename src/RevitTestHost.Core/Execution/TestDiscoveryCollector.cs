using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RevitTestHost.Core.Protocol;
using RevitTestHost.Core.Reporting;

namespace RevitTestHost.Core.Execution
{
    /// <summary>
    /// Правила обнаружения тестов, при которых сломанные посторонние типы не роняют весь прогон.
    /// Отражение (сборки, типы, методы) — в надстройке; здесь решения по каждому шагу:
    /// <list type="bullet">
    /// <item>тип, ни один тест которого не пройдёт фильтр (<see cref="TestFilter.CouldMatchType"/>), не открывается вовсе;</item>
    /// <item>сбой осмотра такого типа, который мог пройти фильтр, — результат <c>error</c> с методом <see cref="DiscoveryMethodName"/>;</item>
    /// <item>то, что нельзя сопоставить с фильтром (сборка не загрузилась, типы не загрузились), пропускается
    /// с записью в <see cref="Warnings"/>, а не молча.</item>
    /// </list>
    /// </summary>
    public sealed class TestDiscoveryCollector<TTest> where TTest : ITestCase
    {
        /// <summary>Имя «метода» в результате <c>error</c> для типа, который не удалось осмотреть.</summary>
        public const string DiscoveryMethodName = "(discovery)";

        private readonly TestFilter _filter;

        public TestDiscoveryCollector(TestFilter filter)
        {
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        }

        /// <summary>Найденные тесты, прошедшие фильтр.</summary>
        public List<TTest> Tests { get; } = new List<TTest>();

        /// <summary>Типы, подходившие под фильтр, но не осмотренные из-за исключения.</summary>
        public List<TestResult> Errors { get; } = new List<TestResult>();

        /// <summary>Строки о пропущенных сборках и типах (для журнала сервера и вывода CLI).</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Сборка тестов не загрузилась: её типы неизвестны, пропускаем с предупреждением.</summary>
        public void AssemblyLoadFailed(string assemblyPath, Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            Warnings.Add($"Сборка тестов {assemblyPath} не загружена и пропущена: {ExceptionText.Describe(exception)}");
        }

        /// <summary>
        /// Типы сборки. При <see cref="ReflectionTypeLoadException"/> — те, что загрузились; о незагруженных
        /// (их имена неизвестны, фильтр к ним не применить) — одна строка на каждую причину.
        /// Другое исключение — сборка пропускается с предупреждением.
        /// </summary>
        public IReadOnlyList<Type> LoadTypes(string assemblyName, Func<Type?[]> getTypes)
        {
            if (getTypes == null) throw new ArgumentNullException(nameof(getTypes));
            try
            {
                return getTypes().Where(t => t != null).Select(t => t!).ToList();
            }
            catch (ReflectionTypeLoadException ex)
            {
                var loaded = (ex.Types ?? new Type?[0]).Where(t => t != null).Select(t => t!).ToList();
                var missing = (ex.Types ?? new Type?[0]).Count(t => t == null);
                var reasons = (ex.LoaderExceptions ?? new Exception[0])
                    .Where(e => e != null)
                    .Select(e => ExceptionText.Describe(e!))
                    .Distinct()
                    .ToList();

                if (reasons.Count == 0)
                {
                    Warnings.Add($"Сборка тестов {assemblyName}: {missing} тип(ов) не загружено и пропущено: {ExceptionText.Describe(ex)}");
                }
                else
                {
                    foreach (var reason in reasons)
                        Warnings.Add($"Сборка тестов {assemblyName}: {missing} тип(ов) не загружено и пропущено: {reason}");
                }
                return loaded;
            }
            catch (Exception ex)
            {
                Warnings.Add($"Сборка тестов {assemblyName}: не удалось получить типы, сборка пропущена: {ExceptionText.Describe(ex)}");
                return new Type[0];
            }
        }

        /// <summary>
        /// Осматривает тип, если хоть один его тест может пройти фильтр. <paramref name="inspect"/> возвращает тесты
        /// типа; из них берутся прошедшие фильтр. Исключение осмотра — результат <c>error</c> для типа, частично
        /// найденные тесты этого типа отбрасываются.
        /// </summary>
        public void InspectType(string typeFullName, Func<IEnumerable<TTest>> inspect)
        {
            if (typeFullName == null) throw new ArgumentNullException(nameof(typeFullName));
            if (inspect == null) throw new ArgumentNullException(nameof(inspect));
            if (!_filter.CouldMatchType(typeFullName)) return;

            List<TTest> found;
            try
            {
                found = inspect().ToList();
            }
            catch (Exception ex)
            {
                Errors.Add(new TestResult(typeFullName, DiscoveryMethodName, TestStatus.Error, 0,
                    ExceptionText.Describe(ex)));
                return;
            }

            Tests.AddRange(found.Where(t => _filter.IsMatch($"{t.ClassFullName}.{t.MethodName}")));
        }
    }
}
