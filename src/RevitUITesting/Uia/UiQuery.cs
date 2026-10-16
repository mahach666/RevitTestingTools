using System;
using System.Collections.Generic;
using System.Diagnostics;
using FlaUI.Core.AutomationElements;

namespace RevitUITesting.Uia
{
    /// <summary>Результат обхода: найденное и признак, что обход прошёл без сбоев.</summary>
    internal sealed class UiQueryResult
    {
        internal List<AutomationElement> Matches { get; } = new List<AutomationElement>();

        /// <summary>Все узлы обойдены: «не найдено» при Complete = true означает «точно нет».</summary>
        internal bool Complete { get; set; } = true;

        /// <summary>Сбои «приложение занято» (UIA_E_TIMEOUT / RPC_E_TIMEOUT).</summary>
        internal int BusyFaults { get; set; }

        /// <summary>Узлы ниже корня, исчезнувшие во время обхода: снимок дерева устарел (например, строки пересоздались), обход неполный.</summary>
        internal int StaleFaults { get; set; }

        internal Exception LastFault { get; set; }
    }

    /// <summary>
    /// Обход дерева в ширину через FindAllChildren (штатный FindFirst(Descendants) в окнах WPF с тяжёлым содержимым зависает),
    /// с учётом сбоев: «занят» — обход прерывается как неполный (каждый следующий запрос к зависшему приложению ждал бы таймаут UIA);
    /// «исчез» у корня — области нет (обход полный, ничего не найдено); «исчез» ниже корня — снимок устарел, обход неполный;
    /// прочие исключения пробрасываются, в том числе ошибки в условиях автора теста.
    /// </summary>
    internal static class UiQuery
    {
        internal const int DefaultMaxDepth = AutomationElementExtensions.DefaultMaxDepth;

        internal static UiQueryResult Find(AutomationElement root, By selector, int maxDepth = DefaultMaxDepth, int limit = int.MaxValue)
        {
            UiQueryResult result = new UiQueryResult();
            if (root == null)
            {
                return result;
            }
            Queue<KeyValuePair<AutomationElement, int>> queue = new Queue<KeyValuePair<AutomationElement, int>>();
            queue.Enqueue(new KeyValuePair<AutomationElement, int>(root, 0));
            while (queue.Count > 0)
            {
                KeyValuePair<AutomationElement, int> current = queue.Dequeue();
                if (current.Value >= maxDepth)
                {
                    continue;
                }
                AutomationElement[] children = Children(current.Key, result, current.Value == 0);
                if (result.BusyFaults > 0)
                {
                    return result;
                }
                foreach (AutomationElement child in children)
                {
                    if (Matches(child, selector, result))
                    {
                        result.Matches.Add(child);
                        if (result.Matches.Count >= limit)
                        {
                            return result;
                        }
                    }
                    if (result.BusyFaults > 0)
                    {
                        return result;
                    }
                    queue.Enqueue(new KeyValuePair<AutomationElement, int>(child, current.Value + 1));
                }
            }
            return result;
        }

        /// <summary>
        /// Прямые потомки с учётом сбоев (без исключения для «занят»/«исчез»).
        /// scopeRoot = true: element — сама область, её исчезновение означает «потомков нет» (обход полный);
        /// false: element получен из снимка родителя, его исчезновение — устаревший снимок (обход неполный).
        /// </summary>
        internal static AutomationElement[] Children(AutomationElement element, UiQueryResult result, bool scopeRoot = true)
        {
            try
            {
                return element.FindAllChildren();
            }
            catch (Exception ex) when (Transient(ex, result, scopeRoot))
            {
                return new AutomationElement[0];
            }
        }

        private static bool Matches(AutomationElement element, By selector, UiQueryResult result)
        {
            try
            {
                return selector.Matches(new ElementFacts(element));
            }
            catch (Exception ex) when (Transient(ex, result, false))
            {
                return false;
            }
        }

