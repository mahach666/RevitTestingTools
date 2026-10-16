using System;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using RevitUITesting.Uia;

namespace RevitUITesting.Revit
{
    /// <summary>Сопоставление элементов ленты AdWindows с вкладкой и кнопкой (параметры — <see cref="RevitIds.Ribbon"/>).</summary>
    internal static class RibbonMatch
    {
        /// <summary>AutomationId равен внутреннему имени PushButtonData или оканчивается на «%имя» (Id кнопок API в AdWindows).</summary>
        internal static bool IsButtonId(string automationId, string button)
        {
            if (string.IsNullOrEmpty(automationId) || string.IsNullOrEmpty(button))
            {
                return false;
            }
            return automationId == button || automationId.EndsWith(RevitIds.Ribbon.ApiIdSeparator + button, StringComparison.Ordinal);
        }

        /// <summary>Подпись после нормализации переносов и пробелов равна искомой (с учётом регистра).</summary>
        internal static bool IsCaption(string name, string button)
        {
            string wanted = CaptionText.Normalize(button);
            return wanted.Length > 0 && CaptionText.Normalize(name) == wanted;
        }

        internal static bool IsButton(ElementFacts facts, string button)
        {
            return RevitIds.Ribbon.ButtonTypes.Contains(facts.ControlType) && (IsButtonId(facts.AutomationId, button) || IsCaption(facts.Name, button));
        }

        internal static List<AutomationElement> FindButtons(AutomationElement root, string button)
        {
            return UiQuery.FindAllComplete(root, By.Where(f => IsButton(f, button), $"кнопка ленты «{CaptionText.Normalize(button)}»"), Wait.DefaultTimeout, RevitIds.Ribbon.SearchDepth);
        }

        /// <summary>Кнопки на экране (IsOffscreen = false) по имени или подписи во всех областях (содержимое вкладки, всплывающие окна).</summary>
        internal static List<AutomationElement> FindShownButtons(IEnumerable<AutomationElement> scopes, string button)
        {
            return scopes.SelectMany(scope => FindButtons(scope, button)).Where(IsShown).ToList();
        }

        /// <summary>
        /// Кнопки по имени или подписи в окнах раскрытых свёрнутых панелей — без отбора по экрану: окно панели видимо (EnumWindows), но UI Automation
        /// и его, и кнопки в нём держит offscreen с пустым прямоугольником (Evidence/ribbon-collapsed-2022.txt, прогон 5: 5 с после раскрытия).
        /// </summary>
        internal static List<AutomationElement> FindPanelWindowButtons(IEnumerable<AutomationElement> panelWindows, string button)
        {
            return panelWindows.SelectMany(window => FindButtons(window, button)).ToList();
        }

        /// <summary>Одно совпадение или null; несколько — <see cref="AmbiguousElementException"/> (нажатие не того элемента хуже отказа).</summary>
        internal static AutomationElement Single(List<AutomationElement> matches, string what)
        {
            if (matches.Count > 1)
            {
                throw new AmbiguousElementException(what, matches.Select(m => m.Describe()).ToList());
            }
            return matches.FirstOrDefault();
        }

