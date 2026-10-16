using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace RevitUITesting.Uia
{
    /// <summary>
    /// Свойства узла дерева UI Automation, прочитанные не более одного раза и только по требованию.
    /// Сбой чтения (приложение занято, элемент исчез) пробрасывается — его классифицирует поиск.
    /// </summary>
    public sealed class ElementFacts
    {
        private string _automationId;
        private string _name;
        private string _className;
        private ControlType? _controlType;

        internal ElementFacts(AutomationElement element)
        {
            Element = element;
        }

        public AutomationElement Element { get; }

        public string AutomationId => _automationId ?? (_automationId = Element.Properties.AutomationId.ValueOrDefault ?? "");

        public string Name => _name ?? (_name = Element.Properties.Name.ValueOrDefault ?? "");

        public string ClassName => _className ?? (_className = Element.Properties.ClassName.ValueOrDefault ?? "");

        public ControlType ControlType => (_controlType ?? (_controlType = Element.Properties.ControlType.ValueOrDefault)).Value;

        public string HelpText => Element.Properties.HelpText.ValueOrDefault ?? "";

        public string ItemStatus => Element.Properties.ItemStatus.ValueOrDefault ?? "";
    }

    /// <summary>
    /// Селектор элемента: сравнение точное (Ordinal). Комбинируется через <see cref="And"/>.
    /// <c>By.Id("miDelete")</c>, <c>By.Name("Создание").And(By.Type(ControlType.Window))</c>.
    /// </summary>
    public sealed class By
    {
        private readonly Func<ElementFacts, bool> _match;
        private readonly string _text;

        private By(Func<ElementFacts, bool> match, string text)
        {
            _match = match;
            _text = text;
        }

        public static By Id(string automationId)
        {
            Require(automationId, nameof(automationId));
            return new By(f => f.AutomationId == automationId, "id=\"" + automationId + "\"");
        }

        public static By IdPrefix(string prefix)
        {
            Require(prefix, nameof(prefix));
            return new By(f => f.AutomationId.StartsWith(prefix, StringComparison.Ordinal), "id^=\"" + prefix + "\"");
        }

        public static By Name(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            return new By(f => f.Name == name, "name=\"" + name + "\"");
        }

        public static By NameStartsWith(string prefix)
        {
            Require(prefix, nameof(prefix));
            return new By(f => f.Name.StartsWith(prefix, StringComparison.Ordinal), "name^=\"" + prefix + "\"");
        }

        public static By ClassName(string className)
        {
            Require(className, nameof(className));
            return new By(f => f.ClassName == className, "class=\"" + className + "\"");
        }

        public static By Type(ControlType type)
        {
            return new By(f => f.ControlType == type, "type=" + type);
        }

        /// <summary>Произвольное условие; description попадает в сообщения об ошибках.</summary>
        public static By Where(Func<ElementFacts, bool> predicate, string description)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            return new By(predicate, string.IsNullOrEmpty(description) ? "условие" : description);
        }

        public By And(By other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            return new By(f => _match(f) && other._match(f), _text + " и " + other._text);
        }

        public bool Matches(ElementFacts facts)
        {
            return _match(facts);
        }

        public override string ToString()
        {
            return _text;
        }

        private static void Require(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Пустое значение селектора", name);
            }
        }
    }
}
