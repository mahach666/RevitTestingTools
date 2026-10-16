using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using NUnit.Framework;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Unit
{
    [TestFixture]
    public class RibbonMatchTests
    {
        private UIA3Automation _automation;

        [SetUp]
        public void Prepare()
        {
            _automation = new UIA3Automation();
        }

        [TearDown]
        public void Cleanup()
        {
            _automation.Dispose();
        }

        [TestCase("RunAllTests", "RunAllTests", true)]
        [TestCase("CustomCtrl_%CustomCtrl_%TestHost%Test Runner%RunAllTests", "RunAllTests", true)]
        [TestCase("CustomCtrl_%CustomCtrl_%TestHost%Test Runner%XRunAllTests", "RunAllTests", false)]
        [TestCase("", "RunAllTests", false)]
        [TestCase("RunAllTests", "", false)]
        public void Button_id_matching(string automationId, string button, bool expected)
        {
            Assert.That(RibbonMatch.IsButtonId(automationId, button), Is.EqualTo(expected));
        }

        [TestCase("Run All\nTests", "Run All Tests", true)]
        [TestCase("Run All\r\nTests", "Run All\nTests", true)]
        [TestCase("Run  All Tests", "Run All Tests", true)]
        [TestCase("run all tests", "Run All Tests", false)]
        [TestCase("", "", false)]
        public void Caption_matching(string name, string button, bool expected)
        {
            Assert.That(RibbonMatch.IsCaption(name, button), Is.EqualTo(expected));
        }

        [Test]
        public void Buttons_are_found_by_caption_or_id_but_not_as_text()
        {
            FakeNode root = new FakeNode(_automation, "ribbon", ControlType.Pane).Add(
                new FakeNode(_automation, "panel", ControlType.Group, "Test Runner").Add(
                    new FakeNode(_automation, "", ControlType.Button, "Run All\nTests"),
                    new FakeNode(_automation, "label", ControlType.Text, "Run All Tests")),
                new FakeNode(_automation, "CustomCtrl_%CustomCtrl_%Acme dev%Семейства%FamilySearch", ControlType.Button, "Поиск\nсемейств"));

            List<AutomationElement> byCaption = RibbonMatch.FindButtons(root.Element, "Run All Tests");
            List<AutomationElement> byId = RibbonMatch.FindButtons(root.Element, "FamilySearch");
            List<AutomationElement> byRussianCaption = RibbonMatch.FindButtons(root.Element, "Поиск семейств");

            Assert.That(byCaption, Has.Count.EqualTo(1));
            Assert.That(byCaption[0].Properties.ControlType.Value, Is.EqualTo(ControlType.Button));
            Assert.That(byId, Has.Count.EqualTo(1));
            Assert.That(byRussianCaption, Has.Count.EqualTo(1));
        }

        [Test]
        public void Tabs_are_direct_children_paired_with_their_panel_content()
        {
            // как в Evidence/ribbon-2022.txt: вкладка — Button (Name = подпись, внутри Text «mContent» с той же подписью), прямой потомок
            // главного окна, рядом — её содержимое «<id>_PanelBarScrollViewer»; у кнопки меню приложения (тоже Button, прямой потомок) содержимого нет
            FakeNode root = new FakeNode(_automation, "main", ControlType.Window).Add(
                new FakeNode(_automation, "Acme dev_PanelBarScrollViewer", ControlType.Custom, "Acme dev") { FakeOffscreen = true },
                new FakeNode(_automation, "TestHost_PanelBarScrollViewer", ControlType.Custom, "TestHost"),
                new FakeNode(_automation, "ID_ApplicationMenuButton", ControlType.Button, "ID_ApplicationMenuButton"),
                new FakeNode(_automation, "Acme dev", ControlType.Button, "Acme dev"),
                new FakeNode(_automation, "TestHost", ControlType.Button, "TestHost").Add(
                    new FakeNode(_automation, "mContent", ControlType.Text, "TestHost")));

            List<RibbonTab> all = RibbonMatch.FindTabs(root.Element, null);
            List<RibbonTab> testHost = RibbonMatch.FindTabs(root.Element, "TestHost");

            Assert.That(all.Select(t => t.AutomationId), Is.EqualTo(new[] { "Acme dev", "TestHost" }));
            Assert.That(testHost, Has.Count.EqualTo(1));
            Assert.That(testHost[0].IsActive, Is.True, "содержимое вкладки на экране — вкладка активна");
            Assert.That(testHost[0].Content.Properties.AutomationId.Value, Is.EqualTo("TestHost_PanelBarScrollViewer"));
            Assert.That(all[0].IsActive, Is.False);
        }

        [Test]
        public void Tabs_on_tab_strip_are_found_with_only_active_content_realized()
        {
            // как в дампе Revit 2025.1: кнопки вкладок — в Custom «mMainTabs», содержимое — ItemsControl → DataItem → «<id>_PanelBarScrollViewer»,
            // и оно есть только у активной вкладки (DataItem неактивных пустые); кнопка «Modify» панели Select — внутри содержимого
            FakeNode root = new FakeNode(_automation, "main", ControlType.Window).Add(
                new FakeNode(_automation, "", ControlType.List, "", "ItemsControl").Add(
                    new FakeNode(_automation, "", ControlType.DataItem, "UIFramework.RvtRibbonTab", "ItemsControlItem").Add(
                        new FakeNode(_automation, "Architecture_PanelBarScrollViewer", ControlType.Custom, "Architecture").Add(
                            new FakeNode(_automation, "mMainTabPanels", ControlType.List, "", "ItemsControl").Add(
                                new FakeNode(_automation, "ID_BUTTON_SELECT", ControlType.Button, "Modify")))),
                    new FakeNode(_automation, "", ControlType.DataItem, "UIFramework.RvtRibbonTab", "ItemsControlItem") { FakeOffscreen = true }),
                new FakeNode(_automation, "ID_ApplicationMenuButton", ControlType.Button, "ID_ApplicationMenuButton"),
                new FakeNode(_automation, "mMainTabs", ControlType.Custom).Add(
                    new FakeNode(_automation, "Architecture", ControlType.Button, "Architecture"),
                    new FakeNode(_automation, "Manage", ControlType.Button, "Manage"),
                    new FakeNode(_automation, "Manage_Family", ControlType.Button, "Manage") { FakeOffscreen = true },
                    new FakeNode(_automation, "Modify", ControlType.Button, "Modify")));

            List<RibbonTab> all = RibbonMatch.FindTabs(root.Element, null);
            RibbonTab architecture = RibbonMatch.FindTabs(root.Element, "Architecture").Single();
            RibbonTab manage = RibbonMatch.FindTabs(root.Element, "Manage").Single();

            Assert.That(all.Select(t => t.AutomationId), Is.EqualTo(new[] { "Architecture", "Manage", "Modify" }), "кнопка меню приложения и скрытая Manage_Family — не вкладки");
            Assert.That(architecture.IsActive, Is.True);
            Assert.That(architecture.Content.Properties.AutomationId.Value, Is.EqualTo("Architecture_PanelBarScrollViewer"));
            Assert.That(manage.IsActive, Is.False);
            Assert.That(manage.Content, Is.Null, "содержимого неактивной вкладки в дереве нет");
            Assert.That(RibbonMatch.FindTabs(root.Element, "Modify").Select(t => t.AutomationId), Is.EqualTo(new[] { "Modify" }), "кнопка ID_BUTTON_SELECT — не вкладка");
        }

        [Test]
        public void Panel_button_with_tab_caption_is_not_returned_as_tab()
        {
            FakeNode root = ModifyRibbon(withModifyTab: true);

            List<RibbonTab> tabs = RibbonMatch.FindTabs(root.Element, "Modify");

            Assert.That(tabs.Select(t => t.AutomationId), Is.EqualTo(new[] { "Modify" }), "кнопка ID_BUTTON_SELECT и скрытая вкладка Second_Modify — не вкладки");
            Assert.That(RibbonMatch.FindTabs(ModifyRibbon(withModifyTab: false).Element, "Modify"), Is.Empty, "без вкладки Modify кнопка «Modify» вкладкой не становится");
        }

        [Test]
        public void Button_search_in_tab_content_does_not_return_tabs()
        {
            FakeNode root = ModifyRibbon(withModifyTab: true);
            RibbonTab architecture = RibbonMatch.FindTabs(root.Element, "Architecture").Single();

            List<AutomationElement> buttons = RibbonMatch.FindButtons(architecture.Content, "Modify");

            Assert.That(buttons.Select(b => b.Properties.AutomationId.Value), Is.EqualTo(new[] { "ID_BUTTON_SELECT" }));
            Assert.That(RibbonMatch.FindButtons(root.Element, "Modify"), Has.Count.EqualTo(3), "во всём главном окне подпись «Modify» у вкладок и у кнопки панели");
        }

        [Test]
        public void Hidden_tab_is_skipped_but_active_tab_off_the_tab_bar_is_kept()
        {
            // скрытые вкладки редактора семейств (Manage_Family) — offscreen вместе с содержимым; активная вкладка, ушедшая с полосы вкладок
            // (узкое окно), остаётся вкладкой: её содержимое на экране
            FakeNode root = new FakeNode(_automation, "main", ControlType.Window).Add(
                new FakeNode(_automation, "Manage_PanelBarScrollViewer", ControlType.Custom, "Manage"),
                new FakeNode(_automation, "Manage_Family_PanelBarScrollViewer", ControlType.Custom, "Manage_Family") { FakeOffscreen = true },
                new FakeNode(_automation, "Manage", ControlType.Button, "Manage") { FakeOffscreen = true },
                new FakeNode(_automation, "Manage_Family", ControlType.Button, "Manage") { FakeOffscreen = true });

            List<RibbonTab> tabs = RibbonMatch.FindTabs(root.Element, "Manage");

            Assert.That(tabs.Select(t => t.AutomationId), Is.EqualTo(new[] { "Manage" }));
            Assert.That(tabs[0].IsActive, Is.True);
        }

        [Test]
        public void Active_tab_removed_from_tab_bar_is_found_by_its_id()
        {
            // Evidence/ribbon-collapsed-2022.txt: в узком окне кнопки вкладок, не поместившихся на полосу (Manage, View, …), из дерева UIA исчезают,
            // а содержимое активной вкладки на экране; его Name — AutomationId вкладки («thePrecastTab_PanelBarScrollViewer» name «thePrecastTab»)
            FakeNode root = new FakeNode(_automation, "main", ControlType.Window).Add(
                new FakeNode(_automation, "Architecture_PanelBarScrollViewer", ControlType.Custom, "Architecture") { FakeOffscreen = true },
                new FakeNode(_automation, "Manage_PanelBarScrollViewer", ControlType.Custom, "Manage"),
                new FakeNode(_automation, "View_PanelBarScrollViewer", ControlType.Custom, "View") { FakeOffscreen = true },
                new FakeNode(_automation, "Architecture", ControlType.Button, "Architecture"));

            List<RibbonTab> manage = RibbonMatch.FindTabs(root.Element, "Manage");

            Assert.That(manage, Has.Count.EqualTo(1));
            Assert.That(manage[0].Element, Is.Null, "кнопки вкладки нет");
            Assert.That(manage[0].IsActive, Is.True);
            Assert.That(manage[0].Content.Properties.AutomationId.Value, Is.EqualTo("Manage_PanelBarScrollViewer"));
            Assert.That(RibbonMatch.FindTabs(root.Element, "View"), Is.Empty, "неактивная вкладка без кнопки недоступна");
            Assert.That(RibbonMatch.FindTabs(root.Element, null).Select(t => t.AutomationId), Is.EqualTo(new[] { "Architecture" }), "перечень вкладок — по кнопкам");
        }

        [Test]
        public void Tab_is_found_by_internal_id_as_well_as_by_caption()
        {
            FakeNode root = new FakeNode(_automation, "main", ControlType.Window).Add(
                new FakeNode(_automation, "thePrecastTab_PanelBarScrollViewer", ControlType.Custom, "thePrecastTab") { FakeOffscreen = true },
                new FakeNode(_automation, "thePrecastTab", ControlType.Button, "Precast"));

            Assert.That(RibbonMatch.FindTabs(root.Element, "Precast"), Has.Count.EqualTo(1));
            Assert.That(RibbonMatch.FindTabs(root.Element, "thePrecastTab"), Has.Count.EqualTo(1));
        }

        [Test]
        public void Collapsed_panel_is_a_button_with_collapsed_panel_image()
        {
            // Evidence/ribbon-collapsed-2022.txt: свёрнутая панель Settings — Button id «settings_shr» (Name пустой, Toggle) с Image «mCollapsedPanelImage»;
            // у развёрнутой панели (Select) такая же кнопка есть, но offscreen
            FakeNode collapsed = new FakeNode(_automation, "settings_shr", ControlType.Button, "", "Button").Add(
                new FakeNode(_automation, "Badge_1", ControlType.Custom, "", "Badge") { FakeOffscreen = true },
                new FakeNode(_automation, "mCollapsedPanelImage", ControlType.Image, "", "Image"));
            FakeNode expanded = new FakeNode(_automation, "selection_shr", ControlType.Button, "", "Button") { FakeOffscreen = true }.Add(
                new FakeNode(_automation, "mCollapsedPanelImage", ControlType.Image, "", "Image") { FakeOffscreen = true });
            FakeNode plain = new FakeNode(_automation, "ID_SETTINGS_UNITS", ControlType.Button, "Project\r\nUnits", "Button").Add(
                new FakeNode(_automation, "mImage", ControlType.Image, "", "Image"));
            FakeNode content = new FakeNode(_automation, "Manage_PanelBarScrollViewer", ControlType.Custom, "Manage").Add(
                new FakeNode(_automation, "settings_shr", ControlType.Custom, "Settings").Add(plain, collapsed),
                new FakeNode(_automation, "selection_shr", ControlType.Custom, "Select").Add(expanded));

            List<AutomationElement> panels = UiQuery.FindAllComplete(content.Element, RevitIds.Ribbon.CollapsedPanel, System.TimeSpan.FromSeconds(5), RevitIds.Ribbon.SearchDepth);

            Assert.That(panels.Select(p => p.Properties.AutomationId.Value), Is.EquivalentTo(new[] { "selection_shr", "settings_shr" }), "отбор по экрану — у вызывающего (RevitRibbon)");
            Assert.That(panels.Where(RibbonMatch.IsShown).Select(p => p.Properties.AutomationId.Value), Is.EqualTo(new[] { "settings_shr" }));
        }

        [Test]
        public void Menu_item_in_two_popups_is_ambiguous_and_offscreen_copies_are_skipped()
        {
            // Evidence/ribbon-pulldown-2022.txt: меню — окно Popup → List → ListItem → Button «Line Styles» (Invoke); ListItem с той же подписью — не кнопка
            FakeNode fresh = Menu("ID_SETTINGS_LINE_STYLES", offscreen: false);
            FakeNode stale = Menu("ID_SETTINGS_LINE_STYLES", offscreen: false);
            FakeNode hidden = Menu("ID_SETTINGS_LINE_STYLES", offscreen: true);

            List<AutomationElement> one = RibbonMatch.FindShownButtons(new[] { fresh.Element, hidden.Element }, "Line Styles");
            List<AutomationElement> two = RibbonMatch.FindShownButtons(new[] { fresh.Element, stale.Element }, "Line Styles");

            Assert.That(one, Has.Count.EqualTo(1));
            Assert.That(RibbonMatch.Single(one, "пункт «Line Styles»"), Is.Not.Null);
            Assert.That(RibbonMatch.Single(new List<AutomationElement>(), "пункт «Line Styles»"), Is.Null);
            Assert.That(() => RibbonMatch.Single(two, "пункт «Line Styles»"), Throws.InstanceOf<AmbiguousElementException>());
        }

        [Test]
        public void Ribbon_popup_is_recognized_by_uia_class_name()
        {
            // прогон 4: окно меню и окно свёрнутой панели — WPF; ClassName «Popup» есть только в UI Automation, у класса Win32 его нет
            FakeNode menu = new FakeNode(_automation, "_PopupRoot", ControlType.Window, "", "Popup");
            FakeNode panel = new FakeNode(_automation, "settings_shr_SlideOutPanelPopup_PopupRoot", ControlType.Window, "", "Popup");
            FakeNode dialog = new FakeNode(_automation, "", ControlType.Window, "Project Units", "#32770");
            FakeNode family = new FakeNode(_automation, "", ControlType.Window, "Family", "Afx:00007FF794280000:8:0000000000010003:0000000000000000:0000000000000000");

            Assert.That(new[] { menu, panel, dialog, family }.Where(n => RibbonMatch.IsPopup(n.Element)).Select(n => n.FakeId), Is.EqualTo(new[] { "_PopupRoot", "settings_shr_SlideOutPanelPopup_PopupRoot" }));
        }

        [Test]
        public void Panel_window_buttons_are_found_although_uia_reports_them_offscreen()
        {
            // Evidence/ribbon-collapsed-2022.txt (прогон 5): окно раскрытой панели Settings видимо (EnumWindows, 1153×144), но в UIA и оно, и его
            // кнопки 5 с после раскрытия offscreen с пустым прямоугольником — признак IsOffscreen здесь не годится
            FakeNode panel = new FakeNode(_automation, "settings_shr_SlideOutPanelPopup_PopupRoot", ControlType.Window, "", "Popup") { FakeOffscreen = true }.Add(
                new FakeNode(_automation, "ID_SETTINGS_UNITS_RibbonItemControl", ControlType.Custom, "Project\r\nUnits") { FakeOffscreen = true }.Add(
                    new FakeNode(_automation, "ID_SETTINGS_UNITS", ControlType.Button, "Project\r\nUnits", "Button") { FakeOffscreen = true }));

            Assert.That(RibbonMatch.FindShownButtons(new[] { panel.Element }, "Project Units"), Is.Empty, "фильтр по экрану кнопку отбросил бы");
            Assert.That(RibbonMatch.FindPanelWindowButtons(new[] { panel.Element }, "Project Units").Select(b => b.Properties.AutomationId.Value), Is.EqualTo(new[] { "ID_SETTINGS_UNITS" }));
        }

        [TestCase(false, false, false, "Done", TestName = "Окно панели закрыто — действий нет")]
        [TestCase(false, true, false, "Done", TestName = "Окно закрылось после Toggle — готово")]
        [TestCase(false, true, true, "Done", TestName = "Окно закрылось после Esc — готово")]
        [TestCase(true, false, false, "Toggle", TestName = "Окно открыто — сначала один Toggle")]
        [TestCase(true, true, false, "Escape", TestName = "Окно открыто после Toggle — один Esc")]
        [TestCase(true, true, true, "Fail", TestName = "Окно открыто после Toggle и Esc — отказ без повтора")]
        public void Collapse_step_depends_on_panel_window_not_toggle_state(bool windowOpen, bool toggled, bool escaped, string expected)
        {
            // параметр — имя шага: тип шага internal, а метод теста public
            Assert.That(RibbonMatch.NextCollapseStep(windowOpen, toggled, escaped).ToString(), Is.EqualTo(expected));
        }

        [Test]
        public void Collapse_steps_never_repeat_an_action_while_window_stays_open()
        {
            List<PanelCollapseStep> steps = new List<PanelCollapseStep>();
            bool toggled = false;
            bool escaped = false;
            for (int i = 0; i < 10; i++)
            {
                PanelCollapseStep step = RibbonMatch.NextCollapseStep(true, toggled, escaped);
                steps.Add(step);
                if (step == PanelCollapseStep.Toggle)
                {
                    toggled = true;
                }
                else if (step == PanelCollapseStep.Escape)
                {
                    escaped = true;
                }
                else
                {
                    break;
                }
            }
            Assert.That(steps, Is.EqualTo(new[] { PanelCollapseStep.Toggle, PanelCollapseStep.Escape, PanelCollapseStep.Fail }));
        }

        private FakeNode Menu(string itemId, bool offscreen)
        {
            return new FakeNode(_automation, "_PopupRoot", ControlType.Window, "", "Popup").Add(
                new FakeNode(_automation, "HID_SETTINGS_RibbonListButton", ControlType.List, "Additional\r\nSettings", "ListBox").Add(
                    new FakeNode(_automation, itemId, ControlType.ListItem, "Line Styles", "ListBoxItem").Add(
                        new FakeNode(_automation, itemId, ControlType.Button, "Line Styles", "Button") { FakeOffscreen = offscreen })));
        }

        /// <summary>Главное окно по Evidence/ribbon-2022.txt: активна Architecture, в её панели Select — кнопка «Modify» (ID_BUTTON_SELECT).</summary>
        private FakeNode ModifyRibbon(bool withModifyTab)
        {
            FakeNode root = new FakeNode(_automation, "main", ControlType.Window).Add(
                new FakeNode(_automation, "Architecture_PanelBarScrollViewer", ControlType.Custom, "Architecture").Add(
                    new FakeNode(_automation, "", ControlType.Custom, "Select").Add(
                        new FakeNode(_automation, "ID_BUTTON_SELECT", ControlType.Button, "Modify"))),
                new FakeNode(_automation, "Second_Modify_PanelBarScrollViewer", ControlType.Custom, "Second_Modify") { FakeOffscreen = true },
                new FakeNode(_automation, "Architecture", ControlType.Button, "Architecture"),
                new FakeNode(_automation, "Second_Modify", ControlType.Button, "Modify") { FakeOffscreen = true });
            if (withModifyTab)
            {
                root.Add(
                    new FakeNode(_automation, "Modify_PanelBarScrollViewer", ControlType.Custom, "Modify") { FakeOffscreen = true },
                    new FakeNode(_automation, "Modify", ControlType.Button, "Modify"));
            }
            return root;
        }
    }
}
