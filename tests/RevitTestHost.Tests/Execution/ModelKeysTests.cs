using System;
using System.Collections.Generic;
using NUnit.Framework;
using RevitTestHost.Core.Execution;
using RevitTestHost.Sdk.Attributes;

namespace RevitTestHost.Tests.Execution
{
    [TestFixture]
    public class ModelKeysTests
    {
        [Test]
        public void Keys_MatchSdkConstants()
        {
            Assert.That(ModelKeys.NewProject, Is.EqualTo(RevitTestAttribute.NewProject));
            Assert.That(ModelKeys.ActiveDocument, Is.EqualTo(RevitTestAttribute.ActiveDocument));
            Assert.That(RevitTestAttribute.ActiveDocument, Is.EqualTo("@active"));
        }

        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("   ", null)]
        [TestCase("@new", "@new")]
        [TestCase("@active", "@active")]
        [TestCase(@"D:\Models\a.rvt", @"D:\Models\a.rvt")]
        [TestCase(@"TestModels\a.rvt", @"C:\Tests\TestModels\a.rvt")]
        [TestCase("TestModels/a.rvt", @"C:\Tests\TestModels\a.rvt")]
        public void Resolve(string? model, string? expected)
        {
            Assert.That(ModelKeys.Resolve(model, @"C:\Tests"), Is.EqualTo(expected));
        }

        [Test]
        public void Resolve_WithoutAssemblyDirectory_KeepsRelativePath()
        {
            Assert.That(ModelKeys.Resolve("a.rvt", null), Is.EqualTo("a.rvt"));
        }

        [Test]
        public void EmptyParameters_AreEmptyAndReadOnly()
        {
            Assert.That(RunParameters.Empty, Is.Empty);
            Assert.That(RunParameters.Empty, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyDictionary<string, string>>());
        }

        [Test]
        public void From_CopiesSourceAndIsCaseSensitive()
        {
            var source = new Dictionary<string, string> { ["family"] = "Ванна" };
            var copy = RunParameters.From(source);
            source["family"] = "изменено";

            Assert.That(copy["family"], Is.EqualTo("Ванна"));
            Assert.That(copy.ContainsKey("Family"), Is.False);
        }

        [Test]
        public void From_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => RunParameters.From(null!));
        }
    }
}
