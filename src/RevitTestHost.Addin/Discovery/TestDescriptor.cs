using System;
using System.Reflection;
using RevitTestHost.Core.Execution;

namespace RevitTestHost.Addin.Discovery
{
    /// <summary>Single discovered test = class + method to invoke.</summary>
    public sealed class TestDescriptor : ITestCase
    {
        public Type DeclaringType { get; }
        public MethodInfo Method { get; }
        public string? SkipReason { get; }
        public bool TakesContext { get; }

        /// <summary>
        /// Absolute path to the .rvt model, RevitTestAttribute.NewProject ("@new"),
        /// RevitTestAttribute.ActiveDocument ("@active"), or null for tests that run without a document ([RevitFact]).
        /// </summary>
        public string? ModelPath { get; }

        public TestDescriptor(Type declaringType, MethodInfo method, string? skipReason, bool takesContext,
            string? modelPath = null)
        {
            DeclaringType = declaringType;
            Method = method;
            SkipReason = skipReason;
            TakesContext = takesContext;
            ModelPath = modelPath;
        }

        public string ClassFullName => DeclaringType.FullName ?? DeclaringType.Name;
        public string MethodName => Method.Name;
    }
}
