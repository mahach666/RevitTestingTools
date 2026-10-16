using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTestHost.Addin.Execution;
using RevitTestHost.Core.Protocol;
using RevitTestHost.Core.Reporting;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Addin.Serve
{
    /// <summary>
    /// Исполнитель команд канала на Revit API. Вызывается только из обработчика ExternalEvent (поток Revit).
    /// Проверки аргументов и коды ошибок — в <see cref="RequestDispatcher"/> (RevitTestHost.Core):
    /// исключения Revit API отсюда уходят как есть и становятся ответом <c>REVIT_ERROR</c>.
    /// </summary>
    internal sealed class RevitRequestExecutor : IRequestExecutor
    {
        private readonly UIApplication _uiApp;
        private readonly IServerLog _log;

        public RevitRequestExecutor(UIApplication uiApp, IServerLog log)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public string RevitVersion => _uiApp.Application.VersionNumber;

        public IReadOnlyList<DocumentInfo> Documents()
        {
            var active = _uiApp.ActiveUIDocument?.Document;
            var result = new List<DocumentInfo>();
            foreach (Document doc in _uiApp.Application.Documents)
            {
                if (doc.IsLinked) continue;
                result.Add(ToInfo(doc, active != null && doc.Equals(active)));
            }
            return result;
        }

        public void CreateProjectFile(string savePath)
        {
            var doc = _uiApp.Application.NewProjectDocument(UnitSystem.Metric);
            try
            {
                doc.SaveAs(savePath);
            }
            catch (Exception saveError)
            {
                // Не оставляем фоновый документ открытым; исходная ошибка уходит дальше
                try
                {
                    doc.Close(false);
                }
                catch (Exception closeError)
                {
                    throw new InvalidOperationException(
                        $"Не удалось сохранить новый проект в {savePath}: {ExceptionText.Describe(saveError)}. " +
                        $"Фоновый документ не закрыт: {ExceptionText.Describe(closeError)}", saveError);
                }
                throw;
            }

            if (!doc.Close(false))
                throw new InvalidOperationException($"Revit не закрыл фоновый документ после сохранения в {savePath}.");
        }

        public DocumentInfo OpenAndActivate(string path)
        {
            var uiDoc = _uiApp.OpenAndActivateDocument(path);
            return ToInfo(uiDoc.Document, isActive: true);
        }

        public void Close(DocumentInfo document)
        {
            var doc = document.Handle as Document
                      ?? throw new InvalidOperationException($"У документа '{document.Title}' нет объекта Revit (Handle).");
            if (!doc.Close(false))
                throw new InvalidOperationException($"Revit не закрыл документ '{document.Title}' (Document.Close вернул false).");
        }

        public TestRunReport RunTests(string? filter, IReadOnlyDictionary<string, string> parameters, DocumentInfo? activeDocument)
        {
            var report = TestRunOrchestrator.Run(_uiApp, filter, parameters, activeDocument?.Handle as Document);
            // Пропущенные при обнаружении сборки и типы — и в ответе (warnings), и в журнале сервера
            foreach (var warning in report.Warnings)
                _log.Info("Обнаружение тестов: " + warning);
            return report;
        }

        private static DocumentInfo ToInfo(Document doc, bool isActive) =>
            new DocumentInfo(doc.Title, doc.PathName ?? "", isActive, doc.IsModified, doc.IsFamilyDocument, doc);
    }
}
