using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Identifiers;
using FlaUI.Core.Patterns;

namespace RevitUITesting.Uia
{
    /// <summary>
    /// Поиск элементов с ожиданием, чтение свойств и дамп дерева UI Automation.
    /// Поиск — обход в ширину через FindAllChildren (<see cref="UiQuery"/>): штатный FindFirst(Descendants) в окнах WPF
    /// с тяжёлым содержимым зависает. «Не найдено» возвращается только после полного обхода; приложение не отвечало —
    /// <see cref="AppBusyException"/>. Safe* — только для заведомо необязательных значений (сообщения, дампы):
    /// они подменяют значением по умолчанию лишь временные сбои UI Automation и отсутствие свойства или паттерна.
    /// </summary>
    public static class AutomationElementExtensions
    {
        public const int DefaultMaxDepth = 60;

        private static readonly By RowSelector = By.Where(
            f => f.ControlType == ControlType.ListItem || f.ControlType == ControlType.DataItem || f.ControlType == ControlType.TreeItem,
            "строка списка (ListItem, DataItem, TreeItem)");

        // ------------------------------------------------------------------ поиск

        /// <summary>Потомок с AutomationId, ожидание <see cref="Wait.DefaultTimeout"/>; нет — <see cref="ElementNotFoundException"/> с перечнем id области.</summary>
        public static AutomationElement ById(this AutomationElement root, string automationId)
        {
            return root.Find(By.Id(automationId));
        }

        /// <summary>Потомок с именем (Name), ожидание <see cref="Wait.DefaultTimeout"/>.</summary>
        public static AutomationElement ByName(this AutomationElement root, string name)
        {
            return root.Find(By.Name(name));
        }

