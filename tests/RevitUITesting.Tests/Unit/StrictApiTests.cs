using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>
    /// Из библиотеки-образца перенесено только строгое API: члены 0.1, которые проглатывают ошибки или повторяют действия, в библиотеку
    /// не попадают (их возвращение — осознанное решение с правкой этого теста).
    /// </summary>
    [TestFixture]
    public class StrictApiTests
    {
        [TestCase(typeof(Wait), "TryUntil")]
        [TestCase(typeof(Wait), "Retry")]
        [TestCase(typeof(Wait), "IsUiBusy")]
        [TestCase(typeof(AutomationElementExtensions), "FindDescendantBfs")]
        [TestCase(typeof(AutomationElementExtensions), "FindDescendantsBfs")]
        [TestCase(typeof(AutomationElementExtensions), "ChildById")]
        [TestCase(typeof(AutomationElementExtensions), "ByNameContains")]
        [TestCase(typeof(AutomationElementExtensions), "TryById")]
        [TestCase(typeof(AutomationElementExtensions), "TryByName")]
        [TestCase(typeof(AutomationElementExtensions), "TryByControlTypeAndName")]
        [TestCase(typeof(AutomationElementExtensions), "Press")]
        [TestCase(typeof(AutomationElementExtensions), "SelectItem")]
        [TestCase(typeof(AutomationElementExtensions), "SetText")]
        [TestCase(typeof(AutomationElementExtensions), "AllRows")]
        [TestCase(typeof(AutomationElementExtensions), "FindRowScrolling")]
        public void Legacy_member_is_not_ported(Type type, string member)
        {
            Assert.That(type.GetMember(member, BindingFlags.Public | BindingFlags.Static), Is.Empty, $"{type.Name}.{member} — API 0.1 не переносится");
        }

        [Test]
        public void Waits_and_searches_with_explicit_timeout_are_strict_overloads_only()
        {
            Assert.That(typeof(AutomationElementExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(TimeSpan) || p.ParameterType == typeof(TimeSpan?))).Select(m => m.Name),
                Is.EquivalentTo(new[] { "Find" }), "ById/ByName/RowContaining с TimeSpan из 0.1 не переносятся; таймаут — только у Find(By, TimeSpan?)");
            Assert.That(typeof(Wait).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.GetParameters().Length > 1 && m.GetParameters()[1].ParameterType == typeof(TimeSpan)).Select(m => m.Name),
                Is.EquivalentTo(new[] { "Until", "For", "Stays" }));
        }
    }
}
