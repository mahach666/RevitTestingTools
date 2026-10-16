using System;
using System.Collections.Generic;
using NUnit.Framework;
using RevitTestHost.Core.Json;
using RevitTestHost.Core.Reporting;

namespace RevitTestHost.Tests.Reporting
{
    [TestFixture]
    public class JsonReportWriterTests
    {
        private static TestRunReport SampleReport() => new TestRunReport
        {
            StartedAt = new DateTime(2026, 10, 7, 12, 30, 0, DateTimeKind.Utc),
            RevitVersion = "2022",
            Results =
            {
                new TestResult("Ns.A", "Passes", TestStatus.Passed, 5),
                new TestResult("Ns.A", "Fails", TestStatus.Failed, 7, "ожидалось 1\nполучено 2", "at X", "@active"),
                new TestResult("Ns.B", "Skips", TestStatus.Skipped, 0, "причина"),
                new TestResult("Ns.B", "Errs", TestStatus.Error, 3, "boom", model: "C:\\m.rvt"),
            }
        };

        private static Dictionary<string, object?> Parse(string json) => (Dictionary<string, object?>)JsonReader.Parse(json)!;

        [Test]
        public void Summary_HasContractFields()
        {
            var summary = (Dictionary<string, object?>)Parse(JsonReportWriter.ToJson(SampleReport(), indented: false))["summary"]!;

            Assert.That(summary["total"], Is.EqualTo(4L));
            Assert.That(summary["passed"], Is.EqualTo(1L));
            Assert.That(summary["failed"], Is.EqualTo(1L));
            Assert.That(summary["errored"], Is.EqualTo(1L));
            Assert.That(summary["skipped"], Is.EqualTo(1L));
            Assert.That(summary["durationMs"], Is.EqualTo(15L));
            Assert.That(summary["revitVersion"], Is.EqualTo("2022"));
            Assert.That(summary["startedAt"], Is.EqualTo("2026-10-07T12:30:00.0000000Z"));
            Assert.That(summary.ContainsKey("host"), Is.True);
        }

        [Test]
        public void Results_HaveContractFieldsAndLowercaseStatus()
        {
            var results = (List<object?>)Parse(JsonReportWriter.ToJson(SampleReport(), indented: false))["results"]!;

            var failed = (Dictionary<string, object?>)results[1]!;
            Assert.That(failed["class"], Is.EqualTo("Ns.A"));
            Assert.That(failed["method"], Is.EqualTo("Fails"));
            Assert.That(failed["model"], Is.EqualTo("@active"));
            Assert.That(failed["status"], Is.EqualTo("failed"));
            Assert.That(failed["message"], Is.EqualTo("ожидалось 1\nполучено 2"));
            Assert.That(failed["stackTrace"], Is.EqualTo("at X"));
            Assert.That(failed["durationMs"], Is.EqualTo(7L));

            Assert.That(((Dictionary<string, object?>)results[3]!)["status"], Is.EqualTo("error"));
            Assert.That(((Dictionary<string, object?>)results[2]!)["status"], Is.EqualTo("skipped"));
        }

        [Test]
        public void NullOptionalFields_AreOmitted()
        {
            var results = (List<object?>)Parse(JsonReportWriter.ToJson(SampleReport(), indented: false))["results"]!;
            var passed = (Dictionary<string, object?>)results[0]!;

            Assert.That(passed.ContainsKey("model"), Is.False);
            Assert.That(passed.ContainsKey("message"), Is.False);
            Assert.That(passed.ContainsKey("stackTrace"), Is.False);
        }

        [Test]
        public void FileAndProtocolForms_DescribeTheSameObject()
        {
            var report = SampleReport();
            var file = JsonReportWriter.ToJson(report, indented: true);
            var protocol = JsonPayload.ToJson(JsonReportWriter.AsJson(report));

            Assert.That(file, Does.Contain("\n  \"summary\": {"));
            Assert.That(protocol, Does.Not.Contain("\n"));
            Assert.That(JsonReader.Parse(protocol), Is.EqualTo(JsonReader.Parse(file)));
        }

        [Test]
        public void EmptyReport_HasZeroSummaryAndEmptyResults()
        {
            var root = Parse(JsonReportWriter.ToJson(new TestRunReport { RevitVersion = "2022" }, indented: false));

            Assert.That(((Dictionary<string, object?>)root["summary"]!)["total"], Is.EqualTo(0L));
            Assert.That((List<object?>)root["results"]!, Is.Empty);
            Assert.That(root.ContainsKey("warnings"), Is.False, "без предупреждений формат отчёта прежний");
        }

        [Test]
        public void Warnings_WrittenAsArrayOfStrings()
        {
            var report = SampleReport();
            report.Warnings.Add("Сборка A.Tests: 2 типа не загружены");
            report.Warnings.Add("второе");

            var root = Parse(JsonReportWriter.ToJson(report, indented: false));

            Assert.That((List<object?>)root["warnings"]!, Is.EqualTo(new object?[] { "Сборка A.Tests: 2 типа не загружены", "второе" }));
        }
    }
}
