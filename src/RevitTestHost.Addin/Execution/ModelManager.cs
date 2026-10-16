using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTestHost.Core.Protocol;
using RevitTestHost.Sdk.Attributes;

namespace RevitTestHost.Addin.Execution
{
    /// <summary>
    /// Opens and caches test model documents for the duration of one test run.
    /// Documents are opened in the background (never activated in the UI), so they
    /// can always be closed without saving on <see cref="Dispose"/>.
    /// Модель, уже открытая в Revit (в режиме сервера — клиентом в UI), не открывается повторно
    /// и не закрывается: закрываются только документы, которые открыл или создал сам ModelManager.
    /// </summary>
    internal sealed class ModelManager : IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly Dictionary<string, Document> _openDocs =
            new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Документы, которые открыл или создал этот ModelManager: только их закрывает <see cref="Dispose"/>.</summary>
        private readonly List<Document> _ownedDocs = new List<Document>();

        public ModelManager(UIApplication uiApp)
        {
            _uiApp = uiApp;
        }

        /// <summary>
        /// Returns an open document for the given model path. "@new" creates a fresh
        /// metric project document. Throws with a clear message when the file is missing
        /// or Revit fails to open it.
        /// </summary>
        public Document GetOrOpen(string modelPath)
        {
            if (_openDocs.TryGetValue(modelPath, out var cached) && cached.IsValidObject)
            {
                return cached;
            }

            Application app = _uiApp.Application;
            Document doc;
            if (modelPath == RevitTestAttribute.NewProject)
            {
                doc = app.NewProjectDocument(UnitSystem.Metric);
                _ownedDocs.Add(doc);
            }
            else
            {
                if (!File.Exists(modelPath))
                {
                    throw new FileNotFoundException($"Test model not found: {modelPath}", modelPath);
                }
                var alreadyOpen = FindOpenDocument(app, modelPath);
                if (alreadyOpen != null)
                {
                    doc = alreadyOpen;
                }
                else
                {
                    doc = app.OpenDocumentFile(modelPath);
                    _ownedDocs.Add(doc);
                }
            }

            _openDocs[modelPath] = doc;
            return doc;
        }

        /// <summary>Документ, уже открытый в Revit по тому же пути (связанные и несохранённые документы не учитываются).</summary>
        private static Document? FindOpenDocument(Application app, string modelPath)
        {
            foreach (Document open in app.Documents)
            {
                if (open.IsLinked || string.IsNullOrEmpty(open.PathName)) continue;
                if (DocumentLookup.IsSamePath(open.PathName, modelPath)) return open;
            }
            return null;
        }

        public void Dispose()
        {
            foreach (var doc in _ownedDocs)
            {
                try
                {
                    if (doc.IsValidObject)
                    {
                        doc.Close(false);
                    }
                }
                catch
                {
                    // Closing is best-effort: an already-closed or UI-active document must not fail the run.
                }
            }
            _ownedDocs.Clear();
            _openDocs.Clear();
        }
    }
}
