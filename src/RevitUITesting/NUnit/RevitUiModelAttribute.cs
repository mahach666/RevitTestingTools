using System;
using System.IO;
using RevitUITesting.Revit;

namespace RevitUITesting.NUnit
{
    /// <summary>
    /// Модель класса тестов: путь .rvt (полный или относительно папки тестовой сборки) или «@new» — новый проект. Открывается в
    /// [OneTimeSetUp] через Host.Open и становится активной; CopyToTemp — открыть копию во временной папке (исходный файл не меняется).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class RevitUiModelAttribute : Attribute
    {
        public RevitUiModelAttribute(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("[RevitUiModel]: нужен путь модели .rvt или «@new».", nameof(path));
            }
            Path = path;
        }

        public string Path { get; }

        public bool CopyToTemp { get; set; } = true;

        public bool IsNew => Path == RevitHostClient.NewModel;

        /// <summary>«@new» или полный путь существующей модели .rvt.</summary>
        public string Resolve(string testDirectory)
        {
            if (IsNew)
            {
                return Path;
            }
            if (Path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                throw new ArgumentException(@"[RevitUiModel]: префикс \\?\ в процессе Revit 2022 даёт «Illegal characters in path» — укажите обычный путь: " + Path);
            }
            string full = System.IO.Path.IsPathRooted(Path) ? Path : System.IO.Path.GetFullPath(System.IO.Path.Combine(testDirectory, Path));
            if (!full.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"[RevitUiModel]: ожидается модель .rvt или «@new», получено «{Path}».");
            }
            if (!File.Exists(full))
            {
                throw new FileNotFoundException("[RevitUiModel]: модель не найдена", full);
            }
            return full;
        }
    }
}
