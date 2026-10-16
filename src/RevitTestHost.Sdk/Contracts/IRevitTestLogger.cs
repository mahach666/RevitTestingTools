namespace RevitTestHost.Sdk.Contracts
{
    public interface IRevitTestLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }
}
