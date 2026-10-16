using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Contract
{
    /// <summary>Изменение документа и проверка закрытия Revit для контрактных тестов.</summary>
    internal static class ContractModel
    {
        /// <summary>
        /// [Р] Evidence/view-3d-2022.txt (задача 12, прогон 1): «3D View» на вкладке View — разделённая кнопка: Button «…_FlyoutButtonShowFlyout»
        /// (Toggle, раскрывает меню) и Button «…_FlyoutButtonExecute» (Invoke), обе с Name «3D\r\nView» — по подписи кнопка неоднозначна,
        /// поэтому меню раскрывается по AutomationId.
        /// </summary>
        public const string ThreeDViewFlyoutId = "ID_VIEW_DEFAULT_3DVIEW_FlyoutButtonShowFlyout";

        /// <summary>Изменить активный документ: View › 3D View › Default 3D View (в новом проекте создаёт вид {3D}); ждать IsModified.</summary>
        public static void ModifyActiveDocument(RevitApp app)
        {
            UiLog.Step("Изменить документ: View › 3D View › Default 3D View");
            app.Ribbon.ClickInMenu(RevitTitles.Tabs.View, ThreeDViewFlyoutId, RevitTitles.View.DefaultThreeDView);
            Wait.Until(() => app.Host.Documents().Single(d => d.IsActive).IsModified, TimeSpan.FromSeconds(60), "активный документ изменён");
        }

        /// <summary>
        /// Закрыть Revit действием close и проверить: процесс завершён, а в журнале шагов от начала закрытия нет принудительного завершения
        /// (строка RevitApp.Kill «Revit pid … завершён принудительно», RevitApp.KilledText). Журнал — хвост UiLog (200 строк); метка начала из него выпала — провал.
        /// </summary>
        public static void AssertClosedWithoutKill(RevitApp app, Action close)
        {
            int pid = app.ProcessId;
            string marker = $"проверка закрытия Revit pid {pid} без Kill: начало {Guid.NewGuid():N}";
            UiLog.Action(marker);
            close();
            IReadOnlyList<string> tail = UiLog.Recent(200);
            int start = tail.ToList().FindIndex(l => l.EndsWith(marker, StringComparison.Ordinal));
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "метка начала закрытия выпала из хвоста журнала (200 строк) — проверка без Kill невозможна");
            string[] kills = tail.Skip(start).Where(l => l.Contains($"Revit pid {pid} {RevitApp.KilledText}")).ToArray();
            Assert.That(kills, Is.Empty, "Revit закрыт принудительно (Kill), а не через UI");
            Assert.That(app.Process.HasExited, Is.True, $"Revit pid {pid} завершён");
            UiLog.Action($"Revit pid {pid} закрыт без Kill");
        }
    }
}
