using NUnit.Framework;
using RevitTestHost.Core.Protocol;

namespace RevitTestHost.Tests.Protocol
{
    [TestFixture]
    public class DocumentLookupTests
    {
        [TestCase(@"C:\Модели\A.rvt", @"c:\модели\a.RVT")]
        [TestCase(@"C:\Models\A.rvt", "C:/Models/A.rvt")]
        [TestCase(@"C:\Models\A.rvt", @"C:\Models\sub\..\A.rvt")]
        [TestCase(@"\server\share\A.rvt", @"\SERVER\share\a.rvt")]
        public void IsSamePath_SameFileAfterNormalization_IsTrue(string documentPath, string modelPath)
        {
            Assert.That(DocumentLookup.IsSamePath(documentPath, modelPath), Is.True);
        }

        [TestCase(@"C:\Models\A.rvt", @"C:\Models\B.rvt")]
        [TestCase(@"C:\Models\A.rvt", @"D:\Models\A.rvt")]
        [TestCase("", @"C:\Models\A.rvt")]
        [TestCase("   ", @"C:\Models\A.rvt")]
        public void IsSamePath_DifferentFileOrUnsavedDocument_IsFalse(string documentPath, string modelPath)
        {
            Assert.That(DocumentLookup.IsSamePath(documentPath, modelPath), Is.False);
        }
    }
}
