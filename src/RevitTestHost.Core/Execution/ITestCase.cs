namespace RevitTestHost.Core.Execution
{
    /// <summary>Найденный тест так, как его видит конвейер прогона (без Revit-типов).</summary>
    public interface ITestCase
    {
        string ClassFullName { get; }
        string MethodName { get; }

        /// <summary>Абсолютный путь .rvt, «@new», «@active» или null (тест без модели).</summary>
        string? ModelPath { get; }
    }
}
