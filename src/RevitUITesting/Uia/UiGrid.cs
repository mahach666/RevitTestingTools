using System;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;

namespace RevitUITesting.Uia
{
    /// <summary>Колонка таблицы: FieldName (Name заголовка) и подпись (текст заголовка).</summary>
    public sealed class UiGridColumn
    {
        internal UiGridColumn(string fieldName, string caption)
        {
            FieldName = fieldName;
            Caption = caption;
        }

        public string FieldName { get; }

        public string Caption { get; }

        public override string ToString()
        {
            return $"{FieldName} «{Caption}»";
        }
    }

    /// <summary>
    /// Таблица с GridPattern. Устройство проверено на DevExpress GridControl 20.1 (вне Revit; в окнах Revit не проверялось):
    /// заголовки — Header → HeaderItem (Name = FieldName колонки, подпись — в дочернем Text; Invoke по заголовку сортирует — не вызывается);
    /// строки — DataItem; ячейка — элемент строки с AutomationId = FieldName, значение — ValuePattern.Value (показанный текст).
    /// SetValue по ячейке не вызывается: у DevExpress IsReadOnly = False, запись изменила бы данные.
    /// </summary>
    public sealed class UiGrid
    {
        private readonly Func<AutomationElement> _root;
        private readonly string _description;

        public UiGrid(Func<AutomationElement> root, string description)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _description = description;
        }

        public AutomationElement Root => _root() ?? throw new ElementNotFoundException(_description, "таблица", TimeSpan.Zero, TimeSpan.Zero, null, null);

        /// <summary>Число строк (GridPattern.RowCount).</summary>
        public int RowCount => UiQuery.Read(() => GridPattern(Root).RowCount.Value, Wait.DefaultTimeout, "число строк " + _description);

        /// <summary>
        /// Колонки по заголовкам таблицы. Заголовки и подписи читаются полным обходом и строгим чтением: приложение занято —
        /// <see cref="AppBusyException"/>, заголовок исчезает во время каждой попытки — <see cref="UiWaitTimeoutException"/>;
        /// неполного списка колонок не бывает.
        /// </summary>
        public IReadOnlyList<UiGridColumn> Columns()
        {
            AutomationElement root = Root;
            AutomationElement header = UiQuery.TryFind(root, By.Type(ControlType.Header), Wait.DefaultTimeout, 4)
                ?? throw new InvalidOperationException($"{_description}: нет заголовка (Header).");
            return Wait.ForStrict(() => ReadColumns(header), Wait.DefaultTimeout, "колонки " + _description, null);
        }

        private static IReadOnlyList<UiGridColumn> ReadColumns(AutomationElement header)
        {
            List<UiGridColumn> columns = new List<UiGridColumn>();
            foreach (AutomationElement item in UiQuery.FindAllComplete(header, By.Type(ControlType.HeaderItem), Wait.DefaultTimeout, 1))
            {
                string field = UiQuery.Read(() => item.Properties.Name.ValueOrDefault ?? "", Wait.DefaultTimeout, "имя заголовка колонки");
                string caption = UiQuery.FindAllComplete(item, By.Type(ControlType.Text), Wait.DefaultTimeout, 1)
                    .Select(text => UiQuery.Read(() => text.Properties.Name.ValueOrDefault ?? "", Wait.DefaultTimeout, "подпись колонки " + field))
                    .FirstOrDefault(t => t.Length > 0) ?? "";
                columns.Add(new UiGridColumn(field, caption));
            }
            return columns;
        }

        /// <summary>Показанный текст ячейки. column — FieldName или подпись колонки.</summary>
        public string Cell(int row, string column)
        {
            AutomationElement cell = CellElement(row, column);
            return UiQuery.Read(() => cell.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault ?? "", Wait.DefaultTimeout, $"ячейка [{row}, {column}]");
        }

        /// <summary>Значения колонки во всех строках (ровно RowCount).</summary>
        public IReadOnlyList<string> Column(string column)
        {
            int count = RowCount;
            return Enumerable.Range(0, count).Select(r => Cell(r, column)).ToList();
        }

