using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RevitTestHost.Core.Execution;
using RevitTestHost.Sdk.Attributes;
using RevitTestHost.Sdk.Contracts;

namespace RevitTestHost.Addin.Discovery
{
    /// <summary>
    /// Поиск тестов в <c>Tests\*Tests.dll</c>. Правила изоляции сбоев (фильтр до осмотра типа, error-результат
    /// для сломанного подходящего типа, предупреждения о пропущенном) — в <see cref="TestDiscoveryCollector{TTest}"/>.
    /// </summary>
    public static class TestDiscoverer
    {
        public static TestDiscoveryCollector<TestDescriptor> DiscoverAll(TestFilter filter)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            var collector = new TestDiscoveryCollector<TestDescriptor>(filter);

            foreach (var asm in TestAssemblyLoader.LoadAll(collector.AssemblyLoadFailed))
            {
                var asmDir = SafeGetDirectory(asm);
                foreach (var t in collector.LoadTypes(asm.GetName().Name ?? asm.FullName!, asm.GetTypes))
                {
                    var type = t;
                    collector.InspectType(type.FullName ?? type.Name, () => Inspect(type, filter, asmDir));
                }
            }
            return collector;
        }

        /// <summary>
        /// Тесты одного типа. Методы отбираются фильтром по имени до чтения их атрибутов, конструктор проверяется,
        /// только если такие методы есть: сигнатуры чужих типов (ссылки на отсутствующие сборки) не трогаются без нужды.
        /// </summary>
        private static List<TestDescriptor> Inspect(Type t, TestFilter filter, string? asmDir)
        {
            var descriptors = new List<TestDescriptor>();
            if (t.IsAbstract || !t.IsClass) return descriptors;

            var className = t.FullName ?? t.Name;
            var candidates = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => filter.IsMatch($"{className}.{m.Name}"))
                .ToList();
            if (candidates.Count == 0) return descriptors;
            if (t.GetConstructor(Type.EmptyTypes) == null) return descriptors; // Must be default-constructible.

            var classModel = t.GetCustomAttribute<TestModelAttribute>()?.Model;

            foreach (var m in candidates)
            {
                string? skip;
                string? model;

                var fact = m.GetCustomAttribute<RevitFactAttribute>();
                var test = m.GetCustomAttribute<RevitTestAttribute>();
                if (fact != null)
                {
                    skip = fact.Skip;
                    model = null;
                }
                else if (test != null)
                {
                    skip = test.Skip;
                    model = ModelKeys.Resolve(test.Model ?? classModel, asmDir);
                }
                else
                {
                    continue;
                }

                var parameters = m.GetParameters();
                bool takesContext;
                if (parameters.Length == 0) takesContext = false;
                else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(ITestContext)) takesContext = true;
                else continue; // Wrong signature — silently skip.

                descriptors.Add(new TestDescriptor(t, m, skip, takesContext, model));
            }
            return descriptors;
        }

        private static string? SafeGetDirectory(Assembly asm)
        {
            try { return Path.GetDirectoryName(asm.Location); }
            catch { return null; }
        }
    }
}
