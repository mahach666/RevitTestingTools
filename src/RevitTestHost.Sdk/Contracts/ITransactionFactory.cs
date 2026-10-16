using System;

namespace RevitTestHost.Sdk.Contracts
{
    /// <summary>
    /// Factory for transactions that auto-rollback by default.
    /// </summary>
    public interface ITransactionFactory
    {
        /// <summary>
        /// Start a transaction that will ALWAYS roll back on Dispose. For tests
        /// that read or temporarily mutate the document but must not leave state behind.
        /// Throws if <see cref="ITestContext.Document"/> is null (no document loaded).
        /// </summary>
        IDisposable Begin(string name = "test");
    }
}
