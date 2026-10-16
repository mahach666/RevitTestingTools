using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitTestHost.Sdk.Contracts
{
    /// <summary>
    /// Passed to a test method as the only parameter (or omitted entirely).
    /// For <see cref="Attributes.RevitFactAttribute"/> tests, <see cref="Document"/> and <see cref="UiDoc"/> are null.
    /// </summary>
    public interface ITestContext
    {
        UIApplication UiApp { get; }
        Application App { get; }

        /// <summary>Null for [RevitFact] tests (no model loaded). Set for [RevitTest(Model=...)] tests.</summary>
        Document? Document { get; }
        UIDocument? UiDoc { get; }

        ITransactionFactory Transactions { get; }
        IRevitTestLogger Logger { get; }

        string TestName { get; }

        /// <summary>
        /// Model the test runs against: path from the [RevitTest]/[TestModel] attribute
        /// (resolved to an absolute path), RevitTestAttribute.NewProject, RevitTestAttribute.ActiveDocument, or null for [RevitFact].
        /// </summary>
        string? ModelPath { get; }

        /// <summary>
        /// Параметры прогона из команды <c>run</c> режима сервера RevitTestHost (строковые ключи и значения,
        /// ключи с учётом регистра). Никогда не null; в CLI-режиме и при запуске кнопкой ленты — пустой словарь.
        /// </summary>
        IReadOnlyDictionary<string, string> Parameters { get; }
    }
}
