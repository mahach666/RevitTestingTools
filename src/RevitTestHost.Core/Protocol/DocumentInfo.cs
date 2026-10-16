using System;
using RevitTestHost.Core.Json;

namespace RevitTestHost.Core.Protocol
{
    /// <summary>Открытый в Revit документ (связанные файлы сюда не попадают).</summary>
    public sealed class DocumentInfo
    {
        public DocumentInfo(string title, string path, bool isActive, bool isModified, bool isFamilyDocument, object? handle = null)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            IsActive = isActive;
            IsModified = isModified;
            IsFamilyDocument = isFamilyDocument;
            Handle = handle;
        }

        public string Title { get; }

        /// <summary>Полный путь файла; пустая строка — документ ещё не сохранён.</summary>
        public string Path { get; }

        public bool IsActive { get; }
        public bool IsModified { get; }
        public bool IsFamilyDocument { get; }

        /// <summary>Объект документа исполнителя (в надстройке — <c>Autodesk.Revit.DB.Document</c>); в JSON не пишется.</summary>
        public object? Handle { get; }

        /// <summary>Элемент <c>documents</c>: title, path, isActive, isModified, isFamilyDocument.</summary>
        public void WriteFull(JsonWriter w)
        {
            w.BeginObject()
                .Property("title", Title)
                .Property("path", Path)
                .Property("isActive", IsActive)
                .Property("isModified", IsModified)
                .Property("isFamilyDocument", IsFamilyDocument)
                .EndObject();
        }

        /// <summary><c>ping.activeDocument</c>: title, path, isFamilyDocument.</summary>
        public void WriteSummary(JsonWriter w)
        {
            w.BeginObject()
                .Property("title", Title)
                .Property("path", Path)
                .Property("isFamilyDocument", IsFamilyDocument)
                .EndObject();
        }
    }
}
