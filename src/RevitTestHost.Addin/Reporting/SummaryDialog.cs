using System.Linq;
using System.Text;
using Autodesk.Revit.UI;
using RevitTestHost.Core.Reporting;

namespace RevitTestHost.Addin.Reporting
{
    internal static class SummaryDialog
    {
        public static void Show(TestRunReport report, string jsonPath)
        {
            var td = new TaskDialog("RevitTestHost — Test Results")
            {
                MainInstruction = $"{report.Passed} passed / {report.Failed + report.Errored} failed / {report.Skipped} skipped",
                MainContent = BuildBody(report, jsonPath),
                CommonButtons = TaskDialogCommonButtons.Close
            };
            td.Show();
        }

        private static string BuildBody(TestRunReport report, string jsonPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Total: {report.Total} | Duration: {report.DurationMs} ms");
            sb.AppendLine($"JSON report: {jsonPath}");
            sb.AppendLine();

            var failures = report.Results.Where(r => r.Status == TestStatus.Failed || r.Status == TestStatus.Error).ToList();
            if (failures.Count > 0)
            {
                sb.AppendLine("Failures:");
                foreach (var f in failures.Take(10))
                {
                    sb.AppendLine($"  • {f.Class}.{f.Method}: {f.Message}");
                }
                if (failures.Count > 10) sb.AppendLine($"  ... and {failures.Count - 10} more (see JSON)");
            }
            else if (report.Total == 0)
            {
                sb.AppendLine("No tests discovered. Check Tests folder.");
            }

            return sb.ToString();
        }
    }
}
