using System;
using System.Collections.Generic;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using FlaUI.Core.WindowsAPI;

namespace RevitUITesting.Uia
{
    /// <summary>
    /// Действия над уже найденным элементом — для page-objects (ячейка таблицы, пункт выпадающего списка, редактор).
    /// Одно действие — один вызов (без повтора при сбое), мышь — только при совпадении координат (dpiAware) и если в точке окно
    /// приложения, клавиши — только если активно окно приложения. description попадает в журнал и ошибки.
    /// </summary>
    public static class UiActions
    {
        /// <summary>Нажать: Invoke, иначе Toggle, иначе клик мышью. Недоступный элемент — исключение до действия.</summary>
        public static void Press(AutomationElement element, string description)
        {
            UiAct.Press(element, description);
        }

        /// <summary>Клик левой кнопкой мыши в центр элемента.</summary>
        public static void Click(AutomationElement element, string description)
        {
            UiAct.Click(element, description, MouseButton.Left, false);
        }

        public static void DoubleClick(AutomationElement element, string description)
        {
            UiAct.Click(element, description, MouseButton.Left, true);
        }

        public static void RightClick(AutomationElement element, string description)
        {
            UiAct.Click(element, description, MouseButton.Right, false);
        }

        /// <summary>Выделить (SelectionItem.Select); нет паттерна — исключение.</summary>
        public static void Select(AutomationElement element, string description)
        {
            ISelectionItemPattern pattern = UiQuery.Read(() => element.Patterns.SelectionItem.PatternOrDefault, Wait.DefaultTimeout, "SelectionItem " + description)
                ?? throw new InvalidOperationException($"{description}: нет SelectionItemPattern ({element.Describe()}).");
            UiAct.RunOnce(element, description, pattern.Select, "Select");
        }

        /// <summary>Раскрыть (ExpandCollapse.Expand); нет паттерна — исключение.</summary>
        public static void Expand(AutomationElement element, string description)
        {
            IExpandCollapsePattern pattern = UiQuery.Read(() => element.Patterns.ExpandCollapse.PatternOrDefault, Wait.DefaultTimeout, "ExpandCollapse " + description)
                ?? throw new InvalidOperationException($"{description}: нет ExpandCollapsePattern ({element.Describe()}).");
            UiAct.RunOnce(element, description, pattern.Expand, "Expand");
        }

        /// <summary>Нажать клавиши (одновременно) с фокусом на элементе.</summary>
        public static void Keys(AutomationElement focusTarget, string description, params VirtualKeyShort[] keys)
        {
            UiAct.Keys(focusTarget, description, keys);
        }

        /// <summary>Набрать текст с клавиатуры в элемент (поля автодополнения, где ValuePattern не запускает поиск).</summary>
        public static void TypeText(AutomationElement focusTarget, string text, string description)
        {
            UiAct.TypeText(focusTarget, text, description);
        }

        /// <summary>Записать значение через ValuePattern и прочитать обратно; не применилось — <see cref="ValueNotAppliedException"/>.</summary>
        public static void SetValue(AutomationElement element, string value, string description)
        {
            UiAct.SetValue(element, value, description);
        }

        /// <summary>
        /// Флажок в состояние value (TogglePattern или клик мышью); постусловие — прочитанное состояние. Уже в нужном состоянии — ничего не делает.
        /// </summary>
        public static void SetChecked(AutomationElement element, bool value, CheckInput input, string description)
        {
            UiChoice.SetChecked(element, value, input, description);
        }

        /// <summary>
        /// Выбрать в раскрывающемся списке пункт с точной подписью (текст пункта, иначе Name): раскрыть, найти пункт под списком или во
        /// всплывающих окнах popups() (их даёт сеанс приложения, например окна процесса Revit), SelectionItem.Select (нет паттерна —
        /// клик мышью), проверить Value списка, если он есть. Возвращает подписи всех пунктов этого открытия.
        /// </summary>
        public static IReadOnlyList<string> Choose(Func<IEnumerable<AutomationElement>> popups, AutomationElement combo, string itemCaption, string description)
        {
            return UiChoice.Choose(popups, combo, itemCaption, description);
        }

        /// <summary>Подписи пунктов раскрывающегося списка (раскрыть, прочитать, свернуть с проверкой); popups — как у <see cref="Choose"/>.</summary>
        public static IReadOnlyList<string> ComboItems(Func<IEnumerable<AutomationElement>> popups, AutomationElement combo, string description)
        {
            return UiChoice.Items(popups, combo, description);
        }
    }
}
