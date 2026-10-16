using NUnit.Framework;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Tests.Server
{
    [TestFixture]
    public class HostModeSelectorTests
    {
        [Test]
        public void NoVariables_IsNone()
        {
            var d = HostModeSelector.Select(null, null);
            Assert.That(d.Mode, Is.EqualTo(HostMode.None));
            Assert.That(d.Error, Is.Null);
        }

        [Test]
        public void CliOnly_IsCli()
        {
            var d = HostModeSelector.Select("1", null);
            Assert.That(d.Mode, Is.EqualTo(HostMode.Cli));
            Assert.That(d.Error, Is.Null);
        }

        [TestCase("0")]
        [TestCase("true")]
        [TestCase("")]
        public void CliNotOne_IsNotCli(string cli)
        {
            Assert.That(HostModeSelector.Select(cli, null).Mode, Is.EqualTo(HostMode.None));
        }

        [Test]
        public void ServeOnly_IsServeWithPipeName()
        {
            var d = HostModeSelector.Select(null, "RevitTestHost-0123abcd");
            Assert.That(d.Mode, Is.EqualTo(HostMode.Serve));
            Assert.That(d.PipeName, Is.EqualTo("RevitTestHost-0123abcd"));
            Assert.That(d.Error, Is.Null);
        }

        [Test]
        public void ServeWithSurroundingSpaces_IsTrimmed()
        {
            Assert.That(HostModeSelector.Select(null, " RevitTestHost-1 ").PipeName, Is.EqualTo("RevitTestHost-1"));
        }

        [Test]
        public void Both_IsCliWithError()
        {
            var d = HostModeSelector.Select("1", "RevitTestHost-1");
            Assert.That(d.Mode, Is.EqualTo(HostMode.Cli));
            Assert.That(d.PipeName, Is.Null);
            Assert.That(d.Error, Does.Contain("REVITTESTHOST_CLI").And.Contain("REVITTESTHOST_SERVE"));
        }

        [TestCase(@"..\evil")]
        [TestCase("имя")]
        [TestCase("a b")]
        public void InvalidPipeName_IsNoneWithError(string name)
        {
            var d = HostModeSelector.Select(null, name);
            Assert.That(d.Mode, Is.EqualTo(HostMode.None));
            Assert.That(d.Error, Does.Contain("Недопустимое имя канала"));
        }

        [Test]
        public void VariableNames_MatchContract()
        {
            Assert.That(HostModeSelector.CliVariable, Is.EqualTo("REVITTESTHOST_CLI"));
            Assert.That(HostModeSelector.ServeVariable, Is.EqualTo("REVITTESTHOST_SERVE"));
        }
    }
}
