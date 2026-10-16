#if NET
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace RevitTestHost.Core.Execution
{
    /// <summary>
    /// Контекст загрузки одной тестовой сборки на .NET 8 и новее. Сборки из <c>sharedAssemblies</c> (Sdk и Core надстройки)
    /// отдаются тестовой сборке готовыми: она и надстройка видят один тип <c>ITestContext</c> и одни атрибуты, даже когда
    /// рядом с тестами лежит собственная копия Sdk. Остальное ищется по порядку: уже загруженное в контексте по умолчанию
    /// (RevitAPI и другие сборки Revit), <c>.deps.json</c> тестовой сборки, файл рядом с ней.
    /// </summary>
    public sealed class TestLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _directory;
        private readonly Dictionary<string, Assembly> _shared;

        public TestLoadContext(string testAssemblyPath, IEnumerable<Assembly> sharedAssemblies)
            : base("RevitTestHost: " + Path.GetFileName(testAssemblyPath))
        {
            if (testAssemblyPath == null) throw new ArgumentNullException(nameof(testAssemblyPath));
            if (sharedAssemblies == null) throw new ArgumentNullException(nameof(sharedAssemblies));
            _resolver = new AssemblyDependencyResolver(testAssemblyPath);
            _directory = Path.GetDirectoryName(Path.GetFullPath(testAssemblyPath))!;
            _shared = sharedAssemblies.ToDictionary(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (name == null) return null;

            if (_shared.TryGetValue(name, out var shared)) return shared;

            // shortcut: сборка, уже загруженная в контексте по умолчанию, берётся оттуда целиком (общая идентичность типов
            // Revit API); собственная более новая копия такой сборки у плагина здесь не используется
            var loaded = Default.Assemblies.FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            if (loaded != null) return loaded;

            var path = _resolver.ResolveAssemblyToPath(assemblyName) ?? ProbeDirectory(_directory, name);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        /// <summary>Файл <c>&lt;имя&gt;.dll</c> в папке тестовой сборки; null, если его нет (зависимость без .deps.json).</summary>
        internal static string? ProbeDirectory(string directory, string name)
        {
            var candidate = Path.Combine(directory, name + ".dll");
            return File.Exists(candidate) ? candidate : null;
        }
    }
}
#endif
