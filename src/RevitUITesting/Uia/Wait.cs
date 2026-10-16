using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RevitUITesting.Uia
{
    /// <summary>
    /// Ожидания с опросом. «Ещё не готово» — только временные сбои UI Automation (приложение занято, элемент исчез);
    /// прочие исключения (в том числе ошибка в условии) пробрасываются сразу. Перегрузки без таймаута берут <see cref="DefaultTimeout"/>.
    /// Условие проверяется хотя бы раз и ещё раз после дедлайна. Повтора действий здесь нет: нажатие выполняется один раз,
    /// его эффект проверяется ожиданием постусловия.
    /// </summary>
    public static class Wait
    {
        public static TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

        /// <summary>Сколько ждать «оживления» потока UI приложения после сбоя «занят» во время нажатия.</summary>
        public static TimeSpan UiBusyTimeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>Таймаут перегрузок без явного таймаута. Сеанс приложения ставит его из конфигурации (defaultTimeoutSeconds).</summary>
        public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Ждать условие <see cref="DefaultTimeout"/>. Описание по умолчанию — текст выражения условия.</summary>
        public static void Until(Func<bool> condition, [CallerArgumentExpression("condition")] string description = null)
        {
            Strict(condition, DefaultTimeout, description, null);
        }

        /// <summary>Ждать условие не дольше timeout (правила — как у <see cref="Until(Func{bool}, string)"/>).</summary>
        public static void Until(Func<bool> condition, TimeSpan timeout, [CallerArgumentExpression("condition")] string description = null)
        {
            Strict(condition, timeout, description, null);
        }

        /// <summary>Дождаться непустого результата <see cref="DefaultTimeout"/>.</summary>
        public static T For<T>(Func<T> producer, [CallerArgumentExpression("producer")] string description = null) where T : class
        {
            return ForStrict(producer, DefaultTimeout, description, null);
        }

        /// <summary>Дождаться непустого результата не дольше timeout.</summary>
        public static T For<T>(Func<T> producer, TimeSpan timeout, [CallerArgumentExpression("producer")] string description = null) where T : class
        {
            return ForStrict(producer, timeout, description, null);
        }

        /// <summary>
        /// Условие остаётся истинным всё время duration («кнопка так и остаётся недоступной»). false — стало ложным.
        /// Временные сбои UI Automation не считаются «ложью», но конечное состояние должно быть увидено: проверка после конца
        /// интервала обязана выполниться. Если она срывается, опрос продолжается ещё не дольше <see cref="DefaultTimeout"/>,
        /// затем <see cref="AppBusyException"/> (или <see cref="UiWaitTimeoutException"/>, если проверяемый элемент исчез).
        /// </summary>
        public static bool Stays(Func<bool> condition, TimeSpan duration, [CallerArgumentExpression("condition")] string description = null)
        {
            Stopwatch sw = Stopwatch.StartNew();
            TimeSpan limit = duration + DefaultTimeout;
            UiFaults faults = new UiFaults();
            while (true)
            {
                bool expired = sw.Elapsed >= duration;
                bool checkedNow = false;
                try
                {
                    if (!condition())
                    {
                        return false;
                    }
                    checkedNow = true;
                }
                catch (Exception ex) when (UiFault.IsTransient(ex))
                {
                    faults.Add(ex);
                }
                if (expired)
                {
                    if (checkedNow)
                    {
                        return true;
                    }
                    if (sw.Elapsed >= limit)
                    {
                        throw faults.Failure("проверка «" + description + "» в конце интервала", limit, sw.Elapsed);
                    }
                }
                Thread.Sleep(PollInterval);
            }
        }

        /// <summary>
        /// Записывать различающиеся подряд значения sample (например, ход загрузки в строке состояния), пока не придёт значение,
        /// для которого done истинно; оно последнее в списке. Временные сбои UI Automation пропускаются. Не дождались —
        /// <see cref="UiWaitTimeoutException"/> с записанными значениями. Ни одно значение не удалось прочитать, потому что приложение
        /// всё время было занято, — <see cref="AppBusyException"/> (если проверяемый элемент исчезал — <see cref="UiWaitTimeoutException"/> с этой причиной).
        /// interval null — <see cref="PollInterval"/>.
        /// </summary>
        public static IReadOnlyList<T> RecordUntil<T>(Func<T> sample, Func<T, bool> done, TimeSpan timeout, TimeSpan? interval = null, [CallerArgumentExpression("sample")] string description = null)
        {
            if (sample == null || done == null)
            {
                throw new ArgumentNullException(sample == null ? nameof(sample) : nameof(done));
            }
            List<T> values = new List<T>();
            Stopwatch sw = Stopwatch.StartNew();
            int completed = 0;
            UiFaults faults = new UiFaults();
            while (true)
            {
                bool expired = sw.Elapsed >= timeout;
                try
                {
                    T value = sample();
                    completed++;
                    if (values.Count == 0 || !EqualityComparer<T>.Default.Equals(values[values.Count - 1], value))
                    {
                        values.Add(value);
                    }
                    if (done(value))
                    {
                        return values;
                    }
                }
                catch (Exception ex) when (UiFault.IsTransient(ex))
                {
                    faults.Add(ex);
                }
                if (expired)
                {
                    if (completed == 0 && faults.Any)
                    {
                        throw faults.Failure(description, timeout, sw.Elapsed);
                    }
                    throw new UiWaitTimeoutException(description, timeout, sw.Elapsed, "записано: " + string.Join(" → ", values), null);
                }
                Thread.Sleep(interval ?? PollInterval);
            }
        }

        /// <summary>
        /// Условие стало истинным не позже timeout — true; иначе false без исключения таймаута и без сбора диагностики.
        /// Исключения условия не перехватываются: условие само решает, что считать «ещё нет» (например, строгим чтением UiQuery.Read, которое при занятом приложении даёт AppBusyException).
        /// </summary>
        internal static bool Holds(Func<bool> condition, TimeSpan timeout)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                bool expired = sw.Elapsed >= timeout;
                if (condition())
                {
                    return true;
                }
                if (expired)
                {
                    return false;
                }
                Thread.Sleep(PollInterval);
            }
        }

        /// <summary>Учёт временных сбоев ожидания: «занят» и «исчез» — разные диагнозы.</summary>
        private sealed class UiFaults
        {
            private int _busy;
            private int _gone;
            private Exception _last;
            private Exception _lastGone;

            internal void Add(Exception ex)
            {
                if (UiFault.Classify(ex) == UiFaultKind.Gone)
                {
                    _gone++;
                    _lastGone = ex;
                }
                else
                {
                    _busy++;
                }
                _last = ex;
            }

            internal bool Any => _busy + _gone > 0;

            /// <summary>Ни одна проверка не выполнилась: только «занят» — <see cref="AppBusyException"/>; был «исчез» — таймаут с этой ошибкой.</summary>
            internal UiWaitTimeoutException Failure(string description, TimeSpan timeout, TimeSpan elapsed)
            {
                if (_gone == 0)
                {
                    return new AppBusyException(description, timeout, elapsed, _busy, _last);
                }
                return new UiWaitTimeoutException(description + " (проверяемый элемент исчез из дерева UI Automation)", timeout, elapsed, null, _lastGone);
            }
        }

        /// <summary>Строгое ожидание: временные сбои UIA — «не готово», прочее — пробрасывается; observe — что писать в «последнее наблюдение».</summary>
        internal static void Strict(Func<bool> condition, TimeSpan timeout, string description, Func<string> observe)
        {
            ForStrict(() => condition() ? (object)true : null, timeout, description, observe);
        }

        internal static T ForStrict<T>(Func<T> producer, TimeSpan timeout, string description, Func<string> observe) where T : class
        {
            Stopwatch sw = Stopwatch.StartNew();
            int completed = 0;
            UiFaults faults = new UiFaults();
            Exception last = null;
            while (true)
            {
                bool expired = sw.Elapsed >= timeout;
                try
                {
                    T result = producer();
                    if (result != null)
                    {
                        return result;
                    }
                    completed++;
                }
                catch (Exception ex) when (UiFault.IsTransient(ex))
                {
                    faults.Add(ex);
                    last = ex;
                }
                if (expired)
                {
                    if (completed == 0 && faults.Any)
                    {
                        throw faults.Failure(description, timeout, sw.Elapsed);
                    }
                    string observation = null;
                    if (observe != null)
                    {
                        try
                        {
                            observation = observe();
                        }
                        catch (Exception ex) when (UiFault.IsTransient(ex))
                        {
                            observation = "(не прочитано: " + ex.Message + ")";
                        }
                    }
                    throw new UiWaitTimeoutException(description, timeout, sw.Elapsed, observation, last);
                }
                Thread.Sleep(PollInterval);
            }
        }
    }
}