        private static bool Transient(Exception ex, UiQueryResult result, bool scopeRoot)
        {
            switch (UiFault.Classify(ex))
            {
                case UiFaultKind.Busy:
                    result.Complete = false;
                    result.BusyFaults++;
                    result.LastFault = ex;
                    return true;
                case UiFaultKind.Gone:
                    if (!scopeRoot)
                    {
                        result.Complete = false;
                        result.StaleFaults++;
                        result.LastFault = ex;
                    }
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Отказ после неполных обходов: были сбои «занят» — <see cref="AppBusyException"/>; иначе дерево менялось во время каждого обхода.</summary>
        private static UiWaitTimeoutException Incomplete(string what, TimeSpan timeout, TimeSpan elapsed, int busy, Exception last)
        {
            if (busy > 0)
            {
                return new AppBusyException(what, timeout, elapsed, busy, last);
            }
            return new UiWaitTimeoutException(what + ": дерево UI Automation менялось во время каждого обхода (узлы исчезали)", timeout, elapsed, null, last);
        }

        /// <summary>
        /// Первый элемент по селектору. Найден — возвращается даже при неполном обходе; не найден при полном обходе — null;
        /// не найден при неполном — повторные обходы (не дольше timeout), затем <see cref="AppBusyException"/>
        /// (или <see cref="UiWaitTimeoutException"/>, если обходы срывались только из-за исчезающих узлов).
        /// </summary>
        internal static AutomationElement TryFind(AutomationElement root, By selector, TimeSpan timeout, int maxDepth = DefaultMaxDepth)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int busy = 0;
            Exception last = null;
            while (true)
            {
                UiQueryResult result = Find(root, selector, maxDepth, 1);
                if (result.Matches.Count > 0)
                {
                    return result.Matches[0];
                }
                if (result.Complete)
                {
                    return null;
                }
                busy += result.BusyFaults;
                last = result.LastFault ?? last;
                if (sw.Elapsed >= timeout)
                {
                    throw Incomplete("полный обход дерева UI Automation для поиска " + selector, timeout, sw.Elapsed, busy, last);
                }
                System.Threading.Thread.Sleep(Wait.PollInterval);
            }
        }

        /// <summary>Все элементы по селектору при полном обходе (повторы при неполном обходе, затем отказ: при неполных обходах — <see cref="AppBusyException"/> (приложение не отвечало) или <see cref="UiWaitTimeoutException"/> (узлы исчезали во время каждого обхода)).</summary>
        internal static List<AutomationElement> FindAllComplete(AutomationElement root, By selector, TimeSpan timeout, int maxDepth = DefaultMaxDepth)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int busy = 0;
            Exception last = null;
            while (true)
            {
                UiQueryResult result = Find(root, selector, maxDepth);
                if (result.Complete)
                {
                    return result.Matches;
                }
                busy += result.BusyFaults;
                last = result.LastFault ?? last;
                if (sw.Elapsed >= timeout)
                {
                    throw Incomplete("полный обход дерева UI Automation для поиска " + selector, timeout, sw.Elapsed, busy, last);
                }
                System.Threading.Thread.Sleep(Wait.PollInterval);
            }
        }

        /// <summary>
        /// Ждать элемент по селектору в области, корень которой разрешается заново на каждом опросе (вкладка, окно, представление плагина).
        /// root() → null — области пока нет. Отказ: области так и не было — <see cref="ElementNotFoundException"/> про область
        /// (если приложение ни разу не ответило на запрос области — <see cref="AppBusyException"/>);
        /// ни одного полного обхода — отказ при неполных обходах — <see cref="AppBusyException"/> (приложение не отвечало) или <see cref="UiWaitTimeoutException"/> (узлы исчезали во время каждого обхода);
        /// иначе <see cref="ElementNotFoundException"/> с перечнем id области.
        /// </summary>
        internal static AutomationElement WaitFind(Func<AutomationElement> root, By selector, TimeSpan timeout, string scope, int maxDepth = DefaultMaxDepth)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int busy = 0;
            int completed = 0;
            int rootAnswers = 0;
            Exception last = null;
            AutomationElement lastRoot = null;
            while (true)
            {
                bool expired = sw.Elapsed >= timeout;
                AutomationElement current;
                try
                {
                    current = root();
                    rootAnswers++;
                }
                catch (Exception ex) when (UiFault.IsTransient(ex))
                {
                    current = null;
                    if (UiFault.Classify(ex) == UiFaultKind.Busy)
                    {
                        busy++;
                    }
                    else
                    {
                        // область исчезла, пока её разрешали, — для ожидания это «области пока нет»
                        rootAnswers++;
                    }
                    last = ex;
                }
                if (current != null)
                {
                    lastRoot = current;
                    UiQueryResult result = Find(current, selector, maxDepth, 1);
                    if (result.Matches.Count > 0)
                    {
                        return result.Matches[0];
                    }
                    if (result.Complete)
                    {
                        completed++;
                    }
                    else
                    {
                        busy += result.BusyFaults;
                        last = result.LastFault ?? last;
                    }
                }
                if (expired)
                {
                    if (lastRoot == null)
                    {
                        if (rootAnswers == 0)
                        {
                            throw new AppBusyException("область поиска «" + scope + "»", timeout, sw.Elapsed, busy, last);
                        }
                        throw new ElementNotFoundException(scope, "область поиска (её нет: вкладка закрыта, окно не открыто или представление не активно)", timeout, sw.Elapsed, null, last);
                    }
                    if (completed == 0)
                    {
                        throw Incomplete("элемент " + selector + " в области «" + scope + "»", timeout, sw.Elapsed, busy, last);
                    }
                    throw new ElementNotFoundException(scope, selector.ToString(), timeout, sw.Elapsed, IdsIn(lastRoot), last);
                }
                System.Threading.Thread.Sleep(Wait.PollInterval);
            }
        }

