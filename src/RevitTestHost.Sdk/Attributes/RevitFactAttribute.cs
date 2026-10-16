using System;

namespace RevitTestHost.Sdk.Attributes
{
    /// <summary>
    /// Marks a method to be executed by RevitTestHost as a test without opening a model.
    /// Method signature must be: void TestName() or void TestName(ITestContext ctx).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class RevitFactAttribute : Attribute
    {
        /// <summary>Optional skip reason — non-null skips the test.</summary>
        public string? Skip { get; set; }
    }
}
