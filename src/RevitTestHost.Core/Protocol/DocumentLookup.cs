using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitTestHost.Core.Protocol
{
    /// <summary>
    /// Поиск открытого документа по заголовку или пути для <c>close</c> и <c>run.model</c>.
    /// Заголовок сравнивается без учёта регистра и без расширения Revit (.rvt/.rfa/.rte/.rft) —
    /// Revit показывает Title без расширения или с ним в зависимости от настроек Windows.
    /// Путь сравнивается после <see cref="Path.GetFullPath(string)"/> без учёта регистра.
    /// </summary>
    public static class DocumentLookup
    {
        private static readonly string[] RevitExtensions = { ".rvt", ".rfa", ".rte", ".rft" };

        /// <summary>
        /// Ищет документ по заголовку (если <paramref name="title"/> задан) или по пути <paramref name="path"/>.
        /// Аргументы уже проверены разбором запроса (<see cref="CloseArgs"/>, <see cref="RunArgs"/>): здесь они не перепроверяются.
        /// </summary>
        public static DocumentInfo Find(IReadOnlyList<DocumentInfo> documents, string? title, string? path)
        {
            if (documents == null) throw new ArgumentNullException(nameof(documents));

            List<DocumentInfo> matches;
            string what;
            if (title != null)
            {
                var wanted = StripRevitExtension(title);
                matches = documents.Where(d => string.Equals(StripRevitExtension(d.Title), wanted, StringComparison.OrdinalIgnoreCase)).ToList();
                what = $"с заголовком '{title}'";
            }
            else
            {
                var wanted = NormalizePath(path!);
                matches = documents.Where(d => d.Path.Length > 0 && string.Equals(NormalizePath(d.Path), wanted, StringComparison.OrdinalIgnoreCase)).ToList();
                what = $"с путём '{path}'";
            }

            if (matches.Count == 1) return matches[0];
            if (matches.Count == 0)
                throw new HostCommandException(ErrorCodes.NotFound, $"Документ {what} не открыт. Открытые документы: {Describe(documents)}.");
            throw new HostCommandException(ErrorCodes.BadRequest,
                $"Под признак {what} подходят несколько документов ({Describe(matches)}); укажите путь.");
        }

        /// <summary>Значение <c>run.model</c>, отличное от «@active»: абсолютный путь — поиск по пути, иначе по заголовку.</summary>
        public static DocumentInfo FindByModel(IReadOnlyList<DocumentInfo> documents, string model)
        {
            return ArgReader.IsAbsolutePath(model) ? Find(documents, null, model) : Find(documents, model, null);
        }

        /// <summary>
        /// Путь открытого документа <paramref name="documentPath"/> указывает на файл <paramref name="path"/>:
        /// сравнение после <see cref="Path.GetFullPath(string)"/> без учёта регистра. Пустой путь (несохранённый документ) — false.
        /// </summary>
        public static bool IsSamePath(string documentPath, string path)
        {
            if (string.IsNullOrWhiteSpace(documentPath) || string.IsNullOrWhiteSpace(path)) return false;
            return string.Equals(NormalizePath(documentPath), NormalizePath(path), StringComparison.OrdinalIgnoreCase);
        }

        private static string StripRevitExtension(string title)
        {
            var t = title.Trim();
            foreach (var ext in RevitExtensions)
            {
                if (t.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return t.Substring(0, t.Length - ext.Length);
            }
            return t;
        }

        /// <summary>
        /// Полный путь без завершающего разделителя. Путь документа, который не является путём файловой системы
        /// (например, модель на Revit Server), остаётся как есть и сравнивается строкой.
        /// </summary>
        private static string NormalizePath(string path)
        {
            var trimmed = path.Trim();
            try
            {
                return Path.GetFullPath(trimmed).TrimEnd('\\');
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return trimmed;
            }
        }

        private static string Describe(IEnumerable<DocumentInfo> documents)
        {
            var titles = documents.Select(d => d.Path.Length > 0 ? $"'{d.Title}' ({d.Path})" : $"'{d.Title}'").ToList();
            return titles.Count == 0 ? "(нет)" : string.Join(", ", titles);
        }
    }
}
