using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using NUnit.Framework;
using RevitUITesting.NUnit;
using RevitUITesting.Pages;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Contract
{
    /// <summary>
    /// Лента Revit 2022 ENU: вкладки Revit и RevitTestHost, вкладка и кнопка с одной подписью («Modify»), кнопка API по имени и подписи,
    /// нажатие, выпадающая кнопка, свёрнутая панель.
    /// dotnet test --filter "Category=RevitContract&amp;FullyQualifiedName~RibbonContractTests"
    /// </summary>
    [TestFixture]
    [Explicit("живой Revit (версия из revit-ui-tests.json, язык ENU)")]
    [Category("RevitContract")]
    [NonParallelizable]
    [RevitUiModel(RevitHostClient.NewModel)]
    public class RibbonContractTests : RevitUiTestBase
    {
        /// <summary>[Р] Evidence/ribbon-2022.txt: AutomationId вкладки «Modify».</summary>
        private const string ModifyTabId = "Modify";

        /// <summary>[Р] Evidence/ribbon-2022.txt: AutomationId кнопки «Modify» панели Select (Name тоже «Modify»).</summary>
        private const string SelectButtonId = "ID_BUTTON_SELECT";

        /// <summary>[Р] Evidence/ribbon-2022.txt: AutomationId вкладки «Manage».</summary>
        private const string ManageTabId = "Manage";

        /// <summary>[Р] Evidence/ribbon-collapsed-2022.txt: AutomationId свёрнутой панели Settings.</summary>
        private const string SettingsPanelId = "settings_shr";

        /// <summary>[Р] Evidence/ribbon-2022.txt: подпись скрытой вкладки редактора семейств (id «Home_Family», offscreen) — в проекте её нет на полосе вкладок.</summary>
        private const string HiddenFamilyTab = "Create";

        [Test]
        [Order(1)]
        public void Tabs_include_revit_and_testhost()
        {
            IReadOnlyList<string> tabs = App.Ribbon.Tabs();
            TestContext.Out.WriteLine("вкладки: " + string.Join(", ", tabs));
            Assert.That(tabs, Is.SupersetOf(new[] { RevitTitles.Tabs.Architecture, RevitTitles.Tabs.Manage, RevitTitles.Tabs.TestHost }));
            Assert.That(tabs, Does.Not.Contain(HiddenFamilyTab), "скрытые вкладки редактора семейств не входят");
        }

        [Test]
        [Order(2)]
        public void Modify_is_a_tab_for_tab_api_and_a_panel_button_for_button_api()
        {
            AutomationElement tab = App.Ribbon.Tab(RevitTitles.Tabs.Modify);
            Assert.That(UiQuery.Read(() => tab.Properties.AutomationId.Value, Timeout, "AutomationId вкладки Modify"), Is.EqualTo(ModifyTabId));

            AutomationElement button = App.Ribbon.Button(RevitTitles.Tabs.Architecture, RevitTitles.Tabs.Modify);
            Assert.That(UiQuery.Read(() => button.Properties.AutomationId.Value, Timeout, "AutomationId кнопки Modify"), Is.EqualTo(SelectButtonId));
        }

        [Test]
        [Order(3)]
        public void Testhost_button_is_found_by_caption_and_by_internal_name()
        {
            AutomationElement byCaption = App.Ribbon.Button(RevitTitles.Tabs.TestHost, RevitTitles.TestHost.RunAllTests);
            Assert.That(UiQuery.Read(() => byCaption.Properties.IsEnabled.Value, Timeout, "IsEnabled Run All Tests"), Is.True);
            bool idsExposed = RevitIds.Ribbon.ButtonIdsExposed;
            if (idsExposed)
            {
                AutomationElement byId = App.Ribbon.Button(RevitTitles.Tabs.TestHost, RevitIds.TestHost.RunAllTests);
                Assert.That(byId.Equals(byCaption), Is.True, "по имени PushButtonData и по подписи находится одна кнопка");
            }
            else
            {
                Assert.That(() => App.Ribbon.Button(RevitTitles.Tabs.TestHost, RevitIds.TestHost.RunAllTests), Throws.InstanceOf<UiWaitTimeoutException>());
            }
        }

        [Test]
        [Order(4)]
        public void Button_click_opens_project_units()
        {
            RevitDialogPage dialog = RevitDialogPage.OpenedBy(App, () => App.Ribbon.Click(RevitTitles.Tabs.Manage, RevitTitles.Manage.ProjectUnits));
            Evidence.Write("project-units", $"Title: {dialog.Title}{Environment.NewLine}ClassName: {dialog.ClassName}{Environment.NewLine}{dialog.Dump(6)}");
            Assert.That(dialog.Title, Is.EqualTo(RevitTitles.Manage.ProjectUnits));
            dialog.ClickTextAndWaitClosed(RevitTitles.Dialogs.Cancel);
        }

        [Test]
        [Order(5)]
        public void Pulldown_menu_item_opens_line_styles()
        {
            AutomationElement pulldown = App.Ribbon.Button(RevitTitles.Tabs.Manage, RevitTitles.Manage.AdditionalSettings);
            string pulldownId = UiQuery.Read(() => pulldown.Properties.AutomationId.Value, Timeout, "AutomationId Additional Settings");
            ToggleState before = ToggleOf(pulldown);
            Assert.That(before, Is.EqualTo(ToggleState.Off), "меню Additional Settings перед тестом свёрнуто");
            AfterTest("меню Additional Settings закрыто (Toggle Off)", () => CloseMenu(pulldown));
            StringBuilder evidence = new StringBuilder();
            try
            {
                DescribePopups(evidence, "Всплывающие окна ленты до раскрытия (модель открыта)");
                List<AutomationElement> fresh = OpenAndCollectNewWindows(() => App.Ribbon.Open(pulldown, "выпадающая кнопка «Additional Settings» (доказательство)"));
                evidence.AppendLine($"Toggle кнопки до раскрытия: {before}; после: {ToggleOf(pulldown)}");
                DescribeWindows(evidence, fresh);
                Assert.That(App.Ribbon.RibbonPopups().Where(p => p.FindAll(By.Id(pulldownId), 2).Count > 0).ToList(), Has.Count.EqualTo(1),
                    "меню — всплывающее окно ленты со списком, id которого — id выпадающей кнопки");

                CloseMenu(pulldown);
                bool closed = Wait.Holds(() => App.Ribbon.RibbonPopups().All(p => p.FindAll(By.Id(pulldownId), 2).Count == 0), TimeSpan.FromSeconds(10));
                evidence.AppendLine("Меню закрылось по Esc (Toggle Off): " + closed);
                Assert.That(closed, Is.True, "Esc закрывает меню");

                RevitDialogPage dialog = RevitDialogPage.OpenedBy(App, () => App.Ribbon.ClickInMenu(RevitTitles.Tabs.Manage, RevitTitles.Manage.AdditionalSettings, RevitTitles.Manage.LineStyles));
                evidence.AppendLine($"## Окно после пункта «Line Styles»{Environment.NewLine}Title: {dialog.Title}{Environment.NewLine}ClassName: {dialog.ClassName}");
                evidence.AppendLine($"Toggle Additional Settings после нажатия пункта: {ToggleOf(pulldown)}; всплывающих окон ленты: {App.Ribbon.RibbonPopups().Count}");
                evidence.AppendLine(dialog.Dump(3));
                Assert.That(dialog.Title, Is.EqualTo(RevitTitles.Manage.LineStyles));
                dialog.ClickTextAndWaitClosed(RevitTitles.Dialogs.Cancel);
                ToggleState after = ToggleOf(pulldown);
                evidence.AppendLine($"После Cancel: Toggle {after}, всплывающих окон ленты: {App.Ribbon.RibbonPopups().Count}");
                Assert.That(after, Is.EqualTo(ToggleState.Off), "после нажатия пункта меню кнопка свёрнута");
            }
            finally
            {
                Evidence.Write("ribbon-pulldown", evidence.ToString());
            }
        }

        [Test]
        [Order(6)]
        public void Button_in_collapsed_panel_opens_project_units_in_narrow_window()
        {
            Window main = App.MainWindow;
            IntPtr handle = Handle(main);
            WindowPlacement placement = WindowPlacement.Of(handle);
            AfterTest("вернуть положение и размер главного окна Revit (" + placement + ")", () => placement.RestoreTo(handle));
            App.Ribbon.Tab(RevitTitles.Tabs.Manage);
            main.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            // Evidence/ribbon-collapsed-2022.txt: уже 1063 Revit не сжимается; при этой ширине панели Settings, Project Location и др. свёрнуты
            main.Patterns.Transform.Pattern.Resize(900, 700);
            Wait.Until(() => ShownCollapsedPanels().Any(p => PanelId(p) == SettingsPanelId), TimeSpan.FromSeconds(10), "в узком окне показана свёрнутая панель Settings");
            AutomationElement settings = CollapsedPanel(SettingsPanelId);
            // снимок всплывающих окон ленты до раскрытия: окно панели — новое окно после него (прогон 6: Toggle раскрытость не показывает)
            HashSet<IntPtr> before = App.Ribbon.PopupHandles();
            AfterTest("свернуть панель Settings, если её окно осталось открытым", () => App.Ribbon.CollapsePanel(settings, before, "после теста: свёрнутая панель " + SettingsPanelId));

            StringBuilder evidence = new StringBuilder();
            evidence.AppendLine($"Главное окно: было {placement}, стало {main.BoundingRectangle}; всплывающих окон ленты до раскрытия: {before.Count}");
            // раскрытие, поиск кнопки в окне панели и Invoke — библиотекой подряд, без дампов и описаний между ними (прогон 7: окно панели
            // пропало, пока писались дампы); отметки времени — строки журнала шагов, дампы — только после сбоя
            string marker = "Click(Manage, Project Units) в узком окне: свёрнутая панель Settings";
            Step(marker);
            try
            {
                RevitDialogPage dialog = RevitDialogPage.OpenedBy(App, () => App.Ribbon.Click(RevitTitles.Tabs.Manage, RevitTitles.Manage.ProjectUnits));
                UiLog.Action($"диалог «{dialog.Title}» (класс {dialog.ClassName}) открыт");
                // строгое ограниченное ожидание без действий: окно выдвижной панели закрывается само после нажатия кнопки в нём
                bool goneWithDialog = Wait.Holds(() => NewRibbonPopups(before).Count == 0, TimeSpan.FromSeconds(5));
                UiLog.Action($"окно панели {SettingsPanelId} при открытом диалоге: {(goneWithDialog ? "закрыто" : "открыто 5 с")}");
                Assert.That(dialog.Title, Is.EqualTo(RevitTitles.Manage.ProjectUnits));
                dialog.ClickTextAndWaitClosed(RevitTitles.Dialogs.Cancel);
                UiLog.Action("диалог Project Units закрыт (Cancel)");
                App.Ribbon.CollapsePanel(settings, before, "свёрнутая панель " + SettingsPanelId);
                int left = NewRibbonPopups(before).Count;
                UiLog.Action($"после сворачивания: новых всплывающих окон ленты {left}");
                Assert.That(left, Is.EqualTo(0), "окно панели Settings закрыто");
            }
            catch (Exception ex)
            {
                evidence.AppendLine($"## Сбой: {ex.GetType().Name}: {ex.Message}");
                AppendFailureDiagnostics(evidence, before);
                throw;
            }
            finally
            {
                evidence.AppendLine("## Журнал шагов с отметками времени (раскрытие → окно панели → кнопка найдена → Invoke → диалог → окно панели)");
                evidence.AppendLine(LogSince(marker));
                Evidence.Write("ribbon-collapsed-click", evidence.ToString());
            }
        }

        /// <summary>Строки журнала шагов текущего теста, начиная с последнего шага marker.</summary>
        private static string LogSince(string marker)
        {
            List<string> lines = UiLog.Dump().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
            int start = lines.FindLastIndex(l => l.Contains("> " + marker));
            return string.Join(Environment.NewLine, start < 0 ? lines : lines.Skip(start));
        }

        /// <summary>Диагностика только после сбоя: всплывающие окна ленты, новые после снимка, окна процесса, содержимое вкладки Manage.</summary>
        private void AppendFailureDiagnostics(StringBuilder evidence, HashSet<IntPtr> before)
        {
            Diagnose(evidence, "Всплывающие окна ленты", () => DescribePopups(evidence, "Всплывающие окна ленты после сбоя"));
            Diagnose(evidence, "Новые всплывающие окна ленты", () =>
            {
                List<AutomationElement> fresh = NewRibbonPopups(before);
                evidence.AppendLine($"## Новых всплывающих окон ленты после снимка: {fresh.Count}");
                foreach (AutomationElement window in fresh)
                {
                    evidence.AppendLine(window.DumpTree(8, true));
                }
            });
            Diagnose(evidence, "Окна процесса", () => DescribeWindows(evidence, new List<AutomationElement>()));
            Diagnose(evidence, "Содержимое вкладки Manage", () =>
            {
                AutomationElement content = App.Ribbon.TabContent(ManageTabId);
                evidence.AppendLine("## Содержимое вкладки Manage");
                evidence.AppendLine(content == null ? "(нет)" : content.DumpTree(25, true));
            });
        }

        /// <summary>Один раздел диагностики: его сбой записывается в доказательство и не заслоняет исходную ошибку теста.</summary>
        private static void Diagnose(StringBuilder evidence, string title, Action describe)
        {
            try
            {
                describe();
            }
            catch (Exception ex)
            {
                // диагностика после сбоя: исходная ошибка пробрасывается вызывающим, сбой раздела виден в файле доказательства
                evidence.AppendLine($"## {title}: не записано — {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>Всплывающие окна ленты, которых не было в снимке before.</summary>
        private List<AutomationElement> NewRibbonPopups(HashSet<IntPtr> before)
        {
            return App.Ribbon.RibbonPopups().Where(p => !before.Contains(Handle(p))).ToList();
        }

        private List<AutomationElement> ShownCollapsedPanels()
        {
            AutomationElement content = App.Ribbon.TabContent(ManageTabId);
            return content == null ? new List<AutomationElement>() : content.FindAll(RevitIds.Ribbon.CollapsedPanel, RevitIds.Ribbon.SearchDepth).Where(RibbonMatch.IsShown).ToList();
        }

        private AutomationElement CollapsedPanel(string id)
        {
            return ShownCollapsedPanels().Single(p => PanelId(p) == id);
        }

        private string PanelId(AutomationElement panel)
        {
            return UiQuery.Read(() => panel.Properties.AutomationId.Value, Timeout, "AutomationId свёрнутой панели");
        }

        private void DescribePopups(StringBuilder evidence, string title)
        {
            List<AutomationElement> popups = App.Ribbon.RibbonPopups();
            evidence.AppendLine($"## {title}: {popups.Count}");
            foreach (AutomationElement popup in popups)
            {
                evidence.AppendLine(popup.DumpTree(2, true));
            }
        }

        /// <summary>Выполнить раскрытие и вернуть окна процесса Revit, появившиеся за 10 с (по HWND).</summary>
        private List<AutomationElement> OpenAndCollectNewWindows(Action open)
        {
            HashSet<IntPtr> before = new HashSet<IntPtr>(ProcessWindows().Select(Handle));
            open();
            List<AutomationElement> fresh = new List<AutomationElement>();
            Wait.Holds(() => (fresh = ProcessWindows().Where(w => !before.Contains(Handle(w))).ToList()).Count > 0, TimeSpan.FromSeconds(10));
            return fresh;
        }

        /// <summary>
        /// Доказательство: видимые окна процесса Revit, кроме главного, по EnumWindows — шире перечня библиотеки (RevitWindows): сюда попадают
        /// и окна с владельцем, и всплывающие окна в любом месте дерева UI Automation.
        /// </summary>
        private List<AutomationElement> ProcessWindows()
        {
            IntPtr main = Handle(App.MainWindow);
            return RevitNative.TopLevelWindows(App.ProcessId).Where(w => w.IsVisible && w.Handle != main).Select(w => App.Automation.FromHandle(w.Handle)).ToList();
        }

        private void DescribeWindows(StringBuilder evidence, List<AutomationElement> fresh)
        {
            evidence.AppendLine("## Окна процесса Revit, кроме главного");
            foreach (AutomationElement window in ProcessWindows())
            {
                evidence.AppendLine($"окно «{window.SafeName()}» класс {window.SafeClassName()} тип {window.SafeControlType()} hwnd {Handle(window)} {window.BoundingRectangle} в Popups(): {App.Windows.Popups().Any(p => Handle(p) == Handle(window))}, в All(): {App.Windows.All().Any(p => Handle(p) == Handle(window))}");
            }
            evidence.AppendLine($"## Новые окна после раскрытия: {fresh.Count}");
            foreach (AutomationElement window in fresh)
            {
                evidence.AppendLine(window.DumpTree(12, true));
            }
        }

        /// <summary>Строгое чтение ToggleState: «занят» — повтор, нет паттерна или элемент исчез — исключение.</summary>
        private ToggleState ToggleOf(AutomationElement element)
        {
            return UiQuery.Read(() => element.Patterns.Toggle.Pattern.ToggleState.Value, Timeout, "ToggleState " + element.Describe());
        }

        /// <summary>
        /// Закрыть меню выпадающей кнопки: Toggle On — Esc в Revit и ждать Off. Возврат Toggle меню не закрывает (прогон 4), Esc закрывает (прогон 3).
        /// </summary>
        private void CloseMenu(AutomationElement pulldown)
        {
            if (ToggleOf(pulldown) != ToggleState.On)
            {
                return;
            }
            App.SendKeys("Esc: закрыть меню Additional Settings", FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
            Wait.Until(() => ToggleOf(pulldown) == ToggleState.Off, TimeSpan.FromSeconds(10), "меню Additional Settings закрыто (Toggle Off)");
        }

        private static IntPtr Handle(AutomationElement window)
        {
            return UiQuery.Read(() => window.Properties.NativeWindowHandle.Value, TimeSpan.FromSeconds(30), "HWND окна");
        }

        /// <summary>Положение окна (GetWindowPlacement): состояние и нормальные границы — их Revit запоминает при выходе.</summary>
        private sealed class WindowPlacement
        {
            [StructLayout(LayoutKind.Sequential)]
            private struct Point
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct Native
            {
                public int Length;
                public int Flags;
                public int ShowCmd;
                public Point MinPosition;
                public Point MaxPosition;
                public Rect NormalPosition;
            }

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool GetWindowPlacement(IntPtr hWnd, ref Native placement);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool SetWindowPlacement(IntPtr hWnd, ref Native placement);

            private Native _value;

            internal static WindowPlacement Of(IntPtr handle)
            {
                return new WindowPlacement { _value = Read(handle) };
            }

            internal void RestoreTo(IntPtr handle)
            {
                Native value = _value;
                if (!SetWindowPlacement(handle, ref value))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPlacement главного окна Revit");
                }
                Native now = Read(handle);
                if (now.ShowCmd != _value.ShowCmd || !Same(now.NormalPosition, _value.NormalPosition))
                {
                    throw new InvalidOperationException($"Положение главного окна Revit не вернулось: было {this}, стало {Describe(now)}");
                }
            }

            public override string ToString()
            {
                return Describe(_value);
            }

            private static Native Read(IntPtr handle)
            {
                Native value = new Native { Length = Marshal.SizeOf(typeof(Native)) };
                if (!GetWindowPlacement(handle, ref value))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "GetWindowPlacement главного окна Revit");
                }
                return value;
            }

            private static bool Same(Rect a, Rect b)
            {
                return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
            }

            private static string Describe(Native value)
            {
                Rect r = value.NormalPosition;
                return $"showCmd {value.ShowCmd}, нормальные границы {r.Left},{r.Top}–{r.Right},{r.Bottom}";
            }
        }
    }
}
