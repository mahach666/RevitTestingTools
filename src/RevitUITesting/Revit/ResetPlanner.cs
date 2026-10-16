using System;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;

namespace RevitUITesting.Revit
{
    /// <summary>Окно процесса Revit, прочитанное одним проходом (заголовок, класс, тексты).</summary>
    internal sealed class DialogInfo
    {
        internal DialogInfo(AutomationElement element, IntPtr handle, string title, string className, string text, bool isPopup, bool isRibbonToolTip = false)
        {
            Element = element;
            Handle = handle;
            Title = title ?? "";
            ClassName = className ?? "";
            Text = text ?? "";
            IsPopup = isPopup;
            IsRibbonToolTip = isRibbonToolTip;
        }

        internal AutomationElement Element { get; }

        internal IntPtr Handle { get; }

        internal string Title { get; }

        internal string ClassName { get; }

        internal string Text { get; }

        internal bool IsPopup { get; }

        /// <summary>Всплывающая подсказка ленты (<see cref="RevitIds.Dialogs.RibbonToolTipName"/>): не модальное окно и не провал, уборка её не трогает.</summary>
        internal bool IsRibbonToolTip { get; }

        internal string Describe()
        {
            string text = Text.Length > 300 ? Text.Substring(0, 300) + "…" : Text;
            return $"«{Title}» [{ClassName}]" + (text.Length == 0 ? "" : ": " + text.Replace(Environment.NewLine, " / "));
        }
    }

    /// <summary>Окно верхнего уровня, прочитанное через user32 (EnumWindows) без UI Automation.</summary>
    internal sealed class NativeWindowInfo
    {
        internal NativeWindowInfo(IntPtr handle, int processId, string className, string title, bool isVisible)
        {
            Handle = handle;
            ProcessId = processId;
            ClassName = className ?? "";
            Title = title ?? "";
            IsVisible = isVisible;
        }

        internal IntPtr Handle { get; }

        internal int ProcessId { get; }

        internal string ClassName { get; }

        internal string Title { get; }

        internal bool IsVisible { get; }

        internal string Describe()
        {
            return $"«{Title}» [{ClassName}] HWND {RevitNative.Hex(Handle)}";
        }
    }

    internal enum ResetStepKind
    {
        Done,
        Escape,
        Answer,
        Close,
        Fail,
        UnknownModal,
        MainDisabled,
        TaskDialogButton,
    }

    internal sealed class ResetStep
    {
        internal ResetStep(ResetStepKind kind, DialogInfo window, KnownDialog known)
        {
            Kind = kind;
            Window = window;
            Known = known;
        }

        internal ResetStepKind Kind { get; }

        internal DialogInfo Window { get; }

        internal KnownDialog Known { get; }
    }

    /// <summary>
    /// Следующий шаг уборки UI: всплывающее окно — Esc (кроме подсказки ленты: она не модальная, Esc её не закрывает, и уборка её не трогает);
    /// окно из каталога — его действие; главное окно недоступно (модальное состояние) — неизвестное окно означает провал; главное окно
    /// доступно — немодальные окна плагинов не трогаются.
    /// </summary>
    internal static class ResetPlanner
    {
        internal static ResetStep Next(IReadOnlyList<DialogInfo> windows, bool mainEnabled, IReadOnlyList<KnownDialog> catalog)
        {
            DialogInfo popup = windows.FirstOrDefault(w => w.IsPopup && !w.IsRibbonToolTip);
            if (popup != null)
            {
                return new ResetStep(ResetStepKind.Escape, popup, null);
            }
            foreach (DialogInfo window in windows.Where(w => !w.IsPopup))
            {
                KnownDialog known = Match(catalog, window);
                if (known != null)
                {
                    return new ResetStep(Map(known.Action), window, known);
                }
            }
            if (mainEnabled)
            {
                return new ResetStep(ResetStepKind.Done, null, null);
            }
            DialogInfo unknown = windows.FirstOrDefault(w => !w.IsPopup);
            return unknown != null ? new ResetStep(ResetStepKind.UnknownModal, unknown, null) : new ResetStep(ResetStepKind.MainDisabled, null, null);
        }

        /// <summary>
        /// TaskDialog процесса Revit, которые нажимаются сообщением TDM_CLICK_BUTTON, среди окон из EnumWindows: видимое окно процесса processId
        /// класса <see cref="RevitIds.Dialogs.TaskDialogClass"/>, первая подходящая запись каталога которого — <see cref="KnownDialogAction.TaskDialogButton"/>.
        /// Текст окна user32 не читает, поэтому запись с <see cref="KnownDialog.TextContains"/> здесь не совпадает (сверка только по заголовку);
        /// запись потребителя с другим действием и тем же заголовком перекрывает встроенную, как в <see cref="Next"/>.
        /// </summary>
        internal static IReadOnlyList<(NativeWindowInfo Window, KnownDialog Known)> TaskDialogTargets(IEnumerable<NativeWindowInfo> windows, int processId, IReadOnlyList<KnownDialog> catalog)
        {
            List<(NativeWindowInfo Window, KnownDialog Known)> result = new List<(NativeWindowInfo Window, KnownDialog Known)>();
            foreach (NativeWindowInfo window in windows)
            {
                if (window.ProcessId != processId || !window.IsVisible || window.ClassName != RevitIds.Dialogs.TaskDialogClass)
                {
                    continue;
                }
                KnownDialog known = RevitDialogs.Match(catalog, window.Title, null);
                if (known != null && known.Action == KnownDialogAction.TaskDialogButton)
                {
                    result.Add((window, known));
                }
            }
            return result;
        }

        /// <summary>
        /// Запись каталога подходит к окну: заголовок и текст совпадают, а запись <see cref="KnownDialogAction.TaskDialogButton"/> — только для окна
        /// класса <see cref="RevitIds.Dialogs.TaskDialogClass"/>: TDM_CLICK_BUTTON понимает лишь TaskDialog, а окно плагина (WPF, WinForms) с тем же
        /// текстом («save changes») может истолковать сообщение WM_USER+102 по-своему. К окну другого класса такая запись не применяется —
        /// каталог проверяется дальше, а без подходящей записи окно — неизвестное.
        /// </summary>
        internal static bool Applies(KnownDialog known, DialogInfo window)
        {
            if (known.Action == KnownDialogAction.TaskDialogButton && window.ClassName != RevitIds.Dialogs.TaskDialogClass)
            {
                return false;
            }
            return known.Matches(window.Title, window.Text);
        }

        /// <summary>Первая запись каталога, подходящая к окну (<see cref="Applies"/>); нет — null.</summary>
        internal static KnownDialog Match(IEnumerable<KnownDialog> catalog, DialogInfo window)
        {
            return (catalog ?? RevitDialogs.BuiltIn).FirstOrDefault(d => Applies(d, window));
        }

        private static ResetStepKind Map(KnownDialogAction action)
        {
            switch (action)
            {
                case KnownDialogAction.Answer:
                    return ResetStepKind.Answer;
                case KnownDialogAction.Close:
                    return ResetStepKind.Close;
                case KnownDialogAction.Fail:
                    return ResetStepKind.Fail;
                case KnownDialogAction.TaskDialogButton:
                    return ResetStepKind.TaskDialogButton;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, "Неизвестное действие каталога диалогов.");
            }
        }
    }
}
