using System;
using System.IO;
using NUnit.Framework;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Tests.Server
{
    [TestFixture]
    public class FileServerLogTests
    {
        private string _dir = null!;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "RevitTestHost.Tests", Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void CreatesFolderAndAppendsUtf8LinesWithoutBom()
        {
            var path = Path.Combine(_dir, "nested", "server.log");
            var log = new FileServerLog(path);

            log.Info("Клиент подключился.");
            log.Error("Сбой", new InvalidOperationException("подробности"));

            var bytes = File.ReadAllBytes(path);
            Assert.That(bytes.Length, Is.GreaterThan(3));
            Assert.That(bytes[0], Is.Not.EqualTo((byte)0xEF), "BOM в журнале не нужен");
            var text = File.ReadAllText(path);
            Assert.That(text, Does.Contain("[INFO] Клиент подключился."));
            Assert.That(text, Does.Contain("[ERROR] Сбой"));
            Assert.That(text, Does.Contain("подробности"));
        }

        [Test]
        public void LockedFile_DoesNotThrow()
        {
            var path = Path.Combine(_dir, "server.log");
            var log = new FileServerLog(path);
            using (new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.DoesNotThrow(() => log.Info("запись при занятом файле уходит в Trace"));
            }
        }
    }
}
