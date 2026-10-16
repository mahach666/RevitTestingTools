using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitUITesting.Uia
{
    /// <summary>
    /// Не дождались условия. Наследник <see cref="TimeoutException"/>: прежний код с catch (TimeoutException) работает.
    /// В тексте — что ждали, сколько, последнее наблюдение и диагностика приложения (окна приложения, последние шаги).
    /// </summary>
    public class UiWaitTimeoutException : TimeoutException
    {
        public UiWaitTimeoutException(string description, TimeSpan timeout, TimeSpan elapsed, string lastObservation, Exception lastError)
            : this(description, timeout, elapsed, lastObservation, lastError, null)
        {
        }

        protected UiWaitTimeoutException(string description, TimeSpan timeout, TimeSpan elapsed, string lastObservation, Exception lastError, string details)
            : base(Compose(description, timeout, lastObservation, lastError, details), lastError)
        {
            Description = description;
            Timeout = timeout;
            Elapsed = elapsed;
            LastObservation = lastObservation;
            Diagnostics = WaitDiagnostics.Collect();
        }

        /// <summary>Что ждали.</summary>
        public string Description { get; }

        public TimeSpan Timeout { get; }

        public TimeSpan Elapsed { get; }

        /// <summary>Последнее, что удалось увидеть (например, текущий текст элемента); null, если нечего сказать.</summary>
        public string LastObservation { get; }

        /// <summary>Диагностика приложения в момент отказа (её источник задаёт сеанс приложения) и последние шаги журнала.</summary>
        public string Diagnostics { get; }

        public override string Message
        {
            get
            {
                string message = base.Message;
                return string.IsNullOrEmpty(Diagnostics) ? message : message + Environment.NewLine + Diagnostics;
            }
        }

        private static string Compose(string description, TimeSpan timeout, string lastObservation, Exception lastError, string details)
        {
            string text = $"Не дождались: {description} (таймаут {timeout.TotalSeconds:0.#} с)";
            if (!string.IsNullOrEmpty(lastObservation))
            {
                text += "; последнее наблюдение: " + lastObservation;
            }
            if (lastError != null)
            {
                text += "; последняя ошибка: " + lastError.GetType().Name + ": " + lastError.Message;
            }
            if (!string.IsNullOrEmpty(details))
            {
                text += Environment.NewLine + details;
            }
            return text;
        }
    }

    /// <summary>Элемент не появился в области поиска за отведённое время.</summary>
    public sealed class ElementNotFoundException : UiWaitTimeoutException
    {
        public ElementNotFoundException(string scope, string selector, TimeSpan timeout, TimeSpan elapsed, IReadOnlyList<string> idsInScope, Exception lastError)
            : base($"элемент {selector} в области «{scope}»", timeout, elapsed, null, lastError, DescribeIds(idsInScope))
        {
            Scope = scope;
            Selector = selector;
            IdsInScope = idsInScope ?? new string[0];
        }

        /// <summary>Описание области, в которой искали.</summary>
        public string Scope { get; }

        public string Selector { get; }

        /// <summary>AutomationId, которые в области есть (первые 60) — чаще всего нужный id отличается одной буквой.</summary>
        public IReadOnlyList<string> IdsInScope { get; }

        private static string DescribeIds(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return null;
            }
            return "AutomationId в области: " + string.Join(", ", ids.Take(60)) + (ids.Count > 60 ? $" … (всего {ids.Count})" : "");
        }
    }

    /// <summary>
    /// За всё время ожидания ни одна проверка не выполнилась до конца: поток UI приложения не отвечал на запросы UI Automation
    /// (UIA_E_TIMEOUT / RPC_E_TIMEOUT). Это не «элемента нет», а «приложение занято».
    /// </summary>
    public class AppBusyException : UiWaitTimeoutException
    {
        /// <summary>Для наследников со своим описанием занятости (например, сервер канала Revit не ответил): BusyFaults = 0.</summary>
        protected AppBusyException(string description, TimeSpan timeout, TimeSpan elapsed, Exception lastError, string details)
            : base(description, timeout, elapsed, null, lastError, details)
        {
        }

        public AppBusyException(string description, TimeSpan timeout, TimeSpan elapsed, int busyFaults, Exception lastError)
            : base(description, timeout, elapsed, null, lastError, $"Приложение было занято: {busyFaults} запросов UI Automation подряд отклонены по таймауту, ни одна проверка не выполнилась полностью.")
        {
            BusyFaults = busyFaults;
        }

        public int BusyFaults { get; }
    }

    /// <summary>Селектору соответствует больше одного элемента, а действие требует ровно один.</summary>
    public sealed class AmbiguousElementException : InvalidOperationException
    {
        public AmbiguousElementException(string what, IReadOnlyList<string> matches)
            : base($"Неоднозначно: {what} — найдено {matches.Count}: {string.Join("; ", matches.Take(20))}")
        {
            Matches = matches;
        }

        public IReadOnlyList<string> Matches { get; }
    }

    /// <summary>Значение записано, но элемент показывает другое (ввод не применился).</summary>
    public sealed class ValueNotAppliedException : InvalidOperationException
    {
        public ValueNotAppliedException(string target, string expected, string actual)
            : base($"Значение не применилось: {target}; ожидалось «{expected}», в элементе «{actual}»")
        {
            Target = target;
            Expected = expected;
            Actual = actual;
        }

        public string Target { get; }

        public string Expected { get; }

        public string Actual { get; }
    }

    /// <summary>
    /// Источник диагностики для сообщений о таймаутах (задаёт сеанс приложения). Сбор не входит сам в себя:
    /// таймаут, возникший во время сбора (приложение занято), получает сообщение без диагностики приложения, а не запускает сбор заново.
    /// </summary>
    internal static class WaitDiagnostics
    {
        [ThreadStatic]
        private static bool _collecting;

        internal static Func<string> Provider { get; set; }

        internal static string Collect()
        {
            List<string> parts = new List<string>();
            Func<string> provider = Provider;
            if (provider != null && _collecting)
            {
                parts.Add("Диагностика приложения не собиралась: сбой возник во время её сбора.");
            }
            else if (provider != null)
            {
                _collecting = true;
                try
                {
                    string text = provider();
                    if (!string.IsNullOrEmpty(text))
                    {
                        parts.Add(text);
                    }
                }
                catch (Exception ex)
                {
                    parts.Add("Диагностика приложения не собрана: " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    _collecting = false;
                }
            }
            IReadOnlyList<string> steps = UiLog.Recent(8);
            if (steps.Count > 0)
            {
                parts.Add("Последние шаги:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", steps));
            }
            return string.Join(Environment.NewLine, parts);
        }
    }
}
