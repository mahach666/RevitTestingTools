using System;

namespace RevitTestHost.Core.Reporting
{
    public enum TestStatus
    {
        Passed,
        Failed,
        Skipped,
        Error
    }

    public sealed class TestResult
    {
        public string Class { get; }
        public string Method { get; }
        public TestStatus Status { get; }
        public long DurationMs { get; }
        public string? Message { get; }
        public string? StackTrace { get; }

        /// <summary>Model the test ran against (attribute value resolved to a path, "@new", or null).</summary>
        public string? Model { get; }

        public TestResult(string @class, string method, TestStatus status, long durationMs, string? message = null, string? stackTrace = null, string? model = null)
        {
            Class = @class;
            Method = method;
            Status = status;
            DurationMs = durationMs;
            Message = message;
            StackTrace = stackTrace;
            Model = model;
        }
    }
}
