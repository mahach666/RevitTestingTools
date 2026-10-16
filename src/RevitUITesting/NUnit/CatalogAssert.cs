using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FlaUI.Core.AutomationElements;
using NUnit.Framework;
using RevitUITesting.Uia;

namespace RevitUITesting.NUnit
{
    /// <summary>Итог сверки раздела каталога с экраном.</summary>
    public sealed class CatalogReport
    {
        internal CatalogReport(string section, IReadOnlyList<string> found, IReadOnlyList<string> missing, IReadOnlyList<string> skipped, IReadOnlyList<string> unknown)
        {
            Section = section;
            Found = found;
            Missing = missing;
            Skipped = skipped;
            Unknown = unknown;
        }

        public string Section { get; }

        /// <summary>Константы раздела, найденные на экране («Имя = id»).</summary>
        public IReadOnlyList<string> Found { get; }

        /// <summary>Безусловные константы, которых на экране нет — каталог устарел или экран не тот.</summary>
        public IReadOnlyList<string> Missing { get; }

        /// <summary>Условные константы ([ShownWhen]), которых сейчас нет — это не ошибка.</summary>
        public IReadOnlyList<string> Skipped { get; }

        /// <summary>AutomationId на экране, которых нет в разделе (кандидаты в каталог после обновления приложения).</summary>
        public IReadOnlyList<string> Unknown { get; }

        public override string ToString()
        {
            return $"{Section}: найдено {Found.Count}, нет {Missing.Count}, условных нет {Skipped.Count}, неизвестных {Unknown.Count}" +
                   (Missing.Count > 0 ? Environment.NewLine + "  нет: " + string.Join(", ", Missing) : "") +
                   (Unknown.Count > 0 ? Environment.NewLine + "  неизвестные: " + string.Join(", ", Unknown) : "");
        }
    }

    /// <summary>
    /// Сверка раздела каталога идентификаторов (статический класс с public const string, например каталог id окна плагина
    /// или каталог Revit) с деревом UI Automation области. Нужна при обновлении Revit и для проверки своих id плагина.
    /// </summary>
    public static class CatalogAssert
    {
        /// <summary>Сверить раздел с областью (без провала теста).</summary>
        public static CatalogReport Check(AutomationElement scope, Type section, params string[] ignore)
        {
            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }
            HashSet<string> ignored = new HashSet<string>(ignore ?? new string[0], StringComparer.Ordinal);
            // сама область тоже на экране (раздел часто сверяют с его же контейнером: панель, меню). Строгое чтение: исчезнувшая область —
            // исключение, а не «все константы отсутствуют»
            string scopeId = UiQuery.Read(() => scope.Properties.AutomationId.ValueOrDefault ?? "", Wait.DefaultTimeout, "AutomationId области сверки");
            // только полный обход: пропущенные из-за занятости приложения ветки дали бы ложное «на экране нет»
            HashSet<string> onScreen = new HashSet<string>(UiQuery.IdsInComplete(scope, Wait.DefaultTimeout, 40), StringComparer.Ordinal);
            if (scopeId.Length > 0)
            {
                onScreen.Add(scopeId);
            }
            List<string> found = new List<string>();
            List<string> missing = new List<string>();
            List<string> skipped = new List<string>();
            HashSet<string> catalogued = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldInfo field in section.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string)))
            {
                string id = (string)field.GetRawConstantValue();
                catalogued.Add(id);
                if (ignored.Contains(field.Name) || ignored.Contains(id))
                {
                    continue;
                }
                string entry = field.Name + " = " + id;
                if (onScreen.Contains(id))
                {
                    found.Add(entry);
                }
                else if (field.GetCustomAttribute<ShownWhenAttribute>() != null)
                {
                    skipped.Add(entry);
                }
                else
                {
                    missing.Add(entry);
                }
            }
            List<string> unknown = onScreen.Where(id => !catalogued.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
            return new CatalogReport(section.FullName, found, missing, skipped, unknown);
        }

        /// <summary>Все безусловные константы раздела есть на экране; иначе — провал с перечнем. Отчёт пишется в вывод теста.</summary>
        public static CatalogReport AllPresent(AutomationElement scope, Type section, params string[] ignore)
        {
            CatalogReport report = Check(scope, section, ignore);
            TestContext.Out.WriteLine(report.ToString());
            if (report.Missing.Count > 0)
            {
                Assert.Fail($"Раздел каталога {report.Section}: на экране нет {report.Missing.Count}: {string.Join(", ", report.Missing)}");
            }
            return report;
        }
    }
}
