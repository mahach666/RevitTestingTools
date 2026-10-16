using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;

namespace RevitUITesting.Tests.Contract
{
    /// <summary>Доказательства контрактных тестов: файлы в исходной папке Contract\Evidence (коммитятся вместе с выводами в RevitIds/RevitTitles).</summary>
    internal static class Evidence
    {
        /// <summary>Имя файла для года Revit: <c>view-3d</c> и 2024 дают <c>view-3d-2024.txt</c>.</summary>
        internal static string FileName(string baseName, string revitVersion)
        {
            return $"{baseName}-{revitVersion}.txt";
        }

        /// <summary>Пишет доказательство для версии Revit из конфигурации (revit-ui-tests.json или REVIT_UI_REVIT_VERSION).</summary>
        internal static string Write(string baseName, string text, [CallerFilePath] string callerFile = null)
        {
            RevitUiConfig config = RevitUiConfig.Load(TestContext.CurrentContext.TestDirectory);
            string directory = Path.Combine(Path.GetDirectoryName(callerFile), "Evidence");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, FileName(baseName, config.RevitVersion));
            File.WriteAllText(path, $"# {DateTime.Now:yyyy-MM-dd HH:mm}, Revit {config.RevitVersion} {config.Language}, {Environment.MachineName}" + Environment.NewLine + text, new UTF8Encoding(true));
            TestContext.Out.WriteLine("доказательство: " + path);
            return path;
        }
    }
}
