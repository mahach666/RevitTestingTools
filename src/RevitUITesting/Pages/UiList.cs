using System;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using RevitUITesting.Uia;

namespace RevitUITesting.Pages
{
    /// <summary>Список или дерево (строки ListItem/DataItem/TreeItem); корень переразрешается на каждом обращении.</summary>
    public sealed class UiList
    {
        private readonly Func<AutomationElement> _root;
        private readonly string _description;

        public UiList(Func<AutomationElement> root, string description)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _description = description;
        }

        public AutomationElement Root => _root() ?? throw new ElementNotFoundException(_description, "список", TimeSpan.Zero, TimeSpan.Zero, null, null);

        public IReadOnlyList<AutomationElement> Rows()
        {
            return Root.Rows();
        }

        /// <summary>Текст строки: тексты через « | », иначе Name.</summary>
        public IReadOnlyList<string> RowTexts()
        {
            return Rows().Select(r =>
            {
                IReadOnlyList<string> texts = r.Texts();
                return texts.Count > 0 ? string.Join(" | ", texts) : r.SafeName();
            }).ToList();
        }

        /// <summary>Дождаться строки, тексты которой содержат все фрагменты.</summary>
        public AutomationElement Row(params string[] parts)
        {
            return Wait.ForStrict(() => _root()?.TryRowContaining(parts), Wait.DefaultTimeout, $"строка «{string.Join("», «", parts)}» в {_description}", () => string.Join("; ", RowTexts()));
        }

        /// <summary>Выделить строку (SelectionItem.Select).</summary>
        public void Select(params string[] parts)
        {
            UiActions.Select(Row(parts), $"строка «{string.Join("», «", parts)}» в {_description}");
        }

        public override string ToString()
        {
            return _description;
        }
    }
}
