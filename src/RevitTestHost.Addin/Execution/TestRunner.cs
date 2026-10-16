using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTestHost.Addin.Discovery;
using RevitTestHost.Core.Reporting;
using RevitTestHost.Sdk.Contracts;

namespace RevitTestHost.Addin.Execution
{
    public static class TestRunner
    {
        public static TestResult Run(TestDescriptor descriptor, UIApplication uiApp, Document? document,
            IReadOnlyDictionary<string, string> parameters)
        {
            if (descriptor.SkipReason != null)
            {
                return new TestResult(descriptor.ClassFullName, descriptor.MethodName, TestStatus.Skipped, 0,
                    descriptor.SkipReason, model: descriptor.ModelPath);
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var instance = Activator.CreateInstance(descriptor.DeclaringType);
                if (descriptor.TakesContext)
                {
                    var ctx = new TestContext(uiApp, document, descriptor.MethodName, descriptor.ModelPath, parameters);
                    descriptor.Method.Invoke(instance, new object[] { ctx });
                }
                else
                {
                    descriptor.Method.Invoke(instance, null);
                }
                stopwatch.Stop();
                return new TestResult(descriptor.ClassFullName, descriptor.MethodName, TestStatus.Passed,
                    stopwatch.ElapsedMilliseconds, model: descriptor.ModelPath);
            }
            catch (System.Reflection.TargetInvocationException tex) when (tex.InnerException != null)
            {
                stopwatch.Stop();
                var inner = tex.InnerException;
                var typeName = inner.GetType().FullName ?? "";
                var status = typeName.Contains("Xunit") || typeName.Contains("Assertion") || typeName.Contains("FluentAssertions")
                    ? TestStatus.Failed
                    : TestStatus.Error;
                return new TestResult(descriptor.ClassFullName, descriptor.MethodName, status,
                    stopwatch.ElapsedMilliseconds, inner.Message, inner.StackTrace, descriptor.ModelPath);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new TestResult(descriptor.ClassFullName, descriptor.MethodName, TestStatus.Error,
                    stopwatch.ElapsedMilliseconds, ex.Message, ex.StackTrace, descriptor.ModelPath);
            }
        }
    }
}
