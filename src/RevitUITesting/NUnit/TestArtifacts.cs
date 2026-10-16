using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using NUnit.Framework;
using RevitUITesting.Uia;

namespace RevitUITesting.NUnit
{
    /// <summary>
    /// Папка артефактов теста «&lt;ArtifactsDir&gt;\&lt;yyyyMMdd_HHmmss_fff&gt;_&lt;Класс&gt;.&lt;Тест&gt;» с одной меткой времени на все файлы;
    /// файлы прикладываются к результату NUnit. Хранятся последние <see cref="RevitUiConfig.ArtifactsKeep"/> папок.
    /// </summary>
    public sealed class TestArtifacts
    {
        /// <summary>Имя папки артефактов теста (см. <see cref="For"/>): уборка трогает только такие папки.</summary>
        private static readonly System.Text.RegularExpressions.Regex FolderName =
            new System.Text.RegularExpressions.Regex(@"^\d{8}_\d{6}_\d{3}_", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private TestArtifacts(string folder)
        {
            Folder = folder;
        }

        public string Folder { get; }

        /// <summary>Папка для текущего теста (создаётся при первом обращении к файлам).</summary>
        public static TestArtifacts For(RevitUiConfig config, string className, string testName, DateTime stamp)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            string name = stamp.ToString("yyyyMMdd_HHmmss_fff") + "_" + Sanitize(className + "." + testName, 100);
            return new TestArtifacts(Path.Combine(config.ResolvedArtifactsDir, name));
        }

        /// <summary>Путь к файлу в папке теста (папка создаётся).</summary>
        public string PathOf(string fileName)
        {
            Directory.CreateDirectory(Folder);
            return Path.Combine(Folder, fileName);
        }

        /// <summary>Записать текст (UTF-8 с BOM) и приложить файл к результату теста.</summary>
        public string WriteText(string fileName, string text, string description)
        {
            string path = PathOf(fileName);
            File.WriteAllText(path, text ?? "", new UTF8Encoding(true));
            TestContext.AddTestAttachment(path, description);
            return path;
        }

        /// <summary>Снимок всего экрана.</summary>
        public string Screen(string fileName = "screen.png")
        {
            string path = PathOf(fileName);
            Capture.MainScreen().ToFile(path);
            TestContext.AddTestAttachment(path, "Экран при падении");
            return path;
        }

        /// <summary>
        /// Подробный дамп переданных окон (без чтения SelectionPattern.Selection). Перечень окон даёт сеанс приложения
        /// (например, окна процесса Revit вместе с дочерними и всплывающими).
        /// </summary>
        public string Windows(IEnumerable<AutomationElement> windows, string fileName = "windows.txt")
        {
            if (windows == null)
            {
                throw new ArgumentNullException(nameof(windows));
            }
            StringBuilder sb = new StringBuilder();
            foreach (AutomationElement window in windows)
            {
                sb.AppendLine(window.DumpTree(40, true));
                sb.AppendLine();
            }
            return WriteText(fileName, sb.ToString(), "Дерево UI Automation окон приложения");
        }

        /// <summary>
        /// Удалить старые папки артефактов тестов (только с именем вида «yyyyMMdd_HHmmss_fff_…» — прочие папки каталога не трогаются),
        /// оставив ArtifactsKeep последних (0 и меньше — ничего не удалять); удалённые перечисляются в журнале. Папку не удалось удалить
        /// (открыт файл, нет прав) — предупреждение в журнале, подготовка тестов продолжается: артефакты — диагностика.
        /// </summary>
        public static void Prune(RevitUiConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            string root = config.ResolvedArtifactsDir;
            if (!Directory.Exists(root) || config.ArtifactsKeep <= 0)
            {
                return;
            }
            List<DirectoryInfo> folders = new DirectoryInfo(root).GetDirectories()
                .Where(d => FolderName.IsMatch(d.Name))
                .OrderByDescending(d => d.Name, StringComparer.Ordinal).ToList();
            foreach (DirectoryInfo old in folders.Skip(config.ArtifactsKeep))
            {
                try
                {
                    old.Delete(true);
                    UiLog.Action("удалена старая папка артефактов " + old.FullName);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    UiLog.Warn("не удалось удалить старую папку артефактов " + old.FullName + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        internal static string Sanitize(string s, int max)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }
            return s.Length > max ? s.Substring(0, max) : s;
        }
    }
}
