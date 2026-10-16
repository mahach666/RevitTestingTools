using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitTestHost.Core.Reporting
{
    public sealed class TestRunReport
    {
        public DateTime StartedAt { get; init; } = DateTime.UtcNow;
        public string RevitVersion { get; init; } = "2024";
        public string Host { get; init; } = "RevitTestHost " + (typeof(TestRunReport).Assembly.GetName().Version?.ToString(3) ?? "unknown");
        public List<TestResult> Results { get; init; } = new();

        /// <summary>Пропущенные при обнаружении сборки и типы (не результаты тестов); пусто — поля в JSON нет.</summary>
        public List<string> Warnings { get; init; } = new();

        public int Total => Results.Count;
        public int Passed => Results.Count(r => r.Status == TestStatus.Passed);
        public int Failed => Results.Count(r => r.Status == TestStatus.Failed);
        public int Errored => Results.Count(r => r.Status == TestStatus.Error);
        public int Skipped => Results.Count(r => r.Status == TestStatus.Skipped);
        public long DurationMs => Results.Sum(r => r.DurationMs);
    }
}
