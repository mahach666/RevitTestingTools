using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;
using RevitUITesting.NUnit;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Contract
{
    /// <summary>
    /// Закрытие изменённого документа через UI: вопрос «сохранить изменения?» (доказательство для каталога) и RevitApp.CloseActiveDocument().
    /// Закрытие Revit с изменённой моделью проверяет конец сеанса (<see cref="ContractSession"/>).
    /// dotnet test --filter "Category=RevitContract&amp;FullyQualifiedName~CloseViaUiContractTests"
    /// </summary>
    [TestFixture]
    [Explicit("живой Revit (версия из revit-ui-tests.json, язык ENU)")]
    [Category("RevitContract")]
    [NonParallelizable]
    [RevitUiModel(RevitHostClient.NewModel)]
    public class CloseViaUiContractTests : RevitUiTestBase
    {
        private bool _threeDViewEvidenceWritten;

        private void ModifyActiveDocument()
        {
            if (!_threeDViewEvidenceWritten)
            {
                WriteThreeDViewEvidence();
                _threeDViewEvidenceWritten = true;
            }
            ContractModel.ModifyActiveDocument(App);
        }

        /// <summary>
        /// Доказательство до нажатия: элементы вкладки View с «3D» в Name или AutomationId и меню разделённой кнопки «3D View» (раскрыть Toggle,
        /// записать новые всплывающие окна ленты, закрыть Esc). Меню, оставшееся открытым после сбоя, закрывает возврат после теста.
        /// </summary>
        private void WriteThreeDViewEvidence()
        {
            AutomationElement flyout = App.Ribbon.Button(RevitTitles.Tabs.View, ContractModel.ThreeDViewFlyoutId);
            AutomationElement content = Wait.For(() => App.Ribbon.TabContent(RevitTitles.Tabs.View), Timeout, "содержимое вкладки View");
            IReadOnlyList<AutomationElement> found = content.FindAll(By.Where(f => f.Name.Contains("3D") || f.AutomationId.Contains("3D"), "элементы «3D» вкладки View"), RevitIds.Ribbon.SearchDepth);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"## Элементы вкладки View с «3D» в Name или AutomationId: {found.Count}");
            foreach (AutomationElement element in found)
            {
                sb.AppendLine(element.DumpTree(0, true).TrimEnd());
            }
            HashSet<IntPtr> before = App.Ribbon.PopupHandles();
            // только окна этого меню: после закрытия документа Revit показывает и другие окна класса Popup, которые Esc не закрывает
            // (прогон 4, подхват: возврат по «всем новым всплывающим окнам» ждал 10 с и упал)
            HashSet<IntPtr> menuWindows = new HashSet<IntPtr>();
            AfterTest("меню «3D View» закрыто", () =>
            {
                if (App.Ribbon.PopupHandles().Overlaps(menuWindows))
                {
                    App.SendKeys("Esc: закрыть меню «3D View» после теста", VirtualKeyShort.ESCAPE);
                    Wait.Until(() => !App.Ribbon.PopupHandles().Overlaps(menuWindows), TimeSpan.FromSeconds(10), "меню «3D View» закрыто");
                }
            });
            try
            {
                App.Ribbon.Open(flyout, "разделённая кнопка «3D View» (доказательство)");
                List<IntPtr> fresh = new List<IntPtr>();
                bool opened = Wait.Holds(() => (fresh = App.Ribbon.PopupHandles().Except(before).ToList()).Count > 0, TimeSpan.FromSeconds(10));
                menuWindows.UnionWith(fresh);
                List<AutomationElement> menus = App.Ribbon.RibbonPopups().Where(p => fresh.Contains(Handle(p))).ToList();
                sb.AppendLine($"## Меню после Toggle «{ContractModel.ThreeDViewFlyoutId}»: новых всплывающих окон ленты {fresh.Count} (появились за 10 с: {opened})");
                foreach (AutomationElement menu in menus)
                {
                    sb.AppendLine(menu.DumpTree(8, true).TrimEnd());
                }
                Assert.That(fresh, Is.Not.Empty, "Toggle «3D View» раскрыл меню");
                App.SendKeys("Esc: закрыть меню «3D View» (доказательство)", VirtualKeyShort.ESCAPE);
                bool closed = Wait.Holds(() => !App.Ribbon.PopupHandles().Overlaps(fresh), TimeSpan.FromSeconds(10));
                sb.AppendLine("Меню закрылось по Esc: " + closed);
                Assert.That(closed, Is.True, "Esc закрывает меню «3D View»");
            }
            finally
            {
                Evidence.Write("view-3d", sb.ToString());
            }
        }

        private static IntPtr Handle(AutomationElement window)
        {
            return UiQuery.Read(() => window.Properties.NativeWindowHandle.Value, TimeSpan.FromSeconds(30), "HWND окна");
        }

        [Test]
        [Order(1)]
        public void Ui_close_of_modified_document_answers_save_changes()
        {
            ModifyActiveDocument();
            string title = Model.Title;
            HashSet<IntPtr> before = new HashSet<IntPtr>(App.Windows.All().Select(Handle));
            AutomationElement prompt = null;
            for (int press = 0; press < 10 && prompt == null; press++)
            {
                App.SendKeys($"Ctrl+F4 ({press + 1}): закрыть вид документа «{title}»", VirtualKeyShort.CONTROL, VirtualKeyShort.F4);
                Wait.Holds(() => (prompt = App.Windows.All().FirstOrDefault(w => !before.Contains(Handle(w)))) != null, TimeSpan.FromSeconds(5));
            }
            Assert.That(prompt, Is.Not.Null, "за 10 нажатий Ctrl+F4 вопрос о сохранении не появился");

            RevitTaskDialog dialog = new RevitTaskDialog(prompt);
            StringBuilder evidence = new StringBuilder();
            evidence.AppendLine("Title: " + dialog.Title);
            evidence.AppendLine("ClassName: " + prompt.SafeClassName());
            evidence.AppendLine("Text: " + dialog.Text);
            evidence.AppendLine("Buttons: " + string.Join(" | ", dialog.Buttons()));
            evidence.AppendLine("Главное окно доступно: " + App.Windows.IsMainWindowEnabled);
            evidence.AppendLine(prompt.DumpTree(10, true));
            Evidence.Write("save-changes", evidence.ToString());

            Assert.That(RevitDialogs.Match(App.Windows.Catalog, dialog.Title, dialog.Text), Is.SameAs(RevitDialogs.SaveChanges), "вопрос о сохранении узнаётся каталогом");
            Assert.That(prompt.SafeClassName(), Is.EqualTo(RevitIds.Dialogs.TaskDialogClass));
            // «No» — по id кнопки через каталог (уборка окон): подписи кнопок не следуют языку Revit («Да | Нет | Отмена» в save-changes-2022.txt)
            DateTime savedAt = File.GetLastWriteTimeUtc(Model.Path);
            UiResetReport answered = App.Windows.ResetUi();
            Assert.That(answered.Clean, Is.True, answered.ToString());
            Assert.That(answered.Actions.Single(), Does.Contain("TDM_CLICK_BUTTON " + RevitIds.Dialogs.TaskDialogNoButtonId).And.Contain(RevitDialogs.SaveChanges.Name));
            Wait.Until(() => Host.Documents().All(d => d.Title != title), TimeSpan.FromSeconds(60), "документ закрыт");
            Assert.That(File.GetLastWriteTimeUtc(Model.Path), Is.EqualTo(savedAt), "ответ «No»: файл модели не перезаписан");
        }

        [Test]
        [Order(2)]
        public void CloseActiveDocument_closes_modified_document_without_saving()
        {
            HostDocument doc = Host.Open(RevitHostClient.NewModel, false);
            ModifyActiveDocument();

            App.CloseActiveDocument();

            Assert.That(Host.Documents().Select(d => d.Title), Has.No.Member(doc.Title));
        }
    }
}
