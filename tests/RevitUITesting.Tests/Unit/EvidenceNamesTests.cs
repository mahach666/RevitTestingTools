using NUnit.Framework;
using RevitUITesting.Tests.Contract;

namespace RevitUITesting.Tests.Unit
{
    [TestFixture]
    public class EvidenceNamesTests
    {
        [TestCase("view-3d", "2024", "view-3d-2024.txt")]
        [TestCase("ribbon-collapsed-click", "2020", "ribbon-collapsed-click-2020.txt")]
        [TestCase("main-window", "2022", "main-window-2022.txt")]
        public void File_name_carries_the_revit_year(string baseName, string year, string expected)
        {
            Assert.That(Evidence.FileName(baseName, year), Is.EqualTo(expected));
        }
    }
}
