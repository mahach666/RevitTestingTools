using System;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTestHost.Sdk.Contracts;

namespace RevitTestHost.Addin.Execution
{
    internal sealed class TestContext : ITestContext
    {
        public UIApplication UiApp { get; }
        public Application App => UiApp.Application;
        public Document? Document { get; }
        public UIDocument? UiDoc { get; }
        public ITransactionFactory Transactions { get; }
        public IRevitTestLogger Logger { get; }
        public string TestName { get; }
        public string? ModelPath { get; }
        public IReadOnlyDictionary<string, string> Parameters { get; }

        public TestContext(UIApplication uiApp, Document? document, string testName, string? modelPath,
            IReadOnlyDictionary<string, string> parameters)
        {
            UiApp = uiApp;
            Document = document;
            UiDoc = document != null ? new UIDocument(document) : null;
            Transactions = new TransactionFactory(document);
            Logger = new ConsoleLogger();
            TestName = testName;
            ModelPath = modelPath;
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        private sealed class ConsoleLogger : IRevitTestLogger
        {
            public void Info(string message) => Console.WriteLine($"[INFO] {message}");
            public void Warn(string message) => Console.WriteLine($"[WARN] {message}");
            public void Error(string message) => Console.WriteLine($"[ERROR] {message}");
        }

        private sealed class TransactionFactory : ITransactionFactory
        {
            private readonly Document? _doc;
            public TransactionFactory(Document? doc) => _doc = doc;

            public IDisposable Begin(string name = "test")
            {
                if (_doc == null)
                    throw new InvalidOperationException("Begin() called on a no-document context (RevitFact). Use [RevitTest(Model=...)] to load a model first.");
                var tx = new Transaction(_doc, name);
                tx.Start();
                return new TxScope(tx);
            }

            private sealed class TxScope : IDisposable
            {
                private readonly Transaction _tx;
                public TxScope(Transaction tx) => _tx = tx;
                public void Dispose()
                {
                    if (_tx.GetStatus() == TransactionStatus.Started)
                        _tx.RollBack();
                    _tx.Dispose();
                }
            }
        }
    }
}