        /// <summary>
        /// Вкладки ленты по подписи или внутреннему имени (AutomationId) вкладки (name null — все кнопки вкладок с непустой подписью). Вкладка — прямой потомок корня ленты типа <see cref="RevitIds.Ribbon.TabType"/>,
        /// у которого рядом (тоже прямой потомок корня) есть содержимое «AutomationId вкладки + <see cref="RevitIds.Ribbon.TabContentSuffix"/>»;
        /// подпись одна не отличает вкладку от кнопки (кнопка «Modify» панели Select). Скрытые вкладки (offscreen вместе с содержимым, например
        /// вкладки редактора семейств) пропускаются; активная вкладка, чьей кнопки нет на полосе вкладок (узкое окно), находится по внутреннему
        /// имени — тогда <see cref="RibbonTab.Element"/> = null.
        /// Revit 2025 и новее (полоса <see cref="RevitIds.Ribbon.TabStripId"/>): вкладка — Button-потомок полосы, содержимое ищется глубже
        /// (<see cref="RevitIds.Ribbon.TabContentDepth"/>) и есть только у активной вкладки, у остальных <see cref="RibbonTab.Content"/> = null.
        /// Сбой чтения (занят, исчез) пробрасывается — его обрабатывает ожидание вызывающего.
        /// </summary>
        internal static List<RibbonTab> FindTabs(AutomationElement root, string name)
        {
            By candidates = By.Where(f => f.ControlType == RevitIds.Ribbon.TabType || IsContentId(f.AutomationId) || f.AutomationId == RevitIds.Ribbon.TabStripId, "вкладки ленты и их содержимое");
            List<AutomationElement> children = UiQuery.FindAllComplete(root, candidates, Wait.DefaultTimeout, 1);
            Dictionary<string, KeyValuePair<AutomationElement, bool>> contents = new Dictionary<string, KeyValuePair<AutomationElement, bool>>(StringComparer.Ordinal);
            List<KeyValuePair<AutomationElement, string>> buttons = new List<KeyValuePair<AutomationElement, string>>();
            AutomationElement strip = null;
            foreach (AutomationElement child in children)
            {
                string id = ReadId(child);
                if (IsContentId(id))
                {
                    AddContent(contents, child, id);
                }
                else if (id == RevitIds.Ribbon.TabStripId)
                {
                    strip = child;
                }
                else if (id.Length > 0)
                {
                    buttons.Add(new KeyValuePair<AutomationElement, string>(child, id));
                }
            }
            if (strip != null)
            {
                // кнопки у корня (меню приложения, сворачивание ленты) — не вкладки: вкладки только на полосе
                buttons.Clear();
                foreach (AutomationElement button in UiQuery.FindAllComplete(strip, By.Type(RevitIds.Ribbon.TabType), Wait.DefaultTimeout, 1))
                {
                    string id = ReadId(button);
                    if (id.Length > 0)
                    {
                        buttons.Add(new KeyValuePair<AutomationElement, string>(button, id));
                    }
                }
                By content = By.Where(f => IsContentId(f.AutomationId), "содержимое вкладок ленты");
                foreach (AutomationElement element in UiQuery.FindAllComplete(root, content, Wait.DefaultTimeout, RevitIds.Ribbon.TabContentDepth))
                {
                    AddContent(contents, element, ReadId(element));
                }
            }
            List<RibbonTab> tabs = new List<RibbonTab>();
            foreach (KeyValuePair<AutomationElement, string> button in buttons)
            {
                if (!contents.TryGetValue(button.Value, out KeyValuePair<AutomationElement, bool> content))
                {
                    if (strip == null)
                    {
                        continue;
                    }
                    content = new KeyValuePair<AutomationElement, bool>(null, false);
                }
                AutomationElement element = button.Key;
                string caption = UiQuery.Read(() => element.Properties.Name.ValueOrDefault ?? "", Wait.DefaultTimeout, "подпись вкладки ленты «" + button.Value + "»");
                if (name == null ? caption.Length == 0 : !IsCaption(caption, name) && button.Value != name)
                {
                    continue;
                }
                RibbonTab tab = new RibbonTab(element, button.Value, caption, IsShown(element), content.Key, content.Value);
                if (tab.IsShown || tab.IsActive)
                {
                    tabs.Add(tab);
                }
            }
            // кнопка активной вкладки, не поместившейся на полосу вкладок, из дерева UIA исчезает, а её содержимое остаётся на экране:
            // такая вкладка находится только по внутреннему имени (AutomationId) — подписи без кнопки нет
            if (name != null && contents.TryGetValue(name, out KeyValuePair<AutomationElement, bool> orphan) && orphan.Value && !buttons.Any(b => b.Value == name))
            {
                tabs.Add(new RibbonTab(null, name, name, false, orphan.Key, true));
            }
            return tabs;
        }

        private static string ReadId(AutomationElement element)
        {
            return UiQuery.Read(() => element.Properties.AutomationId.ValueOrDefault ?? "", Wait.DefaultTimeout, "AutomationId элемента ленты");
        }

        private static void AddContent(Dictionary<string, KeyValuePair<AutomationElement, bool>> contents, AutomationElement content, string id)
        {
            string tabId = id.Substring(0, id.Length - RevitIds.Ribbon.TabContentSuffix.Length);
            if (!contents.ContainsKey(tabId))
            {
                contents.Add(tabId, new KeyValuePair<AutomationElement, bool>(content, IsShown(content)));
            }
        }

