using System;

namespace RevitTestHost.Sdk.Attributes
{
    /// <summary>
    /// Marks a method to be executed by RevitTestHost as a test with a model opened.
    /// Method signature must be: void TestName() or void TestName(ITestContext ctx).
    /// The model stays open between tests that share the same path (cache), and is
    /// closed without saving when the run finishes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class RevitTestAttribute : Attribute
    {
        /// <summary>
        /// Special <see cref="Model"/> value: create a fresh metric project document
        /// (no .rvt file needed). The document is discarded when the run finishes.
        /// </summary>
        public const string NewProject = "@new";

        /// <summary>
        /// Особое значение <see cref="Model"/>: тест работает с документом, активным в UI Revit.
        /// Хост его не открывает и не закрывает. Нет активного документа — тест получает статус
        /// <c>error</c> с понятным сообщением.
        /// </summary>
        public const string ActiveDocument = "@active";

        /// <summary>
        /// Path to the .rvt model, relative to the test assembly location
        /// (e.g. "TestModels/walls_basic.rvt"), <see cref="NewProject"/> or <see cref="ActiveDocument"/>.
        /// Falls back to the class-level <see cref="TestModelAttribute"/> when null.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>Optional skip reason — non-null skips the test.</summary>
        public string? Skip { get; set; }
    }
}
