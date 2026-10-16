using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RevitTestHost.Core.Execution;
using RevitTestHost.Core.Reporting;

namespace RevitTestHost.Tests.Execution
{
    [TestFixture]
    public class RunEngineTests
    {
        private sealed class FakeTest : ITestCase
        {
            public FakeTest(string cls, string method, string? model)
            {
                ClassFullName = cls;
                MethodName = method;
                ModelPath = model;
            }

            public string ClassFullName { get; }
            public string MethodName { get; }
            public string? ModelPath { get; }
        }

        private sealed class FakeDocument
        {
            public FakeDocument(string name) => Name = name;
            public string Name { get; }
        }

        private static TestResult Pass(FakeTest t, FakeDocument? d) =>
            new TestResult(t.ClassFullName, t.MethodName, TestStatus.Passed, 1, d?.Name, model: t.ModelPath);

        [Test]
        public void ActiveModel_WithoutActiveDocument_IsErrorWithClearMessage()
        {
            var tests = new[] { new FakeTest("Ns.Probe", "Check", ModelKeys.ActiveDocument) };
            var executed = 0;

            var report = RunEngine.Run<FakeTest, FakeDocument>(
                tests, TestFilter.Parse(null), "2022",
                RunEngine.WithActiveDocument<FakeDocument>(null, m => throw new AssertionException("не должен открываться: " + m)),
                (t, d) => { executed++; return Pass(t, d); });

            Assert.That(executed, Is.EqualTo(0));
            var result = report.Results.Single();
            Assert.That(result.Status, Is.EqualTo(TestStatus.Error));
            Assert.That(result.Model, Is.EqualTo("@active"));
            Assert.That(result.Message, Does.Contain("нет активного документа"));
        }

        [Test]
        public void ActiveModel_WithActiveDocument_PassesItWithoutOpening()
        {
            var active = new FakeDocument("UI");
            var opened = new List<string>();

            var report = RunEngine.Run<FakeTest, FakeDocument>(
                new[] { new FakeTest("Ns.Probe", "A", "@active"), new FakeTest("Ns.Probe", "B", "@active") },
                TestFilter.Parse(null), "2022",
                RunEngine.WithActiveDocument(active, m => { opened.Add(m); return new FakeDocument(m); }),
                Pass);

            Assert.That(opened, Is.Empty);
            Assert.That(report.Results.Select(r => r.Message), Is.EqualTo(new[] { "UI", "UI" }));
        }

        [Test]
        public void OtherModels_AreOpenedOncePerGroup_AndNoModelGroupRunsFirst()
        {
            var opened = new List<string>();
            var tests = new[]
            {
                new FakeTest("Ns.A", "M1", @"C:\m.rvt"),
                new FakeTest("Ns.A", "Fact", null),
                new FakeTest("Ns.A", "M2", @"C:\m.rvt"),
                new FakeTest("Ns.A", "New", "@new"),
            };

            var report = RunEngine.Run<FakeTest, FakeDocument>(
                tests, TestFilter.Parse(null), "2022",
                RunEngine.WithActiveDocument<FakeDocument>(null, m => { opened.Add(m); return new FakeDocument(m); }),
                Pass);

            Assert.That(opened, Is.EqualTo(new[] { "@new", @"C:\m.rvt" }));
            Assert.That(report.Results.Select(r => r.Method), Is.EqualTo(new[] { "Fact", "New", "M1", "M2" }));
            Assert.That(report.RevitVersion, Is.EqualTo("2022"));
        }

        [Test]
        public void OpenFailure_MarksWholeGroupAsError()
        {
            var report = RunEngine.Run<FakeTest, FakeDocument>(
                new[] { new FakeTest("Ns.A", "M1", @"C:\missing.rvt"), new FakeTest("Ns.A", "M2", @"C:\missing.rvt") },
                TestFilter.Parse(null), "2022",
                RunEngine.WithActiveDocument<FakeDocument>(null, m => throw new InvalidOperationException("файл не найден")),
                Pass);

            Assert.That(report.Results.Select(r => r.Status), Is.All.EqualTo(TestStatus.Error));
            Assert.That(report.Results[0].Message, Is.EqualTo(@"Не удалось открыть модель теста 'C:\missing.rvt': файл не найден"));
        }

        [Test]
        public void Filter_SelectsOnlyMatchingTests()
        {
            var report = RunEngine.Run<FakeTest, FakeDocument>(
                new[] { new FakeTest("Ns.Probes.FamilyLoaded", "Check", null), new FakeTest("Ns.Other", "Check", null) },
                TestFilter.Parse("ns.probes.*"), "2022",
                RunEngine.WithActiveDocument<FakeDocument>(null, m => new FakeDocument(m)),
                Pass);

            Assert.That(report.Results.Select(r => r.Class), Is.EqualTo(new[] { "Ns.Probes.FamilyLoaded" }));
        }
    }
}