        /// <summary>Строки как словари «FieldName → текст» по указанным колонкам.</summary>
        public IReadOnlyList<IReadOnlyDictionary<string, string>> ReadAll(params string[] columns)
        {
            string[] fields = columns.Select(FieldName).ToArray();
            int count = RowCount;
            List<IReadOnlyDictionary<string, string>> rows = new List<IReadOnlyDictionary<string, string>>();
            for (int r = 0; r < count; r++)
            {
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string field in fields)
                {
                    values[field] = Cell(r, field);
                }
                rows.Add(values);
            }
            return rows;
        }

        /// <summary>Номер строки со значением в колонке; -1 — нет; больше одной — неоднозначность.</summary>
        public int FindRow(string column, string value)
        {
            List<int> matches = new List<int>();
            IReadOnlyList<string> values = Column(column);
            for (int r = 0; r < values.Count; r++)
            {
                if (values[r] == value)
                {
                    matches.Add(r);
                }
            }
            if (matches.Count > 1)
            {
                throw new AmbiguousElementException($"строка с «{value}» в колонке {column} ({_description})", matches.Select(m => "строка " + m).ToList());
            }
            return matches.Count == 0 ? -1 : matches[0];
        }

        /// <summary>Элемент ячейки: в строке (DataItem) элемент с AutomationId = FieldName.</summary>
        public AutomationElement CellElement(int row, string column)
        {
            string field = FieldName(column);
            return CellOf(RowElement(row), row, field);
        }

        /// <summary>Ячейка строки по полному обходу её детей: занят — <see cref="AppBusyException"/>; «нет ячейки» — только если обход был полным.</summary>
        internal AutomationElement CellOf(AutomationElement rowElement, int row, string field)
        {
            return Wait.ForStrict(() =>
            {
                AutomationElement cell = UiQuery.FindAllComplete(rowElement, By.Id(field), Wait.DefaultTimeout, 1).FirstOrDefault();
                if (cell == null)
                {
                    throw new InvalidOperationException($"{_description}: в строке {row} нет ячейки {field} (колонка скрыта?). Ячейки: {string.Join(", ", UiQuery.IdsInComplete(rowElement, Wait.DefaultTimeout, 1))}");
                }
                return cell;
            }, Wait.DefaultTimeout, $"ячейка {field} строки {row} ({_description})", null);
        }

        /// <summary>
        /// Строка (DataItem) — родитель ячейки GridPattern.GetItem(row, 0). Строка вне дерева UI Automation (вертикальная виртуализация не проверена) —
        /// исключение, а не пропуск.
        /// </summary>
        public AutomationElement RowElement(int row)
        {
            AutomationElement root = Root;
            int count = RowCount;
            if (row < 0 || row >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(row), $"{_description}: строк {count}, запрошена {row}.");
            }
            AutomationElement cell = UiQuery.Read(() => GridPattern(root).GetItem(row, 0), Wait.DefaultTimeout, $"ячейка [{row}, 0]")
                ?? throw new InvalidOperationException($"{_description}: GridPattern.GetItem({row}, 0) не вернул ячейку (строка не материализована в дереве UI Automation).");
            AutomationElement parent = UiQuery.Read(() => cell.Parent, Wait.DefaultTimeout, "строка ячейки");
            if (parent == null || parent.SafeControlType() != ControlType.DataItem)
            {
                throw new InvalidOperationException($"{_description}: родитель ячейки [{row}, 0] не DataItem ({parent.Describe()}).");
            }
            return parent;
        }

        /// <summary>Выделить строку (SelectionItem.Select) и передать фокус таблице.</summary>
        public void SelectRow(int row)
        {
            AutomationElement rowElement = RowElement(row);
            ISelectionItemPattern selection = UiQuery.Read(() => rowElement.Patterns.SelectionItem.PatternOrDefault, Wait.DefaultTimeout, "SelectionItem строки")
                ?? throw new InvalidOperationException($"{_description}: строка {row} не поддерживает выделение.");
            UiAct.RunOnce(rowElement, $"выделить строку {row} ({_description})", selection.Select, "Select");
            AutomationElement root = Root;
            UiQuery.Read(() =>
            {
                root.Focus();
                return true;
            }, Wait.DefaultTimeout, "фокус на таблицу");
            Wait.Strict(() => rowElement.SafeIsSelected(), Wait.DefaultTimeout, $"строка {row} выделена ({_description})", null);
        }

        public override string ToString()
        {
            return _description;
        }

        private string FieldName(string column)
        {
            IReadOnlyList<UiGridColumn> columns = Columns();
            List<UiGridColumn> byField = columns.Where(c => c.FieldName == column).ToList();
            List<UiGridColumn> byCaption = columns.Where(c => c.Caption == column).ToList();
            if (byField.Count == 1 && byCaption.Count > 0 && byCaption.Any(c => c.FieldName != column))
            {
                throw new AmbiguousElementException($"колонка «{column}» ({_description})", byField.Concat(byCaption).Select(c => c.ToString()).ToList());
            }
            if (byField.Count == 1)
            {
                return column;
            }
            if (byCaption.Count == 1)
            {
                return byCaption[0].FieldName;
            }
            if (byCaption.Count > 1)
            {
                throw new AmbiguousElementException($"колонка «{column}» ({_description})", byCaption.Select(c => c.ToString()).ToList());
            }
            throw new InvalidOperationException($"{_description}: нет колонки «{column}». Колонки: {string.Join(", ", columns)}");
        }

        private IGridPattern GridPattern(AutomationElement root)
        {
            return root.Patterns.Grid.PatternOrDefault ?? throw new InvalidOperationException($"{_description}: элемент не поддерживает GridPattern ({root.Describe()}).");
        }
    }
}
