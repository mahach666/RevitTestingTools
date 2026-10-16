using NUnit.Framework;
using RevitTestHost.Core.Execution;

namespace RevitTestHost.Tests.Execution
{
    [TestFixture]
    public class TestFilterTests
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ;  ; ")]
        public void Empty_MatchesEverything(string? filter)
        {
            var f = TestFilter.Parse(filter);
            Assert.That(f.IsEmpty, Is.True);
            Assert.That(f.IsMatch("Any.Class.Method"), Is.True);
        }

        [TestCase("Ns.Class.*", "Ns.Class.Method", true)]
        [TestCase("ns.class.*", "Ns.Class.Method", true)]
        [TestCase("Ns.Class.*", "Ns.ClassOther.Method", false)]
        [TestCase("*XYZ*", "A.XYZExtensionTests.Rotate", true)]
        [TestCase("Ns.Class.Method", "Ns.Class.Method2", false)]
        [TestCase("A.*;B.*", "B.C.D", true)]
        [TestCase("A.(x)+.*", "A.(x)+.M", true)]
        [TestCase("A.(x)+.*", "A.xx.M", false)]
        public void Patterns(string filter, string name, bool expected)
        {
            Assert.That(TestFilter.Parse(filter).IsMatch(name), Is.EqualTo(expected));
        }

        [TestCase(null, "Any.Type", true)]
        [TestCase("Ns.Class.*", "Ns.Class", true)]
        [TestCase("ns.class.*", "Ns.Class", true)]
        [TestCase("Ns.Class.*", "Ns.ClassOther", false)]
        [TestCase("Ns.Class.*", "Ns", true)] // по префиксу не исключить: имя метода могло бы начинаться с "Class."
        [TestCase("Ns.Class.*", "Other", false)]
        [TestCase("Ns.Cl*", "Ns.ClassOther", true)]
        [TestCase("Ns.Class.Method", "Ns.Class", true)]
        [TestCase("Ns.Class.Method", "Ns.ClassX", false)]
        [TestCase("Ns.Class.Method", "Ns", true)] // "Ns" + метод "Class.Method" по одному префиксу не исключить
        [TestCase("*XYZ*", "MyPlugin.RevitTests.Foo", true)]
        [TestCase("RevitTestHost.SampleTests.*", "MyPlugin.RevitTests.Foo", false)]
        [TestCase("RevitTestHost.SampleTests.*", "RevitTestHost.SampleTests.ActiveDocumentProbe", true)]
        [TestCase("A.*;RevitTestHost.*", "RevitTestHost.SampleTests.Probe", true)]
        [TestCase("Ns.Outer+Inner.*", "Ns.Outer+Inner", true)]
        public void CouldMatchType(string? filter, string typeFullName, bool expected)
        {
            Assert.That(TestFilter.Parse(filter).CouldMatchType(typeFullName), Is.EqualTo(expected));
        }

        [TestCase("Ns.Class.*", "Ns.Class", "M")]
        [TestCase("Ns.Cl*.Run", "Ns.Class", "Run")]
        [TestCase("Ns.Class.Method", "Ns.Class", "Method")]
        [TestCase("*Probe*", "A.B", "Probe")]
        public void CouldMatchType_IsTrueWheneverSomeMethodMatches(string filter, string typeFullName, string method)
        {
            var f = TestFilter.Parse(filter);
            Assume.That(f.IsMatch(typeFullName + "." + method), Is.True);
            Assert.That(f.CouldMatchType(typeFullName), Is.True);
        }
    }
}
