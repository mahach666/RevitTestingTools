using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitTestHost.Core.Protocol;
using RevitTestHost.Core.Reporting;

namespace RevitTestHost.Tests.Protocol
{
    /// <summary>Исполнитель без Revit: список документов в памяти и журнал вызовов.</summary>
    internal sealed class FakeExecutor : IRequestExecutor
    {
        public readonly List<DocumentInfo> Docs = new List<DocumentInfo>();
        public readonly List<string> Calls = new List<string>();
        public Exception? ThrowOnDocuments;
        public string? LastFilter;
        public IReadOnlyDictionary<string, string>? LastParameters;
        public DocumentInfo? LastRunDocument;

        public string RevitVersion => "2022";

        public IReadOnlyList<DocumentInfo> Documents()
        {
            if (ThrowOnDocuments != null) throw ThrowOnDocuments;
            return Docs.ToList();
        }

        public void CreateProjectFile(string savePath)
        {
            Calls.Add("create " + savePath);
            File.WriteAllText(savePath, "rvt");
        }

        public DocumentInfo OpenAndActivate(string path)
        {
            Calls.Add("open " + path);
            var opened = new DocumentInfo(Path.GetFileNameWithoutExtension(path), path, true, false, false);
            for (var i = 0; i < Docs.Count; i++)
            {
                var d = Docs[i];
                Docs[i] = new DocumentInfo(d.Title, d.Path, false, d.IsModified, d.IsFamilyDocument);
            }
            Docs.Add(opened);
            return opened;
        }

        public void Close(DocumentInfo document)
        {
            Calls.Add("close " + document.Title);
            Docs.RemoveAll(d => d.Title == document.Title);
        }

        public TestRunReport RunTests(string? filter, IReadOnlyDictionary<string, string> parameters, DocumentInfo? activeDocument)
        {
            Calls.Add("run");
            LastFilter = filter;
            LastParameters = parameters;
            LastRunDocument = activeDocument;
            return new TestRunReport
            {
                RevitVersion = "2022",
                Results = { new TestResult("Ns.Probe", "Check", TestStatus.Passed, 4, model: "@active") }
            };
        }
    }
}
