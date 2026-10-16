using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
#if NET
using RevitTestHost.Core.Execution;
using RevitTestHost.Sdk.Contracts;
#endif

namespace RevitTestHost.Addin.Discovery
{
    public static class TestAssemblyLoader
    {
        public const string TestsFolderRelativeToAddin = "Tests";

        /// <summary>
        /// Сборки <c>*Tests.dll</c> из папки <c>Tests</c> рядом с надстройкой. Сборка, которая не загрузилась,
        /// пропускается, причина уходит в <paramref name="onLoadFailed"/> (путь, исключение).
        /// </summary>
        public static IEnumerable<Assembly> LoadAll(Action<string, Exception> onLoadFailed)
        {
            if (onLoadFailed == null) throw new ArgumentNullException(nameof(onLoadFailed));
            var addinDir = Path.GetDirectoryName(typeof(TestAssemblyLoader).Assembly.Location)!;
            var testsDir = Path.Combine(addinDir, TestsFolderRelativeToAddin);
            if (!Directory.Exists(testsDir))
            {
                yield break;
            }

            foreach (var dll in Directory.GetFiles(testsDir, "*Tests.dll", SearchOption.TopDirectoryOnly))
            {
                Assembly? asm = null;
                try
                {
#if NET
                    // .NET 8 и новее: свой контекст на каждую сборку; Sdk и Core берутся у надстройки (см. TestLoadContext)
                    var context = new TestLoadContext(dll, new[] { typeof(ITestContext).Assembly, typeof(TestLoadContext).Assembly });
                    asm = context.LoadFromAssemblyPath(dll);
#else
                    asm = Assembly.LoadFrom(dll);
#endif
                }
                catch (Exception ex)
                {
                    onLoadFailed(dll, ex);
                }
                if (asm != null) yield return asm;
            }
        }
    }
}