        /// <summary>AutomationId содержимого вкладки («&lt;id вкладки&gt;_PanelBarScrollViewer»).</summary>
        internal static bool IsContentId(string automationId)
        {
            return automationId != null && automationId.Length > RevitIds.Ribbon.TabContentSuffix.Length && automationId.EndsWith(RevitIds.Ribbon.TabContentSuffix, StringComparison.Ordinal);
        }

        /// <summary>Окно — всплывающее окно WPF: ClassName в UI Automation = <see cref="RevitIds.Dialogs.PopupClass"/> (у класса Win32 окна его нет).</summary>
        internal static bool IsPopup(AutomationElement window)
        {
            return UiQuery.Read(() => window.Properties.ClassName.ValueOrDefault ?? "", Wait.DefaultTimeout, "ClassName окна") == RevitIds.Dialogs.PopupClass;
        }

        /// <summary>Элемент на экране (IsOffscreen = false); строгое чтение: «занят» повторяется, «исчез» пробрасывается.</summary>
        internal static bool IsShown(AutomationElement element)
        {
            return !UiQuery.Read(() => element.Properties.IsOffscreen.ValueOrDefault, Wait.DefaultTimeout, "IsOffscreen элемента ленты");
        }

        /// <summary>
        /// Следующий шаг сворачивания раскрытой свёрнутой панели. Раскрыта — значит открыто её окно (появившееся при этом раскрытии), а не Toggle On:
        /// у Project Location окно было открыто при Toggle Off (Evidence/ribbon-collapsed-2022.txt, прогон 6). Каждое действие — не больше одного раза:
        /// окно открыто — один Toggle; осталось открытым — один Esc; осталось и после Esc — отказ.
        /// </summary>
        internal static PanelCollapseStep NextCollapseStep(bool windowOpen, bool toggled, bool escaped)
        {
            if (!windowOpen)
            {
                return PanelCollapseStep.Done;
            }
            if (escaped)
            {
                return PanelCollapseStep.Fail;
            }
            return toggled ? PanelCollapseStep.Escape : PanelCollapseStep.Toggle;
        }
    }

    /// <summary>Шаг сворачивания раскрытой свёрнутой панели (<see cref="RibbonMatch.NextCollapseStep"/>).</summary>
    internal enum PanelCollapseStep
    {
        /// <summary>Окна панели нет — панель свёрнута.</summary>
        Done,

        /// <summary>Нажать Toggle панели (один раз).</summary>
        Toggle,

        /// <summary>Нажать Esc в переднее окно Revit (один раз).</summary>
        Escape,

        /// <summary>Окно не закрылось ни Toggle, ни Esc — ошибка с описанием, без новых действий.</summary>
        Fail,
    }

    /// <summary>Вкладка ленты: кнопка на полосе вкладок и её содержимое (панели).</summary>
    internal sealed class RibbonTab
    {
        internal RibbonTab(AutomationElement element, string automationId, string name, bool isShown, AutomationElement content, bool isActive)
        {
            Element = element;
            AutomationId = automationId;
            Name = name;
            IsShown = isShown;
            Content = content;
            IsActive = isActive;
        }

        /// <summary>Кнопка вкладки (нажатие активирует вкладку); null — кнопки нет в дереве UIA (активная вкладка не поместилась на полосу вкладок).</summary>
        internal AutomationElement Element { get; }

        /// <summary>Внутреннее имя вкладки («Manage», «thePrecastTab», «TestHost»).</summary>
        internal string AutomationId { get; }

        /// <summary>Подпись вкладки (UIA Name).</summary>
        internal string Name { get; }

        /// <summary>Кнопка вкладки на экране.</summary>
        internal bool IsShown { get; }

        /// <summary>Содержимое вкладки — панели и кнопки («&lt;id&gt;_PanelBarScrollViewer»); null — его нет в дереве (неактивная вкладка в Revit 2025 и новее).</summary>
        internal AutomationElement Content { get; }

        /// <summary>Вкладка активна: её содержимое на экране (у неактивных вкладок оно offscreen с пустым прямоугольником).</summary>
        internal bool IsActive { get; }

        public override string ToString()
        {
            return $"вкладка «{CaptionText.Normalize(Name)}» (id «{AutomationId}»{(IsActive ? ", активна" : "")}{(IsShown ? "" : ", не на полосе вкладок")})";
        }
    }
}
