using System;
using System.IO;
using NUnit.Framework;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Tests.Server
{
    [TestFixture]
    public class UserTempTests
    {
        private static string LocalAppDataTemp =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");

        [Test]
        public void Resolve_UserValueSet_ReturnsIt()
        {
            Assert.That(UserTemp.Resolve(() => @"D:\UserTemp"), Is.EqualTo(@"D:\UserTemp"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Resolve_UserValueEmpty_FallsBackToLocalAppDataTemp(string? value)
        {
            Assert.That(UserTemp.Resolve(() => value), Is.EqualTo(LocalAppDataTemp));
        }

        [Test]
        public void Resolve_ExpandsEnvironmentVariables()
        {
            var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            Assume.That(userProfile, Is.Not.Null.And.Not.Empty, "USERPROFILE не задан");

            Assert.That(UserTemp.Resolve(() => @"%USERPROFILE%\AppData\Local\Temp"),
                Is.EqualTo(userProfile + @"\AppData\Local\Temp"));
        }

        [Test]
        public void Directory_IsRootedAndDoesNotDependOnProcessTemp()
        {
            var original = Environment.GetEnvironmentVariable("TEMP");
            try
            {
                // Как Revit: TEMP процесса указывает на папку сеанса
                Environment.SetEnvironmentVariable("TEMP", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
                Assert.That(Path.IsPathRooted(UserTemp.Directory), Is.True);
                Assert.That(UserTemp.Directory, Is.Not.EqualTo(Environment.GetEnvironmentVariable("TEMP")));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TEMP", original);
            }
        }
    }
}
