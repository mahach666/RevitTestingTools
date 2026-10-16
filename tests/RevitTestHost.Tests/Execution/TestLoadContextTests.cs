#if NET
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using NUnit.Framework;
using RevitTestHost.Core.Execution;
using RevitTestHost.Sdk.Attributes;

namespace RevitTestHost.Tests.Execution
{
    [TestFixture]
    public class TestLoadContextTests
    {
        private static string FixturePath =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "RevitTestHost.Tests.Fixtures.dll");

        private static string SdkPath => typeof(RevitFactAttribute).Assembly.Location;

        private const string FixtureClass = "RevitTestHost.Tests.Fixtures.FixtureTests";
        private const string FactAttributeType = "RevitTestHost.Sdk.Attributes.RevitFactAttribute";

        /// <summary>Надстройка в Revit 2025 и новее может жить в своём контексте со своей копией Sdk (изоляция надстроек).</summary>
        private static Assembly LoadHostSdkInSeparateContext()
        {
            return new AssemblyLoadContext("host-simulation").LoadFromAssemblyPath(SdkPath);
        }

        private static MethodInfo FixtureMethod(Assembly fixture)
        {
            return fixture.GetType(FixtureClass, throwOnError: true)!.GetMethod("Passes")!;
        }

        [Test]
        public void Test_assembly_sees_the_sdk_of_the_host_not_its_own_copy()
        {
            Assembly hostSdk = LoadHostSdkInSeparateContext();
            var context = new TestLoadContext(FixturePath, new[] { hostSdk });

            Assembly fixture = context.LoadFromAssemblyPath(FixturePath);

            Type hostFact = hostSdk.GetType(FactAttributeType, throwOnError: true)!;
            Assert.That(FixtureMethod(fixture).GetCustomAttributes(hostFact, false), Has.Length.EqualTo(1),
                "атрибут тестовой сборки должен быть типом из Sdk надстройки");
        }

        [Test]
        public void Without_shared_assemblies_the_sdk_identity_differs()
        {
            // Контроль: без общей сборки Sdk тестовая сборка получает другую копию, и тест выше не был бы показательным
            Assembly hostSdk = LoadHostSdkInSeparateContext();
            var context = new TestLoadContext(FixturePath, Array.Empty<Assembly>());

            Assembly fixture = context.LoadFromAssemblyPath(FixturePath);

            Type hostFact = hostSdk.GetType(FactAttributeType, throwOnError: true)!;
            Assert.That(FixtureMethod(fixture).GetCustomAttributes(hostFact, false), Is.Empty);
        }

        [Test]
        public void Load_probes_dependencies_next_to_the_test_assembly()
        {
            var context = new TestLoadContext(FixturePath, Array.Empty<Assembly>());

            Assembly byName = context.LoadFromAssemblyName(new AssemblyName("RevitTestHost.Tests.Fixtures"));

            Assert.That(byName.Location, Is.EqualTo(FixturePath));
        }

        [Test]
        public void ProbeDirectory_finds_a_dll_by_simple_name()
        {
            string directory = Path.GetDirectoryName(FixturePath)!;

            Assert.That(TestLoadContext.ProbeDirectory(directory, "RevitTestHost.Tests.Fixtures"), Is.EqualTo(FixturePath));
        }

        [Test]
        public void ProbeDirectory_returns_null_when_there_is_no_such_dll()
        {
            string directory = Path.GetDirectoryName(FixturePath)!;

            Assert.That(TestLoadContext.ProbeDirectory(directory, "No.Such.Assembly"), Is.Null);
        }
    }
}
#endif
