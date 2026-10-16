using System;
using RevitTestHost.Sdk.Attributes;
using RevitTestHost.Sdk.Contracts;

namespace RevitTestHost.SampleTests
{
    /// <summary>
    /// Проба для режима сервера: тест работает с документом, активным в UI, и сверяет его заголовок
    /// с параметром <c>expectedTitle</c> из команды <c>run</c>.
    /// </summary>
    public class ActiveDocumentProbe
    {
        [RevitTest(Model = RevitTestAttribute.ActiveDocument)]
        public void TitleMatchesParameter(ITestContext ctx)
        {
            if (ctx.Document == null)
                throw new SampleAssertionException("Для Model = \"@active\" ctx.Document не должен быть null.");
            if (ctx.ModelPath != RevitTestAttribute.ActiveDocument)
                throw new SampleAssertionException($"ctx.ModelPath = '{ctx.ModelPath}', ожидалось '@active'.");
            if (!ctx.Parameters.TryGetValue("expectedTitle", out var expected))
                throw new SampleAssertionException("Нет параметра 'expectedTitle' (передайте его в run.parameters).");
            if (!string.Equals(ctx.Document.Title, expected, StringComparison.OrdinalIgnoreCase))
                throw new SampleAssertionException($"Активный документ '{ctx.Document.Title}', ожидался '{expected}'.");
        }
    }

    /// <summary>Провал проверки: имя типа содержит «Assertion», поэтому раннер ставит статус failed, а не error.</summary>
    public sealed class SampleAssertionException : Exception
    {
        public SampleAssertionException(string message) : base(message)
        {
        }
    }
}
