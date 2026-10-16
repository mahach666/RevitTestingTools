using System;

namespace RevitTestHost.Sdk.Attributes
{
    /// <summary>
    /// Class-level default model for all [RevitTest] methods in the class
    /// that don't specify their own Model.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class TestModelAttribute : Attribute
    {
        public TestModelAttribute(string model)
        {
            Model = model;
        }

        /// <summary>Path relative to the test assembly location, RevitTestAttribute.NewProject or RevitTestAttribute.ActiveDocument.</summary>
        public string Model { get; }
    }
}
