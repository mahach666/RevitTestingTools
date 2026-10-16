using System;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;

namespace RevitUITesting.Uia
{
    /// <summary>Способ переключить флажок.</summary>
    public enum CheckInput
    {
        /// <summary>TogglePattern.Toggle (по умолчанию).</summary>
        TogglePattern,

        /// <summary>
        /// Клик мышью: для окон, где доступность кнопок пересчитывает CommandManager только на ввод пользователя
        /// (программный Toggle не создаёт ввода). Требует DPI-осведомлённости процесса тестов.
        /// </summary>
        MouseClick,
    }

    /// <summary>Выбор пункта раскрывающегося списка и флажки — общая логика для page-objects.</summary>
    internal static class UiChoice
    {
        /// <summary>
        /// Раскрыть список (ExpandCollapse), найти пункт ListItem с точной подписью (под списком или во всплывающем окне из popups()),
        /// выбрать его: SelectionItem.Select, а если паттерна нет — кликом мыши. Если у списка есть ValuePattern — дождаться этого значения.
        /// Возвращает подписи всех пунктов этого открытия.
        /// </summary>
        internal static IReadOnlyList<string> Choose(Func<IEnumerable<AutomationElement>> popups, AutomationElement combo, string itemCaption, string description)
        {
            if (popups == null)
            {
                throw new ArgumentNullException(nameof(popups));
            }
            if (combo == null)
            {
                throw new ArgumentNullException(nameof(combo));
            }
            IExpandCollapsePattern expand = UiQuery.Read(() => combo.Patterns.ExpandCollapse.PatternOrDefault, Wait.DefaultTimeout, "ExpandCollapse " + description)
                ?? throw new InvalidOperationException($"{description}: не раскрывается (нет ExpandCollapsePattern) — это не список ({combo.Describe()}).");
            UiAct.RunOnce(combo, "раскрыть " + description, expand.Expand, "Expand");
            By item = By.Where(f => f.ControlType == ControlType.ListItem && Caption(f.Element) == itemCaption, $"пункт «{itemCaption}»");
            AutomationElement found = Wait.ForStrict(() => FindItem(popups, combo, item), Wait.DefaultTimeout, $"пункт «{itemCaption}» в {description}", null);
            IReadOnlyList<string> captions = Siblings(found);
            ISelectionItemPattern select = UiQuery.Read(() => found.Patterns.SelectionItem.PatternOrDefault, Wait.DefaultTimeout, "SelectionItem пункта");
            if (select != null)
            {
                UiAct.RunOnce(found, $"выбрать «{itemCaption}» в {description}", select.Select, "Select");
            }
            else
            {
                UiAct.Click(found, $"выбрать «{itemCaption}» в {description}", MouseButton.Left, false);
            }
            VerifyChosen(combo, itemCaption, description);
            return captions;
        }

        /// <summary>
        /// Если у списка есть ValuePattern — дождаться в нём выбранной подписи (до 3 с); иначе — <see cref="ValueNotAppliedException"/>.
        /// Значение читается строго: занятое приложение — <see cref="AppBusyException"/>, а не «пусто».
        /// </summary>
        internal static void VerifyChosen(AutomationElement combo, string itemCaption, string description)
        {
            if (!UiQuery.Read(() => combo.Patterns.Value.IsSupported, Wait.DefaultTimeout, "ValuePattern " + description))
            {
                return;
            }
            string actual = "";
            if (!Wait.Holds(() => (actual = UiQuery.Read(() => combo.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault ?? "", Wait.DefaultTimeout, "значение " + description)) == itemCaption, TimeSpan.FromSeconds(3)))
            {
                throw new ValueNotAppliedException(description, itemCaption, actual);
            }
        }

