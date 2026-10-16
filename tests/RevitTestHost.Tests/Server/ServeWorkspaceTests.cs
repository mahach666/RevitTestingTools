using System;
using System.IO;
using NUnit.Framework;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Tests.Server
{
    [TestFixture]
    public class ServeWorkspaceTests
    {
        [TestCase("RevitTestHost-0123456789abcdef0123456789abcdef", true)]
        [TestCase("a_b-C9", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("..", false)]
        [TestCase("a\\b", false)]
        [TestCase("a/b", false)]
        [TestCase("a b", false)]
        [TestCase("канал", false)]
        public void IsValidPipeName(string? name, bool expected)
        {
            Assert.That(ServeWorkspace.IsValidPipeName(name), Is.EqualTo(expected));
        }

        [Test]
        public void IsValidPipeName_RejectsTooLong()
        {
            Assert.That(ServeWorkspace.IsValidPipeName(new string('a', 128)), Is.True);
            Assert.That(ServeWorkspace.IsValidPipeName(new string('a', 129)), Is.False);
        }

        [Test]
        public void ForPipe_IsUnderUserTempRevitTestHostServe()
        {
            var ws = ServeWorkspace.ForPipe("RevitTestHost-abc");

            Assert.That(ws.RootDirectory, Is.EqualTo(Path.Combine(UserTemp.Directory, "RevitTestHost", "serve", "RevitTestHost-abc")));
            Assert.That(ws.LogPath, Is.EqualTo(Path.Combine(ws.RootDirectory, "server.log")));
        }

        [Test]
        public void StartupLogPath_IsUnderUserTempRevitTestHostServe()
        {
            Assert.That(ServeWorkspace.StartupLogPath,
                Is.EqualTo(Path.Combine(UserTemp.Directory, "RevitTestHost", "serve", "startup.log")));
        }

        [Test]
        public void ForPipe_InvalidName_Throws()
        {
            Assert.Throws<ArgumentException>(() => ServeWorkspace.ForPipe(@"..\..\Windows"));
        }

        [Test]
        public void CopyModel_MissingSource_ThrowsFileNotFound()
        {
            var ws = new ServeWorkspace(Path.Combine(Path.GetTempPath(), "RevitTestHost.Tests", Guid.NewGuid().ToString("N")));
            Assert.Throws<FileNotFoundException>(() => ws.CopyModel(@"C:\definitely\missing\model.rvt"));
        }
    }
}
