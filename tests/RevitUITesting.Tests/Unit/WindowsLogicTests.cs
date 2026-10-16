using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using NUnit.Framework;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Unit
{
    [TestFixture]
    public class WindowsLogicTests
    {
        private const int RevitPid = 4242;

        private static DialogInfo Window(string title, string text = "", bool popup = false, string className = "#32770")
        {
            return new DialogInfo(null, new IntPtr(title.GetHashCode() & 0xFFFF), title, className, text, popup);
        }

        private static NativeWindowInfo Native(int handle, string title, int processId = RevitPid, string className = "#32770", bool visible = true)
        {
            return new NativeWindowInfo(new IntPtr(handle), processId, className, title, visible);
        }

        [TestCase("Run All\nTests", "Run All Tests")]
        [TestCase("  Run\r\n All\tTests ", "Run All Tests")]
        [TestCase("Project Units", "Project Units")]
        [TestCase(null, "")]
        public void Caption_is_normalized(string raw, string expected)
        {
            Assert.That(CaptionText.Normalize(raw), Is.EqualTo(expected));
        }

        [TestCase("No", "No", true)]
        [TestCase("no", "No", true)]
        [TestCase("Save\nSaves the changes to the project", "Save", true)]
        [TestCase("Don't save", "Save", false)]
        [TestCase("Run All\nTests", "Run All Tests", true)]
        public void Button_caption_matching(string name, string caption, bool expected)
        {
            Assert.That(RevitTaskDialog.ButtonMatches(name, caption), Is.EqualTo(expected));
        }

        [Test]
        public void Task_dialog_text_excludes_title_and_buttons()
        {
            string text = RevitTaskDialog.ComposeText("Autodesk Revit 2022", new[] { "Autodesk Revit 2022", "Do you want to save changes to new-1?", "Yes", "No", "Do you want to save changes to new-1?" }, new[] { "Yes", "No", "Cancel" });
            Assert.That(text, Is.EqualTo("Do you want to save changes to new-1?"));
        }

        [Test]
        public void Popup_is_escaped_first()
        {
            ResetStep step = ResetPlanner.Next(new[] { Window("Warning", "x"), Window("", popup: true, className: "Popup") }, true, RevitDialogs.BuiltIn);
            Assert.That(step.Kind, Is.EqualTo(ResetStepKind.Escape));
            Assert.That(step.Window.IsPopup, Is.True);
        }

        [Test]
        public void Known_save_prompt_is_answered_by_task_dialog_button()
        {
            ResetStep step = ResetPlanner.Next(new[] { Window("Save File", "Do you want to save changes to new-1.rvt?") }, false, RevitDialogs.BuiltIn);
            Assert.That(step.Kind, Is.EqualTo(ResetStepKind.TaskDialogButton));
            Assert.That(step.Known, Is.SameAs(RevitDialogs.SaveChanges));
        }

        [TestCase("HwndWrapper[Revit.exe;;1]")]
        [TestCase("WindowsForms10.Window.8.app.0.1")]
        public void Save_changes_text_outside_task_dialog_is_not_answered_by_button_id(string className)
        {
            // TDM_CLICK_BUTTON 7 понимает только TaskDialog (#32770): окно плагина с тем же текстом — не вопрос Revit о сохранении
            DialogInfo plugin = Window("Plugin settings", "Do you want to save changes to settings?", className: className);

            Assert.That(ResetPlanner.Next(new[] { plugin }, false, RevitDialogs.BuiltIn).Kind, Is.EqualTo(ResetStepKind.UnknownModal));
            Assert.That(ResetPlanner.Next(new[] { plugin }, true, RevitDialogs.BuiltIn).Kind, Is.EqualTo(ResetStepKind.Done), "немодальное окно плагина не трогается");
            Assert.That(ResetPlanner.Applies(RevitDialogs.SaveChanges, plugin), Is.False);
            Assert.That(ResetPlanner.Match(RevitDialogs.BuiltIn, plugin), Is.Null);
        }

        [Test]
        public void Task_dialog_button_entry_is_skipped_for_other_class_and_next_entry_applies()
        {
            // запись save-changes к окну WPF не применяется — каталог проверяется дальше, как будто её нет
            DialogInfo warning = Window("Warning", "Do you want to save changes to settings?", className: "HwndWrapper[Revit.exe;;1]");

            ResetStep step = ResetPlanner.Next(new[] { warning }, false, RevitDialogs.BuiltIn);

            Assert.That(step.Kind, Is.EqualTo(ResetStepKind.Close));
            Assert.That(step.Known, Is.SameAs(RevitDialogs.RevitWarning));
        }

        [Test]
        public void Save_changes_task_dialog_still_applies()
        {
            DialogInfo prompt = Window("Save File", "Do you want to save changes to new-1.rvt?", className: RevitIds.Dialogs.TaskDialogClass);

            Assert.That(ResetPlanner.Applies(RevitDialogs.SaveChanges, prompt), Is.True);
            Assert.That(ResetPlanner.Match(RevitDialogs.BuiltIn, prompt), Is.SameAs(RevitDialogs.SaveChanges));
        }

        [Test]
        public void Save_prompt_is_not_pressed_by_title_only_native_scan()
        {
            // запись «save-changes» узнаёт окно по тексту, а EnumWindows текста не даёт: «No» нажимается только после чтения текста через UIA
            Assert.That(ResetPlanner.TaskDialogTargets(new[] { Native(1, "Save File") }, RevitPid, RevitDialogs.BuiltIn), Is.Empty);
        }

        [Test]
        public void Known_warning_is_closed_even_when_main_window_is_enabled()
        {
            ResetStep step = ResetPlanner.Next(new[] { Window("Warning", "Highlighted walls overlap.") }, true, RevitDialogs.BuiltIn);
            Assert.That(step.Kind, Is.EqualTo(ResetStepKind.Close));
        }

        [Test]
        public void Unsigned_addin_is_pressed_always_load()
        {
            ResetStep step = ResetPlanner.Next(new[] { Window("Security - Unsigned Add-In", "AcmeDev") }, false, RevitDialogs.BuiltIn);
            Assert.That(step.Kind, Is.EqualTo(ResetStepKind.TaskDialogButton));
            Assert.That(step.Known, Is.SameAs(RevitDialogs.UnsignedAddinEn));
        }

        [Test]
        public void Unknown_window_fails_only_when_main_window_is_disabled()
        {
            DialogInfo[] windows = { Window("Источники", "Путь UNC") };

            Assert.That(ResetPlanner.Next(windows, false, RevitDialogs.BuiltIn).Kind, Is.EqualTo(ResetStepKind.UnknownModal));
            Assert.That(ResetPlanner.Next(windows, true, RevitDialogs.BuiltIn).Kind, Is.EqualTo(ResetStepKind.Done), "немодальное окно плагина уборка не трогает");
        }

        [Test]
        public void Disabled_main_window_without_windows_is_reported()
        {
            Assert.That(ResetPlanner.Next(new DialogInfo[0], false, RevitDialogs.BuiltIn).Kind, Is.EqualTo(ResetStepKind.MainDisabled));
            Assert.That(ResetPlanner.Next(new DialogInfo[0], true, RevitDialogs.BuiltIn).Kind, Is.EqualTo(ResetStepKind.Done));
        }

        [Test]
        public void Config_entry_closes_otherwise_unknown_modal()
        {
            IReadOnlyList<KnownDialog> catalog = RevitDialogs.Catalog(new[] { new KnownDialogSetting { Title = "Project Units" } });
            ResetStep step = ResetPlanner.Next(new[] { Window("Project Units", "Units") }, false, catalog);
            Assert.That(step.Kind, Is.EqualTo(ResetStepKind.Close));
            Assert.That(step.Known.Name, Is.EqualTo("knownDialogs[0]"));
        }

        [Test]
        public void Native_unsigned_addin_dialogs_are_selected_in_both_languages()
        {
            NativeWindowInfo en = Native(1, "Security - Unsigned Add-In");
            NativeWindowInfo ru = Native(2, "Безопасность — Надстройка без подписи");

            var targets = ResetPlanner.TaskDialogTargets(new[] { en, ru }, RevitPid, RevitDialogs.BuiltIn);

            Assert.That(targets.Select(t => t.Window), Is.EqualTo(new[] { en, ru }));
            Assert.That(targets.Select(t => t.Known), Is.EqualTo(new[] { RevitDialogs.UnsignedAddinEn, RevitDialogs.UnsignedAddinRu }));
        }

        [Test]
        public void Native_window_of_other_process_is_ignored()
        {
            Assert.That(ResetPlanner.TaskDialogTargets(new[] { Native(1, "Security - Unsigned Add-In", processId: RevitPid + 1) }, RevitPid, RevitDialogs.BuiltIn), Is.Empty);
        }

        [Test]
        public void Native_window_of_other_class_is_ignored()
        {
            Assert.That(ResetPlanner.TaskDialogTargets(new[] { Native(1, "Security - Unsigned Add-In", className: "HwndWrapper[Revit.exe;;1]") }, RevitPid, RevitDialogs.BuiltIn), Is.Empty);
        }

        [Test]
        public void Hidden_native_window_is_ignored()
        {
            Assert.That(ResetPlanner.TaskDialogTargets(new[] { Native(1, "Security - Unsigned Add-In", visible: false) }, RevitPid, RevitDialogs.BuiltIn), Is.Empty);
        }

        [Test]
        public void Native_window_with_unknown_title_is_ignored()
        {
            Assert.That(ResetPlanner.TaskDialogTargets(new[] { Native(1, "Autodesk Revit 2022"), Native(2, "Warning - can be ignored") }, RevitPid, RevitDialogs.BuiltIn), Is.Empty);
        }

        [Test]
        public void Native_window_matching_entry_with_other_action_is_not_pressed()
        {
            KnownDialog answer = new KnownDialog("answer", "Security - Unsigned Add-In", null, KnownDialogAction.Answer, "Always Load");
            KnownDialog close = new KnownDialog("close", "Security - Unsigned Add-In", null, KnownDialogAction.Close);
            NativeWindowInfo[] windows = { Native(1, "Security - Unsigned Add-In") };

            Assert.That(ResetPlanner.TaskDialogTargets(windows, RevitPid, new[] { answer }), Is.Empty);
            Assert.That(ResetPlanner.TaskDialogTargets(windows, RevitPid, new[] { close, RevitDialogs.UnsignedAddinEn }), Is.Empty, "первая подходящая запись каталога решает — как в ResetPlanner.Next");
        }

        [Test]
        public void Failed_post_to_closed_window_means_gone_not_failure()
        {
            Assert.That(RevitNative.Outcome(posted: true, windowExists: true), Is.EqualTo(PostOutcome.Posted));
            Assert.That(RevitNative.Outcome(posted: true, windowExists: false), Is.EqualTo(PostOutcome.Posted), "сообщение принято, окно закрылось после — это ответ");
            Assert.That(RevitNative.Outcome(posted: false, windowExists: false), Is.EqualTo(PostOutcome.WindowGone), "окно закрылось до PostMessage — не сбой");
            Assert.That(RevitNative.Outcome(posted: false, windowExists: true), Is.EqualTo(PostOutcome.Failed), "живое окно не приняло сообщение — сбой");
        }

        /// <summary>
        /// Подсказка ленты (Evidence/closing-run-2022.txt, запуск D; ribbon-pulldown-2022.txt, запуск A): окно Popup, под ним ToolTip
        /// ClassName «ToolTip», Name «Autodesk.Windows.RibbonToolTip». Esc её не закрывает, а модальной она не бывает — уборку не останавливает.
        /// </summary>
        [Test]
        public void Ribbon_tooltip_popup_is_recognized_and_other_popup_is_not()
        {
            using (UIA3Automation automation = new UIA3Automation())
            {
                FakeNode tip = new FakeNode(automation, "", ControlType.Window, "", "Popup") { FakeHandle = new IntPtr(0x400) };
                tip.Add(new FakeNode(automation, "", ControlType.ToolTip, "Autodesk.Windows.RibbonToolTip", "ToolTip")
                    .Add(new FakeNode(automation, "mTitle", ControlType.Text, "View Templates", "TextBlock")));
                FakeNode menu = new FakeNode(automation, "_PopupRoot", ControlType.Window, "", "Popup") { FakeHandle = new IntPtr(0x500) };
                menu.Add(new FakeNode(automation, "HID_SETTINGS_RibbonListButton", ControlType.List, "Additional Settings", "ListBox"));

                List<DialogInfo> infos = RevitWindows.Collect(new[] { tip.Element, menu.Element }, new IntPtr(0x100), h => true, true);

                Assert.That(infos.Select(i => i.IsPopup), Is.EqualTo(new[] { true, true }));
                Assert.That(infos.Select(i => i.IsRibbonToolTip), Is.EqualTo(new[] { true, false }));
            }
        }

        [Test]
        public void Ribbon_tooltip_does_not_stop_reset_while_unknown_popup_still_does()
        {
            DialogInfo tip = new DialogInfo(null, new IntPtr(0x400), "", "Popup", "View Templates", true, true);
            DialogInfo popup = Window("", popup: true, className: "Popup");

            Assert.That(ResetPlanner.Next(new[] { tip }, true, RevitDialogs.BuiltIn).Kind, Is.EqualTo(ResetStepKind.Done), "подсказка ленты — не модальное окно и не провал");
            Assert.That(ResetPlanner.Next(new[] { tip }, false, RevitDialogs.BuiltIn).Kind, Is.EqualTo(ResetStepKind.MainDisabled), "подсказка не выдаётся за модальное окно");
            ResetStep step = ResetPlanner.Next(new[] { tip, popup }, true, RevitDialogs.BuiltIn);
            Assert.That(step.Kind, Is.EqualTo(ResetStepKind.Escape));
            Assert.That(step.Window, Is.SameAs(popup), "обычное всплывающее окно по-прежнему закрывается Esc, а не закрывшееся — в Remaining");
        }

        [Test]
        public void Native_window_description_names_title_class_and_handle()
        {
            Assert.That(Native(0x1A2B, "Security - Unsigned Add-In").Describe(), Is.EqualTo("«Security - Unsigned Add-In» [#32770] HWND 0x1A2B"));
        }

        [Test]
        public void Main_window_texts_are_not_read_when_listing_windows()
        {
            using (UIA3Automation automation = new UIA3Automation())
            {
                FakeNode main = new FakeNode(automation, "", ControlType.Window, "Autodesk Revit 2022.1", "Window") { FakeHandle = new IntPtr(0x100) };
                main.Add(new FakeNode(automation, "", ControlType.Text, "Architecture"));
                FakeNode dialog = new FakeNode(automation, "", ControlType.Window, "Warning", "#32770") { FakeHandle = new IntPtr(0x200) };
                dialog.Add(new FakeNode(automation, "", ControlType.Text, "Elements overlap"));
                FakeNode hidden = new FakeNode(automation, "", ControlType.Window, "Family", "Afx") { FakeHandle = new IntPtr(0x300) };

                List<DialogInfo> infos = RevitWindows.Collect(new[] { main.Element, dialog.Element, hidden.Element }, main.FakeHandle, h => h != hidden.FakeHandle, true);

                Assert.That(infos.Select(i => i.Title), Is.EqualTo(new[] { "Warning" }));
                Assert.That(infos[0].Text, Is.EqualTo("Elements overlap"));
                Assert.That(main.FindChildrenCalls, Is.EqualTo(0), "дерево главного окна не обходится: у живого Revit с моделью это десятки секунд");
                Assert.That(main.PropertyReads, Is.EqualTo(1), "у главного окна читается только HWND");
                Assert.That(hidden.FindChildrenCalls, Is.EqualTo(0), "скрытое окно не читается");
            }
        }

        [Test]
        public void Owned_dialog_is_listed_although_uia_shows_it_under_its_owner()
        {
            // задача 11, живой Revit 2022: окно «Project Units» (#32770, владелец — главное окно) в UI Automation — потомок главного окна, а не
            // рабочего стола; перечень окон по детям рабочего стола его не видел — OpenedBy, уборка и Quit не находили модальное окно.
            // Здесь то же отношение владения в отдельном процессе: окна вне экрана, видимые (IsWindowVisible), без панели задач.
            const string script = "Add-Type -AssemblyName System.Windows.Forms; " +
                "function New-RutForm($t) { $f = New-Object System.Windows.Forms.Form; $f.Text = $t; $f.ShowInTaskbar = $false; $f.StartPosition = 'Manual'; $f.Location = New-Object System.Drawing.Point(-12000, -12000); $f }; " +
                "$owner = New-RutForm 'RUT-owner'; $owned = New-RutForm 'RUT-owned'; $free = New-RutForm 'RUT-free'; " +
                "$owner.Add_Shown({ $free.Show(); $owned.Show($owner) }); [System.Windows.Forms.Application]::Run($owner)";
            ProcessStartInfo info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true };
            using (Process process = Process.Start(info))
            using (UIA3Automation automation = new UIA3Automation())
            {
                try
                {
                    IntPtr owner = IntPtr.Zero;
                    Wait.Until(() => (owner = Shown(process.Id, "RUT-owner")) != IntPtr.Zero && Shown(process.Id, "RUT-owned") != IntPtr.Zero && Shown(process.Id, "RUT-free") != IntPtr.Zero,
                        TimeSpan.FromSeconds(30), "окна RUT-owner, RUT-owned и RUT-free показаны");
                    Assert.That(automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(process.Id)).Select(w => w.SafeName()), Does.Not.Contain("RUT-owned"),
                        "предпосылка: UI Automation показывает окно с владельцем под владельцем, а не под рабочим столом");

                    RevitWindows windows = new RevitWindows(automation, process.Id, () => owner, null);

                    // окно главного окна — первым: отчёт уборки называет первое окно, а модальное окно Revit — именно оно, а не «Family» (ревью задачи 11)
                    Assert.That(windows.All().Select(w => w.SafeName()), Is.EqualTo(new[] { "RUT-owned", "RUT-free" }));
                }
                finally
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
        }

        [Test]
        public void Only_window_children_of_main_except_popups_are_owned_dialogs()
        {
            // Evidence: «Project Units» — [Window] cls="#32770" прямой потомок главного окна; прочие прямые потомки главного окна — Pane, Custom,
            // Button, Tab, Thumb, TitleBar (ribbon-collapsed-2022.txt). Всплывающее окно WPF (cls «Popup») под главным окном модальным не считается.
            using (UIA3Automation automation = new UIA3Automation())
            {
                FakeNode main = new FakeNode(automation, "", ControlType.Window, "Autodesk Revit 2022.1", "Window").Add(
                    new FakeNode(automation, "60164", ControlType.Pane, "Properties", "Afx:ControlBar:94280000:8:10003:10"),
                    new FakeNode(automation, "Architecture", ControlType.Button, "Architecture", "Button"),
                    new FakeNode(automation, "_PopupRoot", ControlType.Window, "", RevitIds.Dialogs.PopupClass),
                    new FakeNode(automation, "", ControlType.Window, "Project Units", "#32770"));

                List<string> owned = RevitWindows.OwnedDialogs(main.Element).Select(w => w.Properties.Name.Value).ToList();

                Assert.That(owned, Is.EqualTo(new[] { "Project Units" }));
            }
        }

        private static IntPtr Shown(int processId, string title)
        {
            return RevitNative.TopLevelWindows(processId).Where(w => w.IsVisible && w.Title == title).Select(w => w.Handle).FirstOrDefault();
        }
    }
}
