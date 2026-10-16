using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using NUnit.Framework;
using RevitUITesting.Revit;
using RevitUITesting.Uia;
using Wait = RevitUITesting.Uia.Wait;

namespace RevitUITesting.Pages
{
    /// <summary>
    /// Основа page-object: всё ищется внутри <see cref="Root"/>, корень переразрешается на каждом ожидании (панель могла пересоздаться),
    /// ожидания без явного таймаута — <see cref="Wait.DefaultTimeout"/>, нажатие — один раз, ошибки с описанием области и id вокруг.
    /// Наследник задаёт, где его корень (<see cref="ResolveRoot"/>).
    /// </summary>
    public abstract class UiPage
    {
        protected UiPage(RevitApp app, string description)
        {
            App = app ?? throw new ArgumentNullException(nameof(app));
            Description = description;
        }

        private TimeSpan? _callTimeout;

        public RevitApp App { get; }

        /// <summary>Что это за область (для сообщений): «панель «Поиск семейств»», «окно «Источники»».</summary>
        public string Description { get; }

        /// <summary>Актуальный корень области; нет — <see cref="ElementNotFoundException"/>.</summary>
        public AutomationElement Root => ResolveRoot() ?? throw new ElementNotFoundException(Description, "корень области", TimeSpan.Zero, TimeSpan.Zero, null, null);

        /// <summary>
        /// Корень области сейчас; null — области нет (панель скрыта, окно закрыто). Чтения внутри ограничивают себя <see cref="ReadTimeout"/>;
        /// сбой «занят» — <see cref="AppBusyException"/>, а не «области нет».
        /// </summary>
        protected abstract AutomationElement ResolveRoot();

        /// <summary>Сколько можно ждать ответа Revit в одном чтении: таймаут вызова, пока он идёт, иначе <see cref="Wait.DefaultTimeout"/>.</summary>
        protected TimeSpan ReadTimeout => _callTimeout ?? Wait.DefaultTimeout;

        public AutomationElement Find(string automationId, TimeSpan? timeout = null)
        {
            return Find(By.Id(automationId), timeout);
        }

        /// <summary>Элемент по селектору (первый в порядке обхода в ширину).</summary>
        public AutomationElement Find(By selector, TimeSpan? timeout = null)
        {
            TimeSpan limit = timeout ?? Wait.DefaultTimeout;
            return Within(limit, () => UiQuery.WaitFind(ResolveRoot, selector, limit, Description));
        }

        /// <summary>
        /// Элемент без ожидания; null — только по полному обходу (Revit ответил на все запросы). Области нет (панель скрыта, окно закрыто) — тоже null,
        /// поэтому «элемента нет в скрытой панели» этим методом не доказывается.
        /// </summary>
        public AutomationElement TryFind(string automationId)
        {
            return TryFind(By.Id(automationId));
        }

        public AutomationElement TryFind(By selector)
        {
            AutomationElement root = ResolveRoot();
            return root == null ? null : UiQuery.TryFind(root, selector, ReadTimeout);
        }

        public bool Has(string automationId)
        {
            return TryFind(automationId) != null;
        }

        public bool Has(By selector)
        {
            return TryFind(selector) != null;
        }

        /// <summary>Элемента нет: полные обходы без совпадений всё время stableFor (по умолчанию 1 с). Иначе — провал проверки NUnit.</summary>
        public void AssertAbsent(By selector, TimeSpan? stableFor = null)
        {
            if (!Wait.Stays(() => TryFind(selector) == null, stableFor ?? TimeSpan.FromSeconds(1), "нет " + selector))
            {
                Assert.Fail($"В области «{Description}» есть элемент {selector}, хотя его быть не должно.");
            }
        }

        /// <summary>Дождаться доступности элемента и нажать один раз (Invoke в фоне, без повтора).</summary>
        public void Click(string automationId, TimeSpan? timeout = null)
        {
            Click(By.Id(automationId), timeout);
        }

        public void Click(By selector, TimeSpan? timeout = null)
        {
            AutomationElement element = WaitEnabled(selector, timeout);
            UiAct.Press(element, selector + " в «" + Description + "»");
        }

        /// <summary>Кнопка без AutomationId по подписи (Name). Больше одной — неоднозначность.</summary>
        public void ClickText(string caption, TimeSpan? timeout = null)
        {
            By selector = By.Type(ControlType.Button).And(By.Name(caption));
            Single(selector, timeout);
            // доступность читается строго и кнопка переразрешается на каждом опросе (как в Click): «занят» — AppBusyException, не «недоступна»
            AutomationElement button = WaitEnabled(selector, timeout);
            UiAct.Press(button, $"кнопка «{caption}» в «{Description}»");
        }

        /// <summary>Клик мышью — для элементов, у которых Invoke не срабатывает.</summary>
        public void MouseClick(By selector)
        {
            UiAct.Click(Find(selector), selector.ToString(), MouseButton.Left, false);
        }

        public void DoubleClick(By selector)
        {
            UiAct.Click(Find(selector), selector.ToString(), MouseButton.Left, true);
        }

        public void RightClick(By selector)
        {
            UiAct.Click(Find(selector), selector.ToString(), MouseButton.Right, false);
        }

        /// <summary>Записать значение через ValuePattern и прочитать обратно; не применилось — <see cref="ValueNotAppliedException"/>.</summary>
        public void SetValue(string automationId, string value)
        {
            UiAct.SetValue(Find(automationId), value, "поле " + automationId + " в «" + Description + "»");
        }

