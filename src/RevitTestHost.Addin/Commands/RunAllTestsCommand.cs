using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTestHost.Addin.Execution;
using RevitTestHost.Addin.Reporting;
using RevitTestHost.Core.Reporting;

namespace RevitTestHost.Addin.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RunAllTestsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            TestRunReport report;

            try
            {
                report = TestRunOrchestrator.Run(uiApp);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("RevitTestHost — fatal", $"Discovery/run aborted: {ex.Message}");
                return Result.Failed;
            }

            string jsonPath;
            try { jsonPath = JsonReporter.Write(report); }
            catch (Exception ex) { jsonPath = $"(failed to write JSON: {ex.Message})"; }

            SummaryDialog.Show(report, jsonPath);
            return Result.Succeeded;
        }
    }
}
