using System;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using RevitUITesting.NUnit;
using RevitUITesting.Revit;

namespace RevitUITesting.Tests.Contract
{
    /// <summary>
    /// Revit сеанса контрактных тестов закрывается штатно после всех тестов пространства имён (без него — Kill в ProcessExit).
    /// Конец сеанса — контракт закрытия с открытой изменённой моделью (задача 12, D-3): перед RevitSession.End открывается новый проект и
    /// изменяется. Revit, запущенный сеансом без keepRevitOpen, закрывается Quit — проверяется, что вопрос о сохранении отвечен и Kill не было.
    /// Подхваченный или оставляемый Revit не закрывается: изменённый документ ждёт следующего Quit (RevitAppContractTests.Attach_and_quit
    /// проверяет то же). Изменённый документ появляется только здесь — классам тестов он не мешает.
    /// </summary>
    [SetUpFixture]
    public class ContractSession
    {
        [OneTimeTearDown]
        public void EndRevitSession()
        {
            RevitApp app = RevitSession.Current;
            if (app == null || !app.IsAlive)
            {
                RevitSession.End();
                return;
            }
            Exception leave = null;
            try
            {
                HostDocument doc = app.Host.Open(RevitHostClient.NewModel, false);
                ContractModel.ModifyActiveDocument(app);
                UiLog.Action($"конец сеанса: открыт изменённый документ «{doc.Title}» — с ним Revit закрывает RevitSession.End или, если Revit подхвачен или оставляется, следующий Quit");
            }
            catch (Exception ex)
            {
                // Revit сеанса закрывается в любом случае; сбой подготовки проверки пробрасывается после закрытия
                leave = ex;
            }
            if (app.StartedByUs && !app.Config.KeepRevitOpen)
            {
                ContractModel.AssertClosedWithoutKill(app, RevitSession.End);
            }
            else
            {
                RevitSession.End();
            }
            if (leave != null)
            {
                ExceptionDispatchInfo.Capture(leave).Throw();
            }
        }
    }
}