        /// <summary>Подписи пунктов списка: раскрыть, прочитать, свернуть (ExpandCollapse.Collapse) с проверкой свёрнутости.</summary>
        internal static IReadOnlyList<string> Items(Func<IEnumerable<AutomationElement>> popups, AutomationElement combo, string description)
        {
            if (popups == null)
            {
                throw new ArgumentNullException(nameof(popups));
            }
            if (combo == null)
            {
                throw new ArgumentNullException(nameof(combo));
            }
            IExpandCollapsePattern expand = UiQuery.Read(() => combo.Patterns.ExpandCollapse.PatternOrDefault, Wait.DefaultTimeout, "ExpandCollapse " + description)
                ?? throw new InvalidOperationException($"{description}: не раскрывается (нет ExpandCollapsePattern) — это не список ({combo.Describe()}).");
            UiAct.RunOnce(combo, "раскрыть " + description, expand.Expand, "Expand");
            AutomationElement first = Wait.ForStrict(() => FindItem(popups, combo, By.Type(ControlType.ListItem)), Wait.DefaultTimeout, "пункты " + description, null);
            IReadOnlyList<string> captions = Siblings(first);
            UiAct.RunOnce(combo, "свернуть " + description, expand.Collapse, "Collapse");
            Wait.Strict(() => UiQuery.Read(() => expand.ExpandCollapseState.Value, Wait.DefaultTimeout, "раскрытие " + description) == ExpandCollapseState.Collapsed,
                Wait.DefaultTimeout, description + " свёрнут", null);
            return captions;
        }

        /// <summary>Флажок в заданное состояние; постусловие — прочитанное состояние.</summary>
        internal static void SetChecked(AutomationElement element, bool value, CheckInput input, string description)
        {
            ToggleState target = value ? ToggleState.On : ToggleState.Off;
            if (State(element, description) == target)
            {
                return;
            }
            if (input == CheckInput.MouseClick)
            {
                UiAct.Click(element, (value ? "отметить " : "снять ") + description, MouseButton.Left, false);
            }
            else
            {
                ITogglePattern toggle = UiQuery.Read(() => element.Patterns.Toggle.PatternOrDefault, Wait.DefaultTimeout, "Toggle " + description);
                UiAct.RunOnce(element, (value ? "отметить " : "снять ") + description, toggle.Toggle, "Toggle");
            }
            Wait.Strict(() => State(element, description) == target, Wait.DefaultTimeout, $"{description} = {(value ? "вкл" : "выкл")}", () => State(element, description).ToString());
        }

        /// <summary>Состояние TogglePattern без подмены сбоя; нет паттерна — исключение.</summary>
        internal static ToggleState State(AutomationElement element, string description)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            ToggleState? state = UiQuery.Read(() => element.Patterns.Toggle.PatternOrDefault?.ToggleState.Value, Wait.DefaultTimeout, "состояние " + description);
            return state ?? throw new InvalidOperationException($"{description}: нет TogglePattern ({element.Describe()}).");
        }

        /// <summary>
        /// Подпись пункта списка: текст дочернего TextBlock, если он есть, иначе Name. У пунктов с моделью-объектом Name — имя типа .NET,
        /// а подпись только в тексте.
        /// </summary>
        internal static string Caption(AutomationElement item)
        {
            return item.Texts().FirstOrDefault() ?? UiQuery.Read(() => item.Properties.Name.ValueOrDefault ?? "", Wait.DefaultTimeout, "имя пункта");
        }

        /// <summary>Пункт под самим списком, иначе во всплывающих окнах popups() (первое найденное).</summary>
        private static AutomationElement FindItem(Func<IEnumerable<AutomationElement>> popups, AutomationElement combo, By item)
        {
            AutomationElement underCombo = UiQuery.TryFind(combo, item, Wait.DefaultTimeout, 6);
            if (underCombo != null)
            {
                return underCombo;
            }
            IEnumerable<AutomationElement> windows = popups() ?? throw new InvalidOperationException("Поставщик всплывающих окон вернул null вместо перечня окон.");
            return windows.Select(p => UiQuery.TryFind(p, item, Wait.DefaultTimeout, 8)).FirstOrDefault(e => e != null);
        }

        private static IReadOnlyList<string> Siblings(AutomationElement item)
        {
            AutomationElement parent = UiQuery.Read(() => item.Parent, Wait.DefaultTimeout, "контейнер пунктов");
            if (parent == null)
            {
                return new[] { Caption(item) };
            }
            AutomationElement[] children = UiQuery.Read(() => parent.FindAllChildren(), Wait.DefaultTimeout, "пункты списка");
            return children.Where(c => UiQuery.Read(() => c.Properties.ControlType.Value, Wait.DefaultTimeout, "тип пункта") == ControlType.ListItem).Select(Caption).ToList();
        }
    }
}