        /// <summary>
        /// AutomationId в области — только для текста сообщения «не найден»: уникальные, в порядке обхода; обход останавливается
        /// после limit узлов, неполный обход допустим. Временные сбои UI Automation поглощаются обходом и отражаются
        /// последней строкой «(обход неполный: занят N, исчезло M)»; прочие исключения пробрасываются. Для сверки нужен <see cref="IdsInComplete"/>.
        /// </summary>
        internal static IReadOnlyList<string> IdsIn(AutomationElement root, int maxDepth = 30, int limit = 1000)
        {
            List<string> ids = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int visited = 0;
            // «совпадение» на limit-м узле останавливает обход (Find с limit = 1)
            UiQueryResult result = Find(root, By.Where(f =>
            {
                string id = f.AutomationId;
                if (id.Length > 0 && seen.Add(id))
                {
                    ids.Add(id);
                }
                return ++visited >= limit;
            }, "сбор id"), maxDepth, 1);
            if (ids.Count > limit)
            {
                ids = ids.GetRange(0, limit);
            }
            if (result.BusyFaults > 0 || result.StaleFaults > 0)
            {
                ids.Add("(обход неполный: занят " + result.BusyFaults + ", исчезло " + result.StaleFaults + ")");
            }
            return ids;
        }

        /// <summary>
        /// Все AutomationId области по полному обходу (сверка каталога): при неполном обходе — повтор, по таймауту отказ
        /// при неполных обходах — <see cref="AppBusyException"/> (приложение не отвечало) или <see cref="UiWaitTimeoutException"/> (узлы исчезали во время каждого обхода); прочие исключения пробрасываются.
        /// </summary>
        internal static IReadOnlyList<string> IdsInComplete(AutomationElement root, TimeSpan timeout, int maxDepth)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int busy = 0;
            Exception last = null;
            while (true)
            {
                List<string> ids = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                UiQueryResult result = Find(root, By.Where(f =>
                {
                    string id = f.AutomationId;
                    if (id.Length > 0 && seen.Add(id))
                    {
                        ids.Add(id);
                    }
                    return false;
                }, "сбор id"), maxDepth);
                if (result.Complete)
                {
                    return ids;
                }
                busy += result.BusyFaults;
                last = result.LastFault ?? last;
                if (sw.Elapsed >= timeout)
                {
                    throw Incomplete("полный обход дерева UI Automation для сбора AutomationId", timeout, sw.Elapsed, busy, last);
                }
                System.Threading.Thread.Sleep(Wait.PollInterval);
            }
        }

        /// <summary>Прочитать значение UIA, повторяя чтение, пока приложение занято (чтение безопасно повторять, в отличие от действий).</summary>
        internal static T Read<T>(Func<T> read, TimeSpan timeout, string what)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int busy = 0;
            while (true)
            {
                try
                {
                    return read();
                }
                catch (Exception ex) when (UiFault.Classify(ex) == UiFaultKind.Busy)
                {
                    busy++;
                    if (sw.Elapsed >= timeout)
                    {
                        throw new AppBusyException("чтение: " + what, timeout, sw.Elapsed, busy, ex);
                    }
                    System.Threading.Thread.Sleep(Wait.PollInterval);
                }
            }
        }
    }
}
