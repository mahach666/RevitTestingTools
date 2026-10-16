using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RevitTestHost.Core.Execution
{
    /// <summary>
    /// Фильтр тестов: шаблоны через ';' с '*', сопоставляются с "Namespace.Class.Method" без учёта регистра.
    /// Пустой фильтр пропускает все тесты.
    /// </summary>
    public sealed class TestFilter
    {
        private readonly List<Regex> _patterns;

        /// <summary>Начало каждого шаблона до первого '*' (весь шаблон, если '*' нет).</summary>
        private readonly List<string> _prefixes;

        private TestFilter(List<Regex> patterns, List<string> prefixes)
        {
            _patterns = patterns;
            _prefixes = prefixes;
        }

        public bool IsEmpty => _patterns.Count == 0;

        public static TestFilter Parse(string? filter)
        {
            var patterns = new List<Regex>();
            var prefixes = new List<string>();
            if (!string.IsNullOrWhiteSpace(filter))
            {
                foreach (var raw in filter!.Split(';'))
                {
                    var pattern = raw.Trim();
                    if (pattern.Length == 0) continue;
                    var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                    patterns.Add(new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                    var star = pattern.IndexOf('*');
                    prefixes.Add(star < 0 ? pattern : pattern.Substring(0, star));
                }
            }
            return new TestFilter(patterns, prefixes);
        }

        public bool IsMatch(string fullName) => IsEmpty || _patterns.Any(p => p.IsMatch(fullName));

        /// <summary>
        /// Может ли хоть один тест класса <paramref name="typeFullName"/> пройти фильтр — до осмотра самого типа.
        /// Имя теста начинается с "Namespace.Class.", а совпадение с шаблоном требует начала, равного префиксу шаблона
        /// (до первого '*'); false — ни один метод класса фильтр не пройдёт, тип можно не открывать.
        /// Регистр не учитывается так же, как в <see cref="IsMatch"/>.
        /// </summary>
        public bool CouldMatchType(string typeFullName)
        {
            if (typeFullName == null) throw new ArgumentNullException(nameof(typeFullName));
            if (IsEmpty) return true;
            var classPrefix = typeFullName + ".";
            return _prefixes.Any(prefix => prefix.Length <= classPrefix.Length
                ? StartsWithIgnoreCase(classPrefix, prefix)
                : StartsWithIgnoreCase(prefix, classPrefix));
        }

        /// <summary>Посимвольно через ToLowerInvariant — так сравнивает Regex с IgnoreCase | CultureInvariant.</summary>
        private static bool StartsWithIgnoreCase(string text, string start)
        {
            for (var i = 0; i < start.Length; i++)
            {
                if (char.ToLowerInvariant(text[i]) != char.ToLowerInvariant(start[i])) return false;
            }
            return true;
        }
    }
}
