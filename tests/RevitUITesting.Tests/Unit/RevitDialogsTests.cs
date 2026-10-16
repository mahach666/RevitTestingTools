using System.Collections.Generic;
using NUnit.Framework;
using RevitUITesting.Revit;

namespace RevitUITesting.Tests.Unit
{
    [TestFixture]
    public class RevitDialogsTests
    {
        [TestCase("Save File", "Do you want to save changes to new-1.rvt?")]
        [TestCase("Autodesk Revit 2022", "Do you want to save changes to Project1.rvt?")]
        [TestCase("Revit", "DO YOU WANT TO SAVE CHANGES to new-1?")]
        public void Save_changes_prompt_is_answered_No_by_button_id_not_by_caption(string title, string text)
        {
            KnownDialog match = RevitDialogs.Match(RevitDialogs.BuiltIn, title, text);

            Assert.That(match, Is.SameAs(RevitDialogs.SaveChanges));
            // Evidence/save-changes-2022.txt: подписи кнопок не следуют языку Revit («Да | Нет | Отмена» при Revit ENU), id кнопки «No» — 7 (CommandButton_7)
            Assert.That(match.Action, Is.EqualTo(KnownDialogAction.TaskDialogButton));
            Assert.That(match.TaskDialogButtonId, Is.EqualTo(7));
            Assert.That(match.TaskDialogButtonId, Is.EqualTo(RevitIds.Dialogs.TaskDialogNoButtonId));
            Assert.That(match.AnswerCaption, Is.Null);
        }

        [TestCase("Security - Unsigned Add-In")]
        [TestCase("Безопасность — Надстройка без подписи")]
        public void Unsigned_addin_is_answered_always_load(string title)
        {
            KnownDialog match = RevitDialogs.Match(RevitDialogs.BuiltIn, title, "The add-in AcmeDev has not been signed");

            Assert.That(match.Action, Is.EqualTo(KnownDialogAction.TaskDialogButton));
            Assert.That(match.TaskDialogButtonId, Is.EqualTo(1001));
            Assert.That(match.TaskDialogButtonId, Is.EqualTo(RevitIds.Dialogs.UnsignedAddinAlwaysLoadButtonId));
        }

        [Test]
        public void TaskDialogButton_needs_an_id_and_other_actions_must_not_have_one()
        {
            Assert.That(() => new KnownDialog("x", "T", null, KnownDialogAction.TaskDialogButton), Throws.ArgumentException.With.Message.Contains("x"));
            Assert.That(() => new KnownDialog("y", "T", null, KnownDialogAction.TaskDialogButton, taskDialogButtonId: -1), Throws.ArgumentException.With.Message.Contains("y"));
            Assert.That(() => new KnownDialog("z", "T", null, KnownDialogAction.Close, taskDialogButtonId: 1001), Throws.ArgumentException.With.Message.Contains("z"));
            Assert.That(() => new KnownDialog("w", "T", null, KnownDialogAction.Answer, "OK", taskDialogButtonId: 1), Throws.ArgumentException.With.Message.Contains("w"));

            KnownDialog ok = new KnownDialog("ok", "T", null, KnownDialogAction.TaskDialogButton, taskDialogButtonId: 1002);
            Assert.That(ok.TaskDialogButtonId, Is.EqualTo(1002));
            Assert.That(ok.ToString(), Does.Contain("1002"));
        }

        [TestCase("Warning - can be ignored", "revit-warning")]
        [TestCase("Error - cannot be ignored", "revit-error")]
        public void Revit_warnings_and_errors_are_closed_and_reported(string title, string name)
        {
            KnownDialog match = RevitDialogs.Match(RevitDialogs.BuiltIn, title, "Highlighted walls overlap.");

            Assert.That(match.Name, Is.EqualTo(name));
            Assert.That(match.Action, Is.EqualTo(KnownDialogAction.Close));
            Assert.That(match.IsRevitError, Is.True);
        }

        [Test]
        public void Exact_title_is_not_a_prefix_and_unknown_is_null()
        {
            KnownDialog units = new KnownDialog("units", "Project Units", null, KnownDialogAction.Close);

            Assert.That(units.Matches("project units", ""), Is.True);
            Assert.That(units.Matches("Project Units 2", ""), Is.False);
            Assert.That(RevitDialogs.Match(RevitDialogs.BuiltIn, "Project Units", "Units"), Is.Null);
            Assert.That(RevitDialogs.Match(RevitDialogs.BuiltIn, "Warn", ""), Is.Null);
        }

        [Test]
        public void Config_entries_come_first_and_without_answer_mean_close()
        {
            IReadOnlyList<KnownDialog> catalog = RevitDialogs.Catalog(new[]
            {
                new KnownDialogSetting { Title = "Warning*", TextContains = "Семейство", Answer = "OK" },
                new KnownDialogSetting { Title = "Источники" },
            });

            KnownDialog warning = RevitDialogs.Match(catalog, "Warning", "Семейство уже загружено");
            KnownDialog sources = RevitDialogs.Match(catalog, "Источники", "");

            Assert.That(warning.Name, Is.EqualTo("knownDialogs[0]"));
            Assert.That(warning.Action, Is.EqualTo(KnownDialogAction.Answer));
            Assert.That(warning.AnswerCaption, Is.EqualTo("OK"));
            Assert.That(sources.Action, Is.EqualTo(KnownDialogAction.Close));
            Assert.That(RevitDialogs.Match(catalog, "Warning", "другое").Name, Is.EqualTo("revit-warning"));
            Assert.That(catalog.Count, Is.EqualTo(RevitDialogs.BuiltIn.Count + 2));
        }

        [Test]
        public void Invalid_entries_are_rejected_with_their_index()
        {
            Assert.That(() => RevitDialogs.Catalog(new[] { new KnownDialogSetting() }), Throws.ArgumentException.With.Message.Contains("knownDialogs[0]"));
            Assert.That(() => RevitDialogs.Catalog(new KnownDialogSetting[] { null }), Throws.ArgumentException.With.Message.Contains("knownDialogs[0]"));
            Assert.That(() => new KnownDialog("x", "T", null, KnownDialogAction.Answer), Throws.ArgumentException.With.Message.Contains("подпись кнопки"));
            Assert.That(RevitDialogs.Catalog(null), Has.Count.EqualTo(RevitDialogs.BuiltIn.Count));
        }

        [Test]
        public void Catalog_constants_are_as_seen_in_revit()
        {
            Assert.That(RevitTitles.TestHost.RunAllTests, Is.EqualTo("Run All\nTests"));
            Assert.That(RevitIds.TestHost.RunAllTests, Is.EqualTo("RunAllTests"));
            Assert.That(RevitIds.TestHost.AddinId, Is.EqualTo("770c23e2-3b3a-4ea3-89a2-d59899bed9a0"));
            Assert.That(RevitTitles.Windows.MainPrefix, Is.EqualTo("Autodesk Revit "));
        }
    }
}
