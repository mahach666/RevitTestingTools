using System;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using RevitUITesting.Pages;
using RevitUITesting.Uia;

namespace RevitUITesting.Revit
{
    /// <summary>Панели Revit (DockablePane) по заголовку: закреплённые — внутри главного окна, плавающие — окна верхнего уровня процесса.</summary>
    public sealed class RevitPanes
    {
        private readonly RevitApp _app;

        internal RevitPanes(RevitApp app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        /// <summary>Сколько заголовков рамок перечисляет сообщение об ошибке <see cref="Find"/>.</summary>
        internal const int DiagnosticTitles = 40;

        /// <summary>
        /// Дождаться показанной панели (не дольше <see cref="Wait.DefaultTimeout"/>). Найденная рамка передаётся странице: первое обращение
        /// к ней не повторяет полный поиск. В сообщении об ошибке — заголовки рамок одним ограниченным проходом (<see cref="DescribeTitles"/>).
        /// </summary>
        public DockablePanePage Find(string title)
        {
            Require(title);
            AutomationElement found = Wait.ForStrict(() => TryElement(title), Wait.DefaultTimeout, $"панель Revit «{title}»",
                () => "рамки панелей в дереве: " + DescribeTitles(Roots(), RevitIds.Panes.SearchDepth, DiagnosticTitles));
            return new DockablePanePage(_app, title, found, null, null);
        }

        /// <summary>Панель, если она показана сейчас; скрыта или её нет — null.</summary>
        public DockablePanePage TryFind(string title)
        {
            Require(title);
            AutomationElement found = TryElement(title);
            return found == null ? null : new DockablePanePage(_app, title, found, null, null);
        }

        /// <summary>
        /// Заголовки показанных панелей (рамки с непустым Name), первые 40. Полный строгий обход с проверкой каждой рамки — с открытой моделью
        /// это десятки секунд; для сообщений об ошибках библиотека берёт ограниченный проход <see cref="DescribeTitles"/>.
        /// </summary>
        public IReadOnlyList<string> Titles()
        {
            return FindPanes(Roots(), null, RevitIds.Panes.SearchDepth).Where(e => IsShown(e, Wait.DefaultTimeout)).Select(e => e.SafeName()).Where(n => n.Length > 0).Distinct().Take(40).ToList();
        }

        /// <summary>
        /// Заголовки рамок панелей для сообщения об ошибке: один проход по каждому корню без повторов, не глубже maxDepth, не больше maxCount
        /// рамок, без вложенной проверки и без строгих чтений (IsOffscreen не читается: в перечень попадают и скрытые рамки). Корень попадает
        /// в перечень, только если под ним рамок нет (плавающая панель, а не главное окно). Занятый Revit или исчезнувшие узлы — пометка
        /// «обход неполный» в тексте, а не исключение: диагностика не заслоняет исходную ошибку и не ждёт.
        /// </summary>
        internal static string DescribeTitles(IEnumerable<AutomationElement> roots, int maxDepth, int maxCount)
        {
            By frame = Frame(null);
            List<string> names = new List<string>();
            string incomplete = null;
            foreach (AutomationElement root in roots)
            {
                int left = maxCount - names.Count;
                if (left <= 0)
                {
                    break;
                }
                UiQueryResult result = UiQuery.Find(root, frame, maxDepth, left);
                names.AddRange(result.Matches.Select(e => e.SafeName()));
                if (result.Matches.Count == 0 && result.Complete && RootIsFrame(root, frame, ref incomplete))
                {
                    names.Add(root.SafeName());
                }
                if (!result.Complete)
                {
                    incomplete = result.LastFault == null ? "узлы исчезали" : result.LastFault.GetType().Name + ": " + result.LastFault.Message;
                }
                if (result.BusyFaults > 0)
                {
                    // следующий запрос к занятому Revit снова ждал бы таймаут UI Automation
                    break;
                }
            }
            List<string> distinct = names.Where(n => n.Length > 0).Distinct().ToList();
            string text = distinct.Count == 0 ? "рамок с заголовком не найдено" : string.Join(", ", distinct);
            if (names.Count >= maxCount)
            {
                text += $" (первые {maxCount})";
            }
            return incomplete == null ? text : text + $" (обход неполный: {incomplete})";
        }

        /// <summary>Корень — рамка панели; сбой «занят»/«исчез» — не рамка, причина — в incomplete (диагностика без исключений).</summary>
        private static bool RootIsFrame(AutomationElement root, By frame, ref string incomplete)
        {
            try
            {
                return frame.Matches(new ElementFacts(root));
            }
            catch (Exception ex) when (UiFault.IsTransient(ex))
            {
                // диагностика: сбой чтения корня не прерывает перечень, а отмечается в тексте
                incomplete = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>Показанная панель с заголовком; нет — null. Чтения строгие: занятый Revit — <see cref="AppBusyException"/>, а не «панели нет».</summary>
        internal AutomationElement TryElement(string title, TimeSpan? timeout = null)
        {
            return ShownPane(Roots(), title, RevitIds.Panes.SearchDepth, timeout ?? Wait.DefaultTimeout);
        }

        /// <summary>Единственная показанная (не за пределами экрана) панель среди рамок с заголовком; нет — null; больше одной — неоднозначность.</summary>
        internal static AutomationElement ShownPane(IEnumerable<AutomationElement> roots, string title, int maxDepth, TimeSpan timeout)
        {
            List<AutomationElement> shown = FindPanes(roots, title, maxDepth, timeout).Where(e => IsShown(e, timeout)).ToList();
            if (shown.Count > 1)
            {
                throw new AmbiguousElementException($"панель Revit «{title}»", shown.Select(e => e.Describe()).ToList());
            }
            return shown.FirstOrDefault();
        }

        /// <summary>Рамка не за пределами экрана. Рамка, исчезнувшая при перечислении, — не показана (явный результат, не сбой).</summary>
        private static bool IsShown(AutomationElement frame, TimeSpan timeout)
        {
            return !UiRead.OrGone(() => frame.Properties.IsOffscreen.ValueOrDefault, true, timeout, "IsOffscreen панели Revit");
        }

        /// <summary>Рамки панелей с Name == title (title null — любой непустой Name) в корнях и под ними; из вложенных совпадений — только самые глубокие.</summary>
        internal static List<AutomationElement> FindPanes(IEnumerable<AutomationElement> roots, string title, int maxDepth, TimeSpan? timeout = null)
        {
            By frame = Frame(title);
            TimeSpan limit = timeout ?? Wait.DefaultTimeout;
            List<AutomationElement> candidates = new List<AutomationElement>();
            foreach (AutomationElement root in roots)
            {
                // окно верхнего уровня, закрывшееся до проверки, панелью не является (FindAllComplete исчезнувший корень пропускает сам)
                if (UiRead.OrGone(() => frame.Matches(new ElementFacts(root)), false, limit, "окно-корень панели"))
                {
                    candidates.Add(root);
                }
                candidates.AddRange(UiQuery.FindAllComplete(root, frame, limit, maxDepth));
            }
            return candidates.Where(c => UiQuery.FindAllComplete(c, frame, limit, maxDepth).Count == 0).ToList();
        }

        /// <summary>Рамка панели: Pane или Window с Name == title (title null — любой непустой Name).</summary>
        private static By Frame(string title)
        {
            return By.Where(f => (title == null ? f.Name.Length > 0 : f.Name == title) && RevitIds.Panes.FrameTypes.Contains(f.ControlType),
                title == null ? "панель Revit" : $"панель Revit «{title}»");
        }

        private IEnumerable<AutomationElement> Roots()
        {
            List<AutomationElement> roots = new List<AutomationElement>();
            AutomationElement main = _app.TryMainWindow();
            if (main != null)
            {
                roots.Add(main);
            }
            roots.AddRange(_app.Windows.All());
            return roots;
        }

        private static void Require(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException("Нужен заголовок панели.", nameof(title));
            }
        }
    }
}