        public void SetValue(By selector, string value)
        {
            UiAct.SetValue(Single(selector, null), value, selector + " в «" + Description + "»");
        }

        /// <summary>Значение элемента: Value, иначе Name, иначе тексты поддерева.</summary>
        public string ValueOf(string automationId)
        {
            return ValueOf(By.Id(automationId));
        }

        public string ValueOf(By selector)
        {
            AutomationElement element = Single(selector, null);
            string value = UiQuery.Read(() => element.Patterns.Value.PatternOrDefault?.Value.Value, Wait.DefaultTimeout, "Value " + selector);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
            string name = UiQuery.Read(() => element.Properties.Name.ValueOrDefault ?? "", Wait.DefaultTimeout, "Name " + selector);
            return name.Length > 0 ? name : string.Join(" ", element.Texts());
        }

        /// <summary>Доступность единственного элемента; сбой чтения — исключение, а не «недоступен».</summary>
        public bool IsEnabled(By selector)
        {
            AutomationElement element = Single(selector, null);
            return UiQuery.Read(() => element.Properties.IsEnabled.Value, Wait.DefaultTimeout, "доступность " + selector);
        }

        /// <summary>Состояние флажка (TogglePattern); null — у элемента нет TogglePattern.</summary>
        public bool? IsChecked(By selector)
        {
            AutomationElement element = Single(selector, null);
            ToggleState? state = UiQuery.Read(() => element.Patterns.Toggle.PatternOrDefault?.ToggleState.Value, Wait.DefaultTimeout, "состояние " + selector);
            return state == null ? (bool?)null : state == ToggleState.On;
        }

        /// <summary>Флажок в состояние value; постусловие — прочитанное состояние.</summary>
        public void SetChecked(By selector, bool value, CheckInput input = CheckInput.TogglePattern)
        {
            UiChoice.SetChecked(Single(selector, null), value, input, selector + " в «" + Description + "»");
        }

        /// <summary>Подписи пунктов раскрывающегося списка (раскрыть, прочитать, свернуть).</summary>
        public IReadOnlyList<string> ComboItems(By combo)
        {
            return UiChoice.Items(() => App.Windows.Popups(), Single(combo, null), combo + " в «" + Description + "»");
        }

        /// <summary>Выбрать в раскрывающемся списке пункт с точной подписью; возвращает подписи всех пунктов.</summary>
        public IReadOnlyList<string> Choose(By combo, string itemCaption)
        {
            return UiChoice.Choose(() => App.Windows.Popups(), Single(combo, null), itemCaption, combo + " в «" + Description + "»");
        }

        /// <summary>Все непустые тексты области.</summary>
        public IReadOnlyList<string> Texts()
        {
            return Root.Texts();
        }

        public string TryText(Func<string, bool> match)
        {
            return Texts().FirstOrDefault(match);
        }

        public string WaitText(Func<string, bool> match, TimeSpan? timeout = null, [CallerArgumentExpression("match")] string description = null)
        {
            return Wait.ForStrict(() => TryText(match), timeout ?? Wait.DefaultTimeout, "текст " + description + " в «" + Description + "»", null);
        }

        /// <summary>Список или дерево по селектору.</summary>
        public UiList ListBy(By selector)
        {
            return new UiList(() => TryFind(selector), "список " + selector + " в «" + Description + "»");
        }

        public UiList ListById(string listId)
        {
            return ListBy(By.Id(listId));
        }

        /// <summary>Таблица (GridPattern).</summary>
        public UiGrid GridById(string gridId)
        {
            return new UiGrid(() => TryFind(gridId), "таблица " + gridId + " в «" + Description + "»");
        }

        /// <summary>Подробный дамп области (для разбора в тесте).</summary>
        public string Dump(int maxDepth = 40)
        {
            return Root.DumpTree(maxDepth, true);
        }

        public override string ToString()
        {
            return Description;
        }

        internal AutomationElement WaitEnabled(By selector, TimeSpan? timeout)
        {
            TimeSpan limit = timeout ?? Wait.DefaultTimeout;
            Find(selector, limit);
            // элемент ищется заново на каждом опросе: найденный заранее мог быть пересоздан (панель после загрузки данных)
            return Within(limit, () => Wait.ForStrict(() =>
            {
                AutomationElement element = TryFind(selector);
                return element != null && element.Properties.IsEnabled.Value ? element : null;
            }, limit, selector + " доступен в «" + Description + "»", null));
        }

        internal AutomationElement Single(By selector, TimeSpan? timeout)
        {
            TimeSpan limit = timeout ?? Wait.DefaultTimeout;
            Find(selector, limit);
            return Within(limit, () =>
            {
                IReadOnlyList<AutomationElement> all = UiQuery.FindAllComplete(Root, selector, limit, 60);
                if (all.Count > 1)
                {
                    throw new AmbiguousElementException(selector + " в «" + Description + "»", all.Select(e => e.Describe()).ToList());
                }
                return all.Count == 0 ? Find(selector, limit) : all[0];
            });
        }

        /// <summary>Выполнить действие, пока <see cref="ReadTimeout"/> равен таймауту вызова (страница рассчитана на один поток теста).</summary>
        private T Within<T>(TimeSpan limit, Func<T> action)
        {
            TimeSpan? previous = _callTimeout;
            _callTimeout = limit;
            try
            {
                return action();
            }
            finally
            {
                _callTimeout = previous;
            }
        }
    }
}
