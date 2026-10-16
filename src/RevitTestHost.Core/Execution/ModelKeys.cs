using System.IO;

namespace RevitTestHost.Core.Execution
{
    /// <summary>
    /// Специальные значения модели теста. Значения совпадают с константами SDK
    /// (<c>RevitTestAttribute.NewProject</c>, <c>RevitTestAttribute.ActiveDocument</c>) — это проверяет модульный тест.
    /// </summary>
    public static class ModelKeys
    {
        /// <summary>Новый метрический проект, создаётся программно.</summary>
        public const string NewProject = "@new";

        /// <summary>Документ, активный в UI Revit; хост его не открывает и не закрывает.</summary>
        public const string ActiveDocument = "@active";

        public const string NoActiveDocumentMessage =
            "в Revit нет активного документа. Тесты с Model = \"@active\" работают с документом, открытым в UI: " +
            "откройте модель (в режиме сервера — командой open) и повторите прогон";

        /// <summary>
        /// Путь модели из атрибута: null/пусто — без модели, «@new» и «@active» — как есть,
        /// абсолютный путь — как есть, относительный — относительно папки тестовой сборки.
        /// </summary>
        public static string? Resolve(string? model, string? assemblyDirectory)
        {
            if (string.IsNullOrWhiteSpace(model)) return null;
            if (model == NewProject || model == ActiveDocument) return model;
            if (Path.IsPathRooted(model)) return model;
            return assemblyDirectory != null ? Path.GetFullPath(Path.Combine(assemblyDirectory, model)) : model;
        }
    }
}
