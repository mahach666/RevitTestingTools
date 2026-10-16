using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RevitTestHost.Core.Execution;
using RevitTestHost.Core.Reporting;

namespace RevitTestHost.Tests.Execution
{
    [TestFixture]
    public class TestDiscoveryCollectorTests
    {
        private sealed class FakeTest : ITestCase
        {
            public FakeTest(string cls, string method)
            {
                ClassFullName = cls;
                MethodName = method;
            }

            public string ClassFullName { get; }
            public string MethodName { get; }
            public string? ModelPath => null;
        }

        private static TestDiscoveryCollector<FakeTest> Collector(string? filter) =>
            new TestDiscoveryCollector<FakeTest>(TestFilter.Parse(filter));

        private static TypeLoadException BrokenType() =>
            new TypeLoadException("Could not load type 'PluginServerApi.Clients.IAnalysisRunsClient' from assembly 'PluginServerApi, Version=2.1.0.0'.");

        [Test]
        public void MatchingType_AddsTestsThatMatchFilter()
        {
            var c = Collector("Ns.Probe.Check*");

            c.InspectType("Ns.Probe", () => new[] { new FakeTest("Ns.Probe", "CheckA"), new FakeTest("Ns.Probe", "Other") });

            Assert.That(c.Tests.Select(t => t.MethodName), Is.EqualTo(new[] { "CheckA" }));
            Assert.That(c.Errors, Is.Empty);
            Assert.That(c.Warnings, Is.Empty);
        }

        [Test]
        public void TypeThatCannotMatchFilter_IsNotInspected()
        {
            var c = Collector("RevitTestHost.SampleTests.*");
            var inspected = false;

            c.InspectType("MyPlugin.RevitTests.Foo", () =>
            {
                inspected = true;
                throw BrokenType();
            });

            Assert.That(inspected, Is.False);
            Assert.That(c.Tests, Is.Empty);
            Assert.That(c.Errors, Is.Empty);
            Assert.That(c.Warnings, Is.Empty);
        }

        [Test]
        public void MatchingTypeThatThrows_BecomesDiscoveryErrorResult()
        {
            var c = Collector("*Foo*");

            c.InspectType("MyPlugin.RevitTests.Foo", () => throw BrokenType());

            Assert.That(c.Tests, Is.Empty);
            var error = c.Errors.Single();
            Assert.That(error.Class, Is.EqualTo("MyPlugin.RevitTests.Foo"));
            Assert.That(error.Method, Is.EqualTo("(discovery)"));
            Assert.That(error.Method, Is.EqualTo(TestDiscoveryCollector<FakeTest>.DiscoveryMethodName));
            Assert.That(error.Status, Is.EqualTo(TestStatus.Error));
            Assert.That(error.Message, Is.EqualTo("System.TypeLoadException: " + BrokenType().Message));
        }

        [Test]
        public void TypeThatThrowsMidEnumeration_KeepsNoPartialTests()
        {
            var c = Collector(null);

            c.InspectType("Ns.Half", () => Half());

            Assert.That(c.Tests, Is.Empty);
            Assert.That(c.Errors.Single().Class, Is.EqualTo("Ns.Half"));

            IEnumerable<FakeTest> Half()
            {
                yield return new FakeTest("Ns.Half", "First");
                throw BrokenType();
            }
        }

        [Test]
        public void ErrorInOneType_DoesNotStopOthers()
        {
            var c = Collector(null);

            c.InspectType("Ns.Broken", () => throw BrokenType());
            c.InspectType("Ns.Good", () => new[] { new FakeTest("Ns.Good", "Run") });

            Assert.That(c.Tests.Single().ClassFullName, Is.EqualTo("Ns.Good"));
            Assert.That(c.Errors.Single().Class, Is.EqualTo("Ns.Broken"));
        }

        [Test]
        public void LoadTypes_ReturnsAllTypes()
        {
            var c = Collector(null);

            var types = c.LoadTypes("Asm", () => new[] { typeof(string), typeof(int) });

            Assert.That(types, Is.EqualTo(new[] { typeof(string), typeof(int) }));
            Assert.That(c.Warnings, Is.Empty);
        }

        [Test]
        public void LoadTypes_ReflectionTypeLoadException_UsesLoadedTypesAndLogsTheRest()
        {
            var c = Collector(null);
            var loaderError = BrokenType();

            var types = c.LoadTypes("MyPlugin.RevitTests", () =>
                throw new ReflectionTypeLoadException(new[] { typeof(string), null, typeof(int), null }, new Exception[] { loaderError, loaderError }));

            Assert.That(types, Is.EqualTo(new[] { typeof(string), typeof(int) }));
            Assert.That(c.Errors, Is.Empty);
            var warning = c.Warnings.Single(); // одинаковые причины — одной строкой
            Assert.That(warning, Does.Contain("MyPlugin.RevitTests"));
            Assert.That(warning, Does.Contain("2"));
            Assert.That(warning, Does.Contain("System.TypeLoadException: " + loaderError.Message));
        }

        [Test]
        public void LoadTypes_ReflectionTypeLoadException_WithoutLoaderExceptions_StillLogs()
        {
            var c = Collector(null);

            var types = c.LoadTypes("Asm", () => throw new ReflectionTypeLoadException(new Type?[] { null, typeof(int) }, new Exception[0]));

            Assert.That(types, Is.EqualTo(new[] { typeof(int) }));
            Assert.That(c.Warnings.Single(), Does.Contain("Asm"));
        }

        [Test]
        public void LoadTypes_OtherException_SkipsAssemblyWithWarning()
        {
            var c = Collector(null);

            var types = c.LoadTypes("Asm", () => throw new InvalidOperationException("сборка повреждена"));

            Assert.That(types, Is.Empty);
            Assert.That(c.Warnings.Single(), Does.Contain("Asm").And.Contain("System.InvalidOperationException: сборка повреждена"));
        }

        [Test]
        public void AssemblyLoadFailed_LogsAssemblyAndReason()
        {
            var c = Collector(null);

            c.AssemblyLoadFailed(@"C:\Tests\Broken.Tests.dll", new BadImageFormatException("не .NET-сборка"));

            Assert.That(c.Warnings.Single(), Does.Contain(@"C:\Tests\Broken.Tests.dll").And.Contain("System.BadImageFormatException: не .NET-сборка"));
            Assert.That(c.Errors, Is.Empty);
        }
    }
}
