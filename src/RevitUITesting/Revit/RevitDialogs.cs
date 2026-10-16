using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitUITesting.Revit
{
    /// <summary>Что делать с известным окном при уборке UI.</summary>
    public enum KnownDialogAction
    {
        /// <summary>Нажать кнопку с подписью <see cref="KnownDialog.AnswerCaption"/>.</summary>
        Answer,

        /// <summary>Закрыть окно (WM_CLOSE — для TaskDialog это «Cancel»).</summary>
        Close,

        /// <summary>Не трогать и остановить тест с текстом окна и подсказкой.</summary>
        Fail,

        /// <summary>
        /// Нажать кнопку TaskDialog по её числовому id сообщением TDM_CLICK_BUTTON (<see cref="KnownDialog.TaskDialogButtonId"/>).
        /// Для окон, до которых UI Automation не достаёт: их нет в дереве от корня, а у кнопок нет паттерна Invoke.
        /// </summary>
        TaskDialogButton,
    }

    /// <summary>Запись каталога известных окон Revit: условие (заголовок и/или фрагмент текста) и действие.</summary>
    public sealed class KnownDialog
    {
        public KnownDialog(string name, string title, string textContains, KnownDialogAction action, string answerCaption = null, bool isRevitError = false, string hint = null, int taskDialogButtonId = 0)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Нужно имя записи каталога диалогов.", nameof(name));
            }
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(textContains))
            {
                throw new ArgumentException($"Запись каталога диалогов «{name}»: нужен заголовок (title) или фрагмент текста (textContains).", nameof(title));
            }
            if (action == KnownDialogAction.Answer && string.IsNullOrWhiteSpace(answerCaption))
            {
                throw new ArgumentException($"Запись каталога диалогов «{name}»: для ответа нужна подпись кнопки.", nameof(answerCaption));
            }
            if (action == KnownDialogAction.TaskDialogButton && taskDialogButtonId <= 0)
            {
                throw new ArgumentException($"Запись каталога диалогов «{name}»: для нажатия кнопки TaskDialog нужен её id больше нуля.", nameof(taskDialogButtonId));
            }
            if (action != KnownDialogAction.TaskDialogButton && taskDialogButtonId != 0)
            {
                throw new ArgumentException($"Запись каталога диалогов «{name}»: id кнопки TaskDialog задаётся только для действия {nameof(KnownDialogAction.TaskDialogButton)}.", nameof(taskDialogButtonId));
            }
            Name = name;
            Title = title;
            TextContains = textContains;
            Action = action;
            AnswerCaption = answerCaption;
            IsRevitError = isRevitError;
            Hint = hint ?? "";
            TaskDialogButtonId = taskDialogButtonId;
        }

        public string Name { get; }

        /// <summary>Заголовок: точное совпадение без учёта регистра; «*» в конце — начало заголовка; null — любой.</summary>
        public string Title { get; }

        /// <summary>Фрагмент текста окна без учёта регистра; null — любой.</summary>
        public string TextContains { get; }

        public KnownDialogAction Action { get; }

        public string AnswerCaption { get; }

        /// <summary>Окно — ошибка или предупреждение Revit: текст попадает в отчёт теста.</summary>
        public bool IsRevitError { get; }

        /// <summary>Подсказка для сообщения об ошибке (для <see cref="KnownDialogAction.Fail"/>) или пояснение к записи (например, почему выбран <see cref="KnownDialogAction.TaskDialogButton"/>).</summary>
        public string Hint { get; }

        /// <summary>Id кнопки TaskDialog для <see cref="KnownDialogAction.TaskDialogButton"/> (например, 1001 — «Always Load»); иначе 0.</summary>
        public int TaskDialogButtonId { get; }

        public bool Matches(string title, string text)
        {
            return TitleMatches(title ?? "") && (string.IsNullOrEmpty(TextContains) || (text ?? "").IndexOf(TextContains, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public override string ToString()
        {
            return $"{Name}: заголовок «{Title ?? "*"}», текст «{TextContains ?? "*"}» → {Action}{(AnswerCaption == null ? "" : " «" + AnswerCaption + "»")}{(Action == KnownDialogAction.TaskDialogButton ? " кнопка " + TaskDialogButtonId : "")}";
        }

        private bool TitleMatches(string title)
        {
            if (string.IsNullOrEmpty(Title))
            {
                return true;
            }
            if (Title.EndsWith("*", StringComparison.Ordinal))
            {
                return title.StartsWith(Title.Substring(0, Title.Length - 1), StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(title, Title, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Каталог известных окон Revit для уборки UI (сброс UI в RevitWindows) и закрытия Revit. Записи knownDialogs из конфигурации
    /// проверяются первыми — так потребитель уточняет встроенные. Окно, которого нет в каталоге, при открытом модальном состоянии — провал теста.
    /// </summary>
    public static class RevitDialogs
    {
        /// <summary>
        /// Вопрос о сохранении при закрытии изменённого документа или Revit: «No» — сообщением TDM_CLICK_BUTTON с id <see cref="RevitIds.Dialogs.TaskDialogNoButtonId"/>.
        /// Не по подписи: подписи кнопок не следуют языку Revit (Revit 2022 ENU на test-machine — «Да | Нет | Отмена», Evidence/save-changes-2022.txt).
        /// </summary>
        public static readonly KnownDialog SaveChanges = new KnownDialog("save-changes", null, RevitTitles.Dialogs.SaveChangesText, KnownDialogAction.TaskDialogButton,
            hint: "«No» (не сохранять) нажимается по id кнопки: подписи кнопок TaskDialog не следуют языку Revit (при ENU были «Да | Нет | Отмена»)", taskDialogButtonId: RevitIds.Dialogs.TaskDialogNoButtonId);

        private const string UnsignedAddinHint = "отвечено «Always Load» сообщением TDM_CLICK_BUTTON: WM_CLOSE этого окна равносилен «Do Not Load» (Revit стартует без надстройки), "
            + "а кнопки не достать через UI Automation. trustAddins (GUID надстройки в CodeSigning) — первая линия защиты, но диалог подавляется не всегда";

        public static readonly KnownDialog UnsignedAddinEn = new KnownDialog("unsigned-addin", RevitTitles.Dialogs.UnsignedAddinEn, null, KnownDialogAction.TaskDialogButton,
            hint: UnsignedAddinHint, taskDialogButtonId: RevitIds.Dialogs.UnsignedAddinAlwaysLoadButtonId);

        public static readonly KnownDialog UnsignedAddinRu = new KnownDialog("unsigned-addin-ru", RevitTitles.Dialogs.UnsignedAddinRu, null, KnownDialogAction.TaskDialogButton,
            hint: UnsignedAddinHint, taskDialogButtonId: RevitIds.Dialogs.UnsignedAddinAlwaysLoadButtonId);

        public static readonly KnownDialog CloseWithoutSaving = new KnownDialog("close-without-saving", RevitTitles.Dialogs.CloseWithoutSaving, null, KnownDialogAction.Close);

        public static readonly KnownDialog RevitError = new KnownDialog("revit-error", RevitTitles.Dialogs.ErrorPrefix + "*", null, KnownDialogAction.Close, isRevitError: true);

        public static readonly KnownDialog RevitWarning = new KnownDialog("revit-warning", RevitTitles.Dialogs.WarningPrefix + "*", null, KnownDialogAction.Close, isRevitError: true);

        public static IReadOnlyList<KnownDialog> BuiltIn { get; } = new[] { SaveChanges, UnsignedAddinEn, UnsignedAddinRu, CloseWithoutSaving, RevitError, RevitWarning };

        /// <summary>Каталог для сеанса: сначала записи knownDialogs конфигурации (Answer пуст — закрыть), затем встроенные.</summary>
        public static IReadOnlyList<KnownDialog> Catalog(IEnumerable<KnownDialogSetting> extra)
        {
            List<KnownDialog> catalog = new List<KnownDialog>();
            int index = 0;
            foreach (KnownDialogSetting entry in extra ?? Enumerable.Empty<KnownDialogSetting>())
            {
                string name = $"knownDialogs[{index}]";
                if (entry == null)
                {
                    throw new ArgumentException(name + ": пустая запись.", nameof(extra));
                }
                bool answer = !string.IsNullOrWhiteSpace(entry.Answer);
                catalog.Add(new KnownDialog(name, entry.Title, entry.TextContains, answer ? KnownDialogAction.Answer : KnownDialogAction.Close, answer ? entry.Answer : null));
                index++;
            }
            catalog.AddRange(BuiltIn);
            return catalog;
        }

        public static KnownDialog Match(IEnumerable<KnownDialog> catalog, string title, string text)
        {
            return (catalog ?? BuiltIn).FirstOrDefault(d => d.Matches(title, text));
        }
    }
}
