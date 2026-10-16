using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using MouseButton = FlaUI.Core.Input.MouseButton;
using NUnit.Framework;
using RevitUITesting.NUnit;
using RevitUITesting.Pages;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Contract
{
    /// <summary>
    /// Модель класса @new, простой Revit, run @active с параметрами, панель Properties, уборка неизвестного и известного модального окна.
    /// dotnet test --filter "Category=RevitContract&amp;FullyQualifiedName~HostContractTests"
    /// </summary>
    [TestFixture]
    [Explicit("живой Revit (версия из revit-ui-tests.json, язык ENU) и пример RevitTestHost.SampleTests")]
    [Category("RevitContract")]
    [NonParallelizable]
    [RevitUiModel(RevitHostClient.NewModel)]
    public class HostContractTests : RevitUiTestBase
    {
        /// <summary>
        /// [Р] Evidence/properties-pane-2022.txt (прогон 1): ComboBox (ClassName «ComboBox», AutomationId «3119») выбора элемента в панели
        /// Properties, Value — текущий вид («Structural Plan: Level 1»). Тексты панели (<c>Texts()</c>, узлы Text с Name) пусты: Text «12520»
        /// без Name, а сетка свойств (Pane «GXWND», id «1102») строк в UI Automation не выставляет — только полосу прокрутки.
        /// </summary>
        private const string PropertiesSelectorId = "3119";

        [Test]
        [Order(1)]
        public void New_model_is_saved_to_serve_folder_and_active()
        {
            Assert.That(Model.Path, Does.Contain(@"\RevitTestHost\serve\").IgnoreCase);
            Assert.That(Host.Ping().ActiveDocument.Title, Is.EqualTo(Model.Title));
            Assert.That(Host.Documents().Single(d => d.IsActive).Title, Is.EqualTo(Model.Title));
        }

        [Test]
        [Order(2)]
        public void Idle_becomes_true_after_quiet_period()
        {
            Thread.Sleep(TimeSpan.FromSeconds(3));
            HostPing ping = Host.Ping();
            Assert.That(ping.Idle, Is.True, "после 3 с без запросов Revit должен простаивать; иначе Idling не срабатывает после внешнего события — дефект режима сервера RevitTestHost");
            Assert.That(Host.WaitIdle().Idle, Is.True);
        }

        [Test]
        [Order(3)]
        public void Run_active_with_expected_title_passes()
        {
            AssertInRevit(ContractSamples.Filter, new Dictionary<string, string> { [ContractSamples.ExpectedTitle] = Model.Title });
        }

        [Test]
        [Order(4)]
        public void Run_active_with_wrong_title_is_reported_as_failure()
        {
            Dictionary<string, string> wrong = new Dictionary<string, string> { [ContractSamples.ExpectedTitle] = "нет такого документа" };

            HostRunReport report = Host.Run(ContractSamples.Filter, wrong);

            Assert.That(report.Failed + report.Errored, Is.EqualTo(1));
            Assert.That(report.Failures.Single().Method, Is.EqualTo(ContractSamples.TitleProbe));
            Assert.That(() => Host.AssertRun(ContractSamples.Filter, wrong), Throws.TypeOf<AssertionException>().With.Message.Contains(ContractSamples.TitleProbe));
        }

        /// <summary>
        /// Панель Properties: найти, скрыть и показать снова кликом по переключателю ленты (Modify › Properties); набранные клавиши (PP) зависят
        /// от языка ввода и в задаче 12 панель не скрыли. Возврат «показать Properties» регистрируется до скрытия и идёт тем же путём: упавший
        /// между скрытием и показом тест не оставляет панель скрытой в настройках Revit пользователя.
        /// </summary>
        [Test]
        [Order(5)]
        public void Properties_pane_is_found_hidden_and_shown_again()
        {
            Step("Найти панель Properties (полный поиск)");
            DockablePanePage pane = App.Panes.Find(RevitTitles.Panes.Properties);
            Step("Dump(8) запомненной панели");
            StringBuilder evidence = new StringBuilder();
            evidence.AppendLine("## Показанная панель «Properties» (DockablePanePage.Dump(8))");
            evidence.AppendLine(pane.Dump(8));
            Evidence.Write("properties-pane", evidence.ToString());
            Step("ValueOf выбора элемента запомненной панели");
            Assert.That(pane.ValueOf(PropertiesSelectorId), Is.Not.Empty, "содержимое панели читается: выбор элемента показывает текущий вид или элемент");
            Step("Переключатель Properties и скрытие");

            Assert.That(PropertiesToggleState(), Is.EqualTo(ToggleState.On), "переключатель показанной панели — On");

            AfterTest("показать Properties", () =>
            {
                if (App.Panes.TryFind(RevitTitles.Panes.Properties) != null)
                {
                    return;
                }
                ClickPropertiesToggle("показать Properties после теста");
                Wait.Until(() => App.Panes.TryFind(RevitTitles.Panes.Properties) != null, TimeSpan.FromSeconds(15), "панель Properties показана");
            });

            ClickPropertiesToggle("скрыть Properties");
            bool hidden = Wait.Holds(() => App.Panes.TryFind(RevitTitles.Panes.Properties) == null, TimeSpan.FromSeconds(15));
            ToggleState hiddenState = PropertiesToggleState();
            evidence.AppendLine($"## После клика по переключателю «{RevitIds.Panes.PropertiesToggle}»: показанной панели нет за 15 с — {hidden}; ToggleState {hiddenState}");
            evidence.AppendLine(DescribeFrames());
            Evidence.Write("properties-pane", evidence.ToString());
            Assert.That(hidden, Is.True, "панель Properties скрыта переключателем ленты");
            Assert.That(hiddenState, Is.EqualTo(ToggleState.Off), "переключатель скрытой панели — Off");
            Step("Запомненная панель после скрытия");
            Assert.That(pane.IsShown, Is.False, "запомненная страница видит, что рамка скрытой панели исчезла или ушла за экран");

            Step("Показать Properties");
            ClickPropertiesToggle("показать Properties");
            Assert.That(App.Panes.Find(RevitTitles.Panes.Properties).IsShown, Is.True);
            Assert.That(PropertiesToggleState(), Is.EqualTo(ToggleState.On), "переключатель снова показанной панели — On");
            Step("Запомненная панель после показа");
            Assert.That(pane.IsShown, Is.True, "запомненная страница переразрешилась на рамку снова показанной панели");
            Step("ValueOf выбора элемента переразрешённой панели");
            Assert.That(pane.ValueOf(PropertiesSelectorId), Is.Not.Empty, "содержимое снова показанной панели читается через запомненную страницу");
            Step("Конец теста Properties");
        }

        /// <summary>Переключатель панели Properties на вкладке Modify (Evidence/properties-toggle-2022.txt).</summary>
        private AutomationElement PropertiesToggle()
        {
            return App.Ribbon.Button(RevitTitles.Tabs.Modify, RevitIds.Panes.PropertiesToggle);
        }

        private ToggleState PropertiesToggleState()
        {
            AutomationElement toggle = PropertiesToggle();
            return UiQuery.Read(() => toggle.Patterns.Toggle.Pattern.ToggleState.Value, Timeout, "ToggleState переключателя Properties");
        }

        /// <summary>
        /// Клик мышью по переключателю: TogglePattern.Toggle и LegacyIAccessible.DoDefaultAction панель не переключают, клик — переключает
        /// (Evidence/properties-toggle-2022.txt, шаги 1–3); раскладка клавиатуры на него не влияет.
        /// </summary>
        private void ClickPropertiesToggle(string why)
        {
            UiAct.Click(PropertiesToggle(), $"переключатель панели Properties (вкладка Modify): {why}", MouseButton.Left, false);
        }

        [Test]
        [Order(6)]
        public void Unknown_modal_fails_reset_and_known_entry_closes_it()
        {
            RevitDialogPage dialog = RevitDialogPage.OpenedBy(App, () => App.Ribbon.Click(RevitTitles.Tabs.Manage, RevitTitles.Manage.ProjectUnits));
            AfterTest("закрыть «Project Units», если открыто", () =>
            {
                if (dialog.IsOpen)
                {
                    dialog.ClickTextAndWaitClosed(RevitTitles.Dialogs.Cancel);
                }
            });

            UiResetReport unknown = App.Windows.ResetUi();
            Assert.That(unknown.Clean, Is.False);
            Assert.That(unknown.Remaining.Single(), Does.Contain("неизвестное модальное окно").And.Contain(RevitTitles.Manage.ProjectUnits));
            Assert.That(dialog.IsOpen, Is.True, "неизвестное окно уборка не трогает");

            RevitWindows withEntry = new RevitWindows(App.Automation, App.ProcessId, App.MainWindowHandle,
                RevitDialogs.Catalog(new[] { new KnownDialogSetting { Title = RevitTitles.Manage.ProjectUnits } }));
            UiResetReport known = withEntry.ResetUi();

            Assert.That(known.Clean, Is.True, known.ToString());
            Assert.That(known.Actions.Single(), Does.Contain(RevitTitles.Manage.ProjectUnits));
            Assert.That(dialog.IsOpen, Is.False);
        }

        /// <summary>Рамки с заголовком «Properties» в дереве UIA (показанные и нет) — доказательство, как выглядит скрытая панель.</summary>
        private string DescribeFrames()
        {
            List<AutomationElement> roots = new List<AutomationElement> { App.MainWindow };
            roots.AddRange(App.Windows.All());
            List<AutomationElement> frames = RevitPanes.FindPanes(roots, RevitTitles.Panes.Properties, RevitIds.Panes.SearchDepth);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Рамок «Properties» в дереве UIA (FindPanes, без отбора по экрану): " + frames.Count);
            foreach (AutomationElement frame in frames)
            {
                sb.AppendLine($"  {frame.Describe()} cls=\"{frame.SafeClassName()}\" IsOffscreen={frame.SafeIsOffscreen()}");
            }
            return sb.ToString();
        }
    }
}
