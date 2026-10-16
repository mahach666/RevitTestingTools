using System;
using NUnit.Framework;
using RevitTestHost.Core.Protocol;

namespace RevitTestHost.Tests.Protocol
{
    [TestFixture]
    public class RequestArgsTests
    {
        [TestCase(@"C:\a.rvt")]
        [TestCase("C:/a.rvt")]
        [TestCase("d:\\Модели\\a.rvt")]
        [TestCase(@"\\server\share\a.rvt")]
        public void IsAbsolutePath_FullyQualified_IsTrue(string path)
        {
            Assert.That(ArgReader.IsAbsolutePath(path), Is.True);
        }

        [TestCase("C:a.rvt")]
        [TestCase("C:")]
        [TestCase(@"\a.rvt")]
        [TestCase("/a.rvt")]
        [TestCase(@"models\a.rvt")]
        [TestCase("a.rvt")]
        [TestCase("")]
        public void IsAbsolutePath_DriveRelativeRootRelativeOrRelative_IsFalse(string path)
        {
            Assert.That(ArgReader.IsAbsolutePath(path), Is.False);
        }

        [TestCase("C:\\a|b.rvt")]
        [TestCase("C:\\a\0b.rvt")]
        public void IsAbsolutePath_FullyQualifiedWithInvalidChars_IsBadRequest(string path)
        {
            var ex = Assert.Throws<HostCommandException>(() => ArgReader.IsAbsolutePath(path))!;
            Assert.That(ex.Code, Is.EqualTo(ErrorCodes.BadRequest));
            Assert.That(ex.Message, Does.Contain("некорректный путь"));
        }
    }

    [TestFixture]
    public class ExceptionTextTests
    {
        [Test]
        public void Describe_IsFullTypeNameAndMessage()
        {
            Assert.That(ExceptionText.Describe(new InvalidOperationException("Revit сломался")),
                Is.EqualTo("System.InvalidOperationException: Revit сломался"));
        }
    }
}
