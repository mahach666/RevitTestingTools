using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RevitTestHost.Core.Execution
{
    /// <summary>Параметры прогона для <c>ITestContext.Parameters</c>.</summary>
    public static class RunParameters
    {
        /// <summary>Пустой словарь только для чтения: параметры CLI-режима и кнопки ленты.</summary>
        public static readonly IReadOnlyDictionary<string, string> Empty =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        /// <summary>Копия словаря только для чтения; ключи сравниваются с учётом регистра.</summary>
        public static IReadOnlyDictionary<string, string> From(IEnumerable<KeyValuePair<string, string>> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in source) copy.Add(pair.Key, pair.Value);
            return new ReadOnlyDictionary<string, string>(copy);
        }
    }
}
