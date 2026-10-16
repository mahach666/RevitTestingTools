namespace RevitUITesting.Tests.Contract
{
    /// <summary>Образец Revit-теста для run @active (samples/RevitTestHost.SampleTests, развёртывается Debug-сборкой примера).</summary>
    internal static class ContractSamples
    {
        /// <summary>Фильтр образца.</summary>
        public const string Filter = "RevitTestHost.SampleTests.*";

        /// <summary>Имя метода образца: сверяет заголовок активного документа с параметром expectedTitle.</summary>
        public const string TitleProbe = "TitleMatchesParameter";

        public const string ExpectedTitle = "expectedTitle";
    }
}