        /// <summary>
        /// Потомок по селектору с ожиданием (timeout null — <see cref="Wait.DefaultTimeout"/>).
        /// Отказ — <see cref="ElementNotFoundException"/> (при полных обходах); при неполных обходах — <see cref="AppBusyException"/>
        /// (приложение не отвечало) или <see cref="UiWaitTimeoutException"/> (узлы исчезали во время каждого обхода).
        /// </summary>
        public static AutomationElement Find(this AutomationElement root, By selector, TimeSpan? timeout = null, string scope = null)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }
            return UiQuery.WaitFind(() => root, selector, timeout ?? Wait.DefaultTimeout, scope ?? root.Describe());
        }

        /// <summary>
        /// Потомок по селектору без ожидания. null — только если обход прошёл полностью и элемента нет;
        /// полного обхода не было за <see cref="Wait.DefaultTimeout"/> — <see cref="AppBusyException"/> или <see cref="UiWaitTimeoutException"/>.
        /// </summary>
        public static AutomationElement TryFind(this AutomationElement root, By selector)
        {
            return UiQuery.TryFind(root, selector, Wait.DefaultTimeout);
        }

        /// <summary>Все потомки по селектору при полном обходе дерева (полного обхода не было за <see cref="Wait.DefaultTimeout"/> — отказ).</summary>
        public static IReadOnlyList<AutomationElement> FindAll(this AutomationElement root, By selector, int maxDepth = DefaultMaxDepth)
        {
            return UiQuery.FindAllComplete(root, selector, Wait.DefaultTimeout, maxDepth);
        }

        // ------------------------------------------------------------------ содержимое

        /// <summary>Непустые имена всех текстовых узлов поддерева (полный обход; сбой чтения — исключение, а не пропуск).</summary>
        public static IReadOnlyList<string> Texts(this AutomationElement element)
        {
            return element.FindAll(By.Type(ControlType.Text))
                .Select(e => UiQuery.Read(() => e.Properties.Name.ValueOrDefault ?? "", Wait.DefaultTimeout, "текст " + e.Describe()))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        /// <summary>Строки списка или дерева (ListItem, DataItem, TreeItem): прямые потомки, а если их нет — строки поддерева до глубины 6.</summary>
        public static IReadOnlyList<AutomationElement> Rows(this AutomationElement list)
        {
            IReadOnlyList<AutomationElement> direct = list.FindAll(RowSelector, 1);
            return direct.Count > 0 ? direct : list.FindAll(RowSelector, 6);
        }

        /// <summary>Строка списка, тексты или имя которой содержат все фрагменты (без учёта регистра); нет — null.</summary>
        public static AutomationElement TryRowContaining(this AutomationElement list, params string[] parts)
        {
            return list.Rows().FirstOrDefault(r => RowMatches(r, parts));
        }

        /// <summary>Строка списка, тексты которой содержат все фрагменты; ожидание <see cref="Wait.DefaultTimeout"/>.</summary>
        public static AutomationElement RowContaining(this AutomationElement list, params string[] parts)
        {
            return Wait.ForStrict(() => list.TryRowContaining(parts), Wait.DefaultTimeout, $"строка списка с текстом '{string.Join("', '", parts)}'", null);
        }

        private static bool RowMatches(AutomationElement row, string[] parts)
        {
            string name = UiQuery.Read(() => row.Properties.Name.ValueOrDefault ?? "", Wait.DefaultTimeout, "имя строки");
            string all = string.Join(" | ", row.Texts()) + " | " + name;
            return parts.All(p => all.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ------------------------------------------------------------------ необязательные значения

        /// <summary>Прямые потомки; пустой массив — элемент исчез или приложение не ответило (только для дампов и сообщений).</summary>
        public static AutomationElement[] SafeChildren(this AutomationElement element)
        {
            return SafeRead(() => element?.FindAllChildren(), new AutomationElement[0]);
        }

        public static string SafeName(this AutomationElement element)
        {
            return SafeRead(() => element?.Properties.Name.ValueOrDefault, "");
        }

        public static string SafeAutomationId(this AutomationElement element)
        {
            return SafeRead(() => element?.Properties.AutomationId.ValueOrDefault, "");
        }

        public static string SafeClassName(this AutomationElement element)
        {
            return SafeRead(() => element?.Properties.ClassName.ValueOrDefault, "");
        }

        public static ControlType SafeControlType(this AutomationElement element)
        {
            return SafeRead(() => element == null ? ControlType.Unknown : element.Properties.ControlType.ValueOrDefault, ControlType.Unknown);
        }

        public static bool SafeIsEnabled(this AutomationElement element)
        {
            return SafeRead(() => element != null && element.Properties.IsEnabled.ValueOrDefault, false);
        }

        public static bool SafeIsOffscreen(this AutomationElement element)
        {
            return SafeRead(() => element == null || element.Properties.IsOffscreen.ValueOrDefault, true);
        }

        /// <summary>Подсказка элемента: строковый ToolTip WPF попадает в свойство HelpText.</summary>
        public static string SafeHelpText(this AutomationElement element)
        {
            return SafeRead(() => element?.Properties.HelpText.ValueOrDefault, "");
        }

        /// <summary>Элемент выделен (SelectionItemPattern: вкладка, строка списка).</summary>
        public static bool SafeIsSelected(this AutomationElement element)
        {
            return SafeRead(() =>
            {
                ISelectionItemPattern pattern = element?.Patterns.SelectionItem.PatternOrDefault;
                return pattern != null && pattern.IsSelected.ValueOrDefault;
            }, false);
        }

        /// <summary>Элемент всё ещё существует в дереве UI Automation (окно не закрыто, строка не удалена).</summary>
        public static bool SafeIsAvailable(this AutomationElement element)
        {
            return SafeRead(() => element != null && element.IsAvailable, false);
        }

        /// <summary>ItemStatus элемента (WPF-приложения пишут туда состояния); "" — нет или не прочитано.</summary>
        public static string SafeItemStatus(this AutomationElement element)
        {
            return SafeRead(() => element?.Properties.ItemStatus.ValueOrDefault, "");
        }

        /// <summary>ItemType элемента; "" — нет.</summary>
        public static string SafeItemType(this AutomationElement element)
        {
            return SafeRead(() => element?.Properties.ItemType.ValueOrDefault, "");
        }

        /// <summary>Значение ValuePattern; "" — паттерна нет или не прочитано.</summary>
        public static string SafeValue(this AutomationElement element)
        {
            return SafeRead(() => element?.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault, "");
        }

        /// <summary>Состояние TogglePattern; null — паттерна нет или не прочитано.</summary>
        public static ToggleState? SafeToggleState(this AutomationElement element)
        {
            return SafeRead<ToggleState?>(() =>
            {
                ITogglePattern toggle = element?.Patterns.Toggle.PatternOrDefault;
                return toggle == null ? (ToggleState?)null : toggle.ToggleState.ValueOrDefault;
            }, null);
        }

        /// <summary>Элемент имеет фокус клавиатуры.</summary>
        public static bool SafeHasKeyboardFocus(this AutomationElement element)
        {
            return SafeRead(() => element != null && element.Properties.HasKeyboardFocus.ValueOrDefault, false);
        }

        // ------------------------------------------------------------------ дамп

        /// <summary>Текстовый дамп поддерева — для сообщений об ошибках и артефактов: [тип] name="…" id="…" value="…" disabled offscreen.</summary>
        public static string DumpTree(this AutomationElement root, int maxDepth = DefaultMaxDepth)
        {
            StringBuilder sb = new StringBuilder();
            Dump(root, sb, 0, maxDepth);
            return sb.ToString();
        }

        /// <summary>
        /// Дамп поддерева. details: класс, прямоугольник, HelpText, ItemStatus, ItemType, Value и поддерживаемые паттерны.
        /// SelectionPattern.Selection не читается: у некоторых WPF-кнопок (SilentInvoker) это чтение ВЫПОЛНЯЕТ команду кнопки.
        /// </summary>
        public static string DumpTree(this AutomationElement root, int maxDepth, bool details)
        {
            if (!details)
            {
                return root.DumpTree(maxDepth);
            }
            StringBuilder sb = new StringBuilder();
            DumpDetailed(root, sb, 0, maxDepth);
            return sb.ToString();
        }

        /// <summary>Описание элемента одной строкой для сообщений: [тип] id="…" name="…".</summary>
        public static string Describe(this AutomationElement element)
        {
            if (element == null)
            {
                return "(нет элемента)";
            }
            return $"[{element.SafeControlType()}] id=\"{element.SafeAutomationId()}\" name=\"{Trunc(element.SafeName(), 60)}\"";
        }

        private static void Dump(AutomationElement element, StringBuilder sb, int depth, int maxDepth)
        {
            if (element == null || depth > maxDepth)
            {
                return;
            }
            string value = element.SafeValue();
            sb.AppendLine($"{new string(' ', depth * 2)}[{element.SafeControlType()}] name=\"{Trunc(element.SafeName(), 90)}\" id=\"{element.SafeAutomationId()}\""
                          + (value.Length > 0 ? " value=\"" + Trunc(value, 60) + "\"" : "")
                          + (element.SafeIsEnabled() ? "" : " disabled") + (element.SafeIsOffscreen() ? " offscreen" : ""));
            foreach (AutomationElement child in element.SafeChildren())
            {
                Dump(child, sb, depth + 1, maxDepth);
            }
        }

        private static void DumpDetailed(AutomationElement element, StringBuilder sb, int depth, int maxDepth)
        {
            if (element == null || depth > maxDepth)
            {
                return;
            }
            sb.Append(new string(' ', depth * 2));
            sb.Append('[').Append(element.SafeControlType()).Append(']');
            Append(sb, "cls", element.SafeClassName());
            Append(sb, "id", element.SafeAutomationId());
            Append(sb, "name", Trunc(element.SafeName(), 90));
            Append(sb, "help", Trunc(element.SafeHelpText(), 90));
            Append(sb, "status", Trunc(element.SafeItemStatus(), 90));
            Append(sb, "itype", Trunc(element.SafeItemType(), 90));
            Append(sb, "value", Trunc(element.SafeValue(), 60));
            if (!element.SafeIsEnabled())
            {
                sb.Append(" disabled");
            }
            if (element.SafeIsOffscreen())
            {
                sb.Append(" offscreen");
            }
            sb.Append(' ').Append(SafeRead(() => element.BoundingRectangle.ToString(), ""));
            string patterns = SafeRead(() => string.Join(",", element.GetSupportedPatterns().Select(PatternName)), "");
            if (patterns.Length > 0)
            {
                sb.Append(" pat=[").Append(patterns).Append(']');
            }
            sb.AppendLine();
            foreach (AutomationElement child in element.SafeChildren())
            {
                DumpDetailed(child, sb, depth + 1, maxDepth);
            }
        }

        private static string PatternName(PatternId id)
        {
            string name = id.Name ?? "";
            return name.EndsWith("Pattern", StringComparison.Ordinal) ? name.Substring(0, name.Length - "Pattern".Length) : name;
        }

        private static void Append(StringBuilder sb, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                sb.Append(' ').Append(key).Append("=\"").Append(value).Append('"');
            }
        }

        /// <summary>Чтение необязательного значения: временный сбой UI Automation или отсутствие свойства/паттерна — fallback, прочее пробрасывается.</summary>
        private static T SafeRead<T>(Func<T> read, T fallback)
        {
            try
            {
                T value = read();
                return value == null ? fallback : value;
            }
            catch (Exception ex) when (UiFault.IsTransient(ex) || ex is FlaUI.Core.Exceptions.NotSupportedException)
            {
                return fallback;
            }
        }

        private static string Trunc(string s, int len)
        {
            if (s == null)
            {
                return "";
            }
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= len ? s : s.Substring(0, len) + "…";
        }
    }
}
