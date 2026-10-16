using System;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Patterns;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using RevitUITesting.Uia;

namespace RevitUITesting.Revit
{
    /// <summary>
    /// Лента Revit (AdWindows): вкладка по подписи, кнопка по внутреннему имени PushButtonData или подписи, свёрнутые панели и выпадающие
    /// кнопки. Параметры поиска — <see cref="RevitIds.Ribbon"/> (зафиксированы контрактными тестами); нажатие — один раз, без повтора.
    /// Вкладка и кнопка в UIA одного типа (Button), поэтому вкладка ищется только среди вкладок (<see cref="RibbonMatch.FindTabs"/>),
    /// а кнопка — только в содержимом своей вкладки: «Modify» — это и вкладка, и кнопка панели Select.
    /// После нажатия <see cref="Click"/> и <see cref="ClickInMenu"/> (в том числе кнопки свёрнутой панели) курсор один раз переносится в центр
    /// клиентской области главного окна Revit — в область вида, в стороне от ленты: курсор, оставленный на ленте, вызывает подсказку
    /// (<see cref="RevitIds.Dialogs.RibbonToolTipName"/>), которая не закрывается по Esc (Evidence/closing-run-2022.txt, запуск D). Курсор
    /// переносится, только если главное окно и окно в этой точке принадлежат процессу Revit; клика нет, ввод в другие приложения не уходит.
    /// </summary>
    public sealed class RevitRibbon
    {
        /// <summary>Сколько ждать кнопку после раскрытия свёрнутой панели (только чтение), прежде чем свернуть панель обратно.</summary>
        private static readonly TimeSpan PanelPopupTimeout = TimeSpan.FromSeconds(5);

        /// <summary>Сколько ждать закрытия окна свёрнутой панели после Toggle и после Esc (только чтение).</summary>
        private static readonly TimeSpan PanelCloseTimeout = TimeSpan.FromSeconds(5);

        private readonly RevitApp _app;

        internal RevitRibbon(RevitApp app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        /// <summary>Подписи вкладок ленты (нормализованные, без повторов; скрытые вкладки не входят).</summary>
        public IReadOnlyList<string> Tabs()
        {
            List<RibbonTab> tabs = Wait.ForStrict(() => RibbonMatch.FindTabs(RibbonRoot(), null), Wait.DefaultTimeout, "вкладки ленты", null);
            return tabs.Select(t => CaptionText.Normalize(t.Name)).Where(n => n.Length > 0).Distinct().ToList();
        }

        /// <summary>
        /// Найти вкладку по подписи или внутреннему имени и сделать её активной (уже активная не нажимается). Возвращает кнопку вкладки, а если
        /// активной вкладке не хватило места на полосе вкладок (узкое окно; её кнопки тогда нет в дереве UIA) — её содержимое.
        /// </summary>
        public AutomationElement Tab(string name)
        {
            RibbonTab tab = Activate(name);
            return tab.Element ?? tab.Content;
        }

        /// <summary>
        /// Кнопка на вкладке: видимая в содержимом вкладки или внутри свёрнутой панели. Ожидание только читает: видимую кнопку или показанные
        /// свёрнутые панели; затем каждая свёрнутая панель раскрывается один раз (Toggle), кнопка ищется во всплывающем окне, появившемся после
        /// раскрытия, без отбора по экрану (UIA держит окно панели offscreen); нажатие (UiAct.Press) — Invoke или Toggle, без координат; у кнопки без этих паттернов остаётся клик мышью, а при пустом прямоугольнике он невозможен; не найдена — панель сворачивается (<see cref="CollapsePanel"/>: по её окну, не по Toggle). Найденная в свёрнутой панели кнопка
        /// возвращается с раскрытой панелью: <see cref="Click"/> нажимает её сразу, а после голого Button() вызывающий сам сворачивает панель.
        /// </summary>
        public AutomationElement Button(string tab, string button)
        {
            Require(button, nameof(button));
            string tabId = Activate(tab).AutomationId;
            string what = $"кнопка ленты «{CaptionText.Normalize(button)}» на вкладке «{tab}»";
            ButtonLookup lookup = Wait.ForStrict(() => Look(tabId, button), Wait.DefaultTimeout, what, () => "видимые кнопки вкладки: " + string.Join(", ", VisibleCaptions(tabId)));
            return lookup.Button ?? FindInCollapsedPanels(tabId, button, lookup.CollapsedPanels, what);
        }

        /// <summary>Нажать кнопку ленты один раз (результат проверяет вызывающий: окно, панель, состояние модели).</summary>
        public void Click(string tab, string button)
        {
            UiAct.Press(Button(tab, button), $"кнопка ленты «{CaptionText.Normalize(button)}» (вкладка «{tab}»)");
            ParkMouse($"после кнопки ленты «{CaptionText.Normalize(button)}»");
        }

        /// <summary>
        /// Раскрыть выпадающую кнопку (у «Additional Settings» только Toggle) и нажать пункт меню по имени или подписи. Меню — окно класса
        /// <see cref="RevitIds.Dialogs.PopupClass"/>, пункт — Button с Invoke (Evidence/ribbon-pulldown-2022.txt). Пункт ищется только в окнах,
        /// появившихся после раскрытия; несколько совпадений — <see cref="AmbiguousElementException"/>. Оставшееся раскрытым меню этой кнопки
        /// сначала сворачивается; пункт не найден — меню сворачивается возвратом Toggle, ошибка пробрасывается.
        /// </summary>
        public void ClickInMenu(string tab, string pulldown, string item)
        {
            Require(item, nameof(item));
            string menuName = $"выпадающая кнопка «{CaptionText.Normalize(pulldown)}»";
            string what = $"пункт «{CaptionText.Normalize(item)}» меню «{CaptionText.Normalize(pulldown)}»";
            AutomationElement menu = Button(tab, pulldown);
            CloseMenu(menu, menuName + ": закрыть оставшееся раскрытым меню");
            HashSet<IntPtr> before = PopupHandles();
            Open(menu, menuName);
            AutomationElement entry;
            try
            {
                entry = Wait.ForStrict(() => RibbonMatch.Single(RibbonMatch.FindShownButtons(NewPopups(before), item), what), Wait.DefaultTimeout, what,
                    () => "новых всплывающих окон ленты: " + NewPopups(before).Count);
            }
            catch (Exception ex)
            {
                AfterFailure(() => CloseMenu(menu, menuName + ": закрыть после сбоя («" + ex.Message + "»)"), menuName, ex);
                throw;
            }
            UiAct.Press(entry, what + $" (вкладка «{tab}»)");
            ParkMouse("после " + what);
        }

        /// <summary>Курсор — в центр клиентской области главного окна Revit, подальше от ленты (см. summary класса).</summary>
        private void ParkMouse(string why)
        {
            UiAct.ParkMouse(_app.MainWindowHandle(), _app.ProcessId, why);
        }

        /// <summary>Корень ленты (<see cref="RevitIds.Ribbon.Root"/>; null — главное окно).</summary>
        internal AutomationElement RibbonRoot()
        {
            if (RevitIds.Ribbon.Root == null)
            {
                return _app.MainWindow;
            }
            return UiQuery.WaitFind(() => _app.MainWindow, RevitIds.Ribbon.Root, Wait.DefaultTimeout, "главное окно Revit", RevitIds.Ribbon.SearchDepth);
        }

        /// <summary>Содержимое вкладки по её AutomationId («&lt;id&gt;_PanelBarScrollViewer»); null — нет.</summary>
        internal AutomationElement TabContent(string tabId)
        {
            return UiQuery.TryFind(RibbonRoot(), By.Id(tabId + RevitIds.Ribbon.TabContentSuffix), Wait.DefaultTimeout, RevitIds.Ribbon.TabContentDepth);
        }

        /// <summary>Раскрыть: ExpandCollapse, если он есть; иначе одно нажатие (Invoke, Toggle, мышь).</summary>
        internal void Open(AutomationElement element, string description)
        {
            IExpandCollapsePattern expand = UiQuery.Read(() => element.Patterns.ExpandCollapse.PatternOrDefault, Wait.DefaultTimeout, "ExpandCollapse " + description);
            if (expand != null)
            {
                UiActions.Expand(element, description);
                return;
            }
            UiAct.Press(element, description);
        }

        /// <summary>
        /// Найти вкладку и активировать: у вкладок AdWindows нет SelectionItem (только Invoke), активность — содержимое вкладки на экране.
        /// Активная вкладка не нажимается.
        /// </summary>
        private RibbonTab Activate(string name)
        {
            Require(name, nameof(name));
            string what = $"вкладка ленты «{name}»";
            RibbonTab tab = Wait.ForStrict(() => Single(name), Wait.DefaultTimeout, what, () => "вкладки: " + string.Join(", ", Tabs()));
            if (tab.IsActive)
            {
                return tab;
            }
            UiAct.Press(tab.Element, what);
            return Wait.ForStrict(() =>
            {
                RibbonTab now = Single(name);
                return now != null && now.IsActive ? now : null;
            }, Wait.DefaultTimeout, what + " активна (её панели на экране)", ActiveTabs);
        }

        private RibbonTab Single(string name)
        {
            List<RibbonTab> tabs = RibbonMatch.FindTabs(RibbonRoot(), name);
            if (tabs.Count > 1)
            {
                throw new AmbiguousElementException($"вкладка ленты «{name}»", tabs.Select(t => t.ToString()).ToList());
            }
            return tabs.FirstOrDefault();
        }

        private string ActiveTabs()
        {
            return "активные вкладки: " + string.Join(", ", RibbonMatch.FindTabs(RibbonRoot(), null).Where(t => t.IsActive).Select(t => t.ToString()));
        }

        /// <summary>Только чтение: видимая кнопка в содержимом вкладки или показанные свёрнутые панели вкладки; ни того ни другого — null (ждать дальше).</summary>
        private ButtonLookup Look(string tabId, string button)
        {
            AutomationElement content = TabContent(tabId);
            if (content == null)
            {
                return null;
            }
            AutomationElement visible = RibbonMatch.Single(RibbonMatch.FindShownButtons(new[] { content }, button),
                $"кнопка ленты «{CaptionText.Normalize(button)}» (уточните внутренним именем PushButtonData)");
            if (visible != null)
            {
                return new ButtonLookup(visible, null);
            }
            if (RevitIds.Ribbon.CollapsedPanel == null)
            {
                return null;
            }
            List<AutomationElement> panels = UiQuery.FindAllComplete(content, RevitIds.Ribbon.CollapsedPanel, Wait.DefaultTimeout, RevitIds.Ribbon.SearchDepth)
                .Where(RibbonMatch.IsShown).ToList();
            return panels.Count > 0 ? new ButtonLookup(null, panels) : null;
        }

        /// <summary>Раскрыть каждую свёрнутую панель один раз и искать кнопку только в окне, появившемся после этого раскрытия.</summary>
        private AutomationElement FindInCollapsedPanels(string tabId, string button, List<AutomationElement> panels, string what)
        {
            DateTime started = DateTime.UtcNow;
            List<string> tried = new List<string>();
            foreach (AutomationElement panel in panels)
            {
                string panelName = $"свёрнутая панель ленты «{UiQuery.Read(() => panel.Properties.AutomationId.ValueOrDefault ?? "", Wait.DefaultTimeout, "AutomationId свёрнутой панели")}»";
                tried.Add(panelName);
                HashSet<IntPtr> before = PopupHandles();
                Open(panel, panelName);
                AutomationElement found;
                bool appeared = false;
                try
                {
                    found = WaitOrNull(() =>
                    {
                        // только окно, появившееся после этого раскрытия, и без отбора по экрану (RibbonMatch.FindPanelWindowButtons);
                        // «занят» — повтор опроса, а в конце AppBusyException; несколько совпадений — AmbiguousElementException
                        List<KeyValuePair<IntPtr, AutomationElement>> fresh = NewPopupWindows(before);
                        if (!appeared && fresh.Count > 0)
                        {
                            // отметка времени в журнале шагов (без дополнительных чтений): окно панели живёт ограниченное время
                            appeared = true;
                            UiLog.Action(panelName + ": окно панели появилось (" + string.Join(", ", fresh.Select(w => "hwnd " + w.Key)) + ")");
                        }
                        return RibbonMatch.Single(RibbonMatch.FindPanelWindowButtons(fresh.Select(w => w.Value), button), what + " в " + panelName);
                    }, PanelPopupTimeout, what + " в " + panelName);
                }
                catch (Exception ex)
                {
                    AfterFailure(() => CollapsePanel(panel, before, panelName + ": свернуть после сбоя («" + ex.Message + "»)"), panelName, ex);
                    throw;
                }
                if (found != null)
                {
                    UiLog.Action(what + ": найдена в окне " + panelName);
                    return found;
                }
                CollapsePanel(panel, before, panelName + ": свернуть (кнопки в ней нет)");
            }
            throw new UiWaitTimeoutException(what, Wait.DefaultTimeout, DateTime.UtcNow - started,
                "видимой кнопки нет; раскрыты и свёрнуты обратно: " + string.Join(", ", tried) + "; видимые кнопки вкладки: " + string.Join(", ", VisibleCaptions(tabId)), null);
        }

        /// <summary>Строгое ожидание без отказа по таймауту: не дождались — null; «занят» до конца — <see cref="AppBusyException"/>.</summary>
        private static AutomationElement WaitOrNull(Func<AutomationElement> producer, TimeSpan timeout, string description)
        {
            try
            {
                return Wait.ForStrict(producer, timeout, description, null);
            }
            catch (UiWaitTimeoutException ex) when (!(ex is AppBusyException))
            {
                // «не появилось за timeout» здесь — ответ («кнопки в этой панели нет»), а не сбой: вызывающий сворачивает панель и идёт дальше
                UiLog.Action(description + ": нет за " + timeout.TotalSeconds + " с");
                return null;
            }
        }

        /// <summary>
        /// Свернуть свёрнутую панель, раскрытую после снимка <paramref name="before"/> (<see cref="PopupHandles"/> перед раскрытием). Раскрыта — значит
        /// открыто её окно: всплывающие окна ленты, которых не было в before (берутся в начале сворачивания); Toggle как признак не годится — у Project
        /// Location окно было открыто при Toggle Off (Evidence/ribbon-collapsed-2022.txt, прогон 6). Шаги — <see cref="RibbonMatch.NextCollapseStep"/>:
        /// окно открыто — один Toggle и строгое ограниченное ожидание закрытия; не закрылось — один Esc в переднее окно Revit и снова ожидание;
        /// не закрылось и тогда — <see cref="UiWaitTimeoutException"/> с описанием окна. Действий внутри ожиданий нет, повторов нет.
        /// </summary>
        internal void CollapsePanel(AutomationElement panel, HashSet<IntPtr> before, string description)
        {
            HashSet<IntPtr> windows = new HashSet<IntPtr>(PopupWindows().Where(w => !before.Contains(w.Key)).Select(w => w.Key));
            DateTime started = DateTime.UtcNow;
            bool toggled = false;
            bool escaped = false;
            bool open = IsAnyOpen(windows);
            while (true)
            {
                switch (RibbonMatch.NextCollapseStep(open, toggled, escaped))
                {
                    case PanelCollapseStep.Done:
                        UiLog.Action(description + (toggled || escaped ? ": окно панели закрыто" : ": окна панели нет — сворачивать нечего"));
                        return;
                    case PanelCollapseStep.Toggle:
                        ITogglePattern toggle = UiQuery.Read(() => panel.Patterns.Toggle.PatternOrDefault, Wait.DefaultTimeout, "TogglePattern " + description);
                        if (toggle == null)
                        {
                            throw new InvalidOperationException(description + ": у свёрнутой панели нет паттерна Toggle — свернуть её нечем.");
                        }
                        UiAct.RunOnce(panel, description, toggle.Toggle, "Toggle");
                        toggled = true;
                        break;
                    case PanelCollapseStep.Escape:
                        UiLog.Warn(description + ": окно панели не закрылось за " + PanelCloseTimeout.TotalSeconds + " с после Toggle — Esc");
                        UiAct.KeysToForeground(_app.ProcessId, "Esc: " + description, VirtualKeyShort.ESCAPE);
                        escaped = true;
                        break;
                    default:
                        throw new UiWaitTimeoutException(description + ": окно панели закрыто", PanelCloseTimeout, DateTime.UtcNow - started,
                            "окно панели осталось открытым после одного Toggle и одного Esc: " + string.Join(", ", windows.Select(h => "hwnd " + h)), null);
                }
                // строгое ограниченное ожидание без действий: «занят» до конца — AppBusyException из строгого чтения окон
                open = !Wait.Holds(() => !IsAnyOpen(windows), PanelCloseTimeout);
            }
        }

        /// <summary>Хотя бы одно из окон windows — всё ещё видимое всплывающее окно ленты.</summary>
        private bool IsAnyOpen(HashSet<IntPtr> windows)
        {
            return windows.Count > 0 && PopupHandles().Overlaps(windows);
        }

        /// <summary>
        /// Закрыть раскрытое меню выпадающей кнопки: Toggle On — одно нажатие Esc в переднее окно процесса Revit, затем ждать Toggle не On.
        /// Возврат Toggle меню не закрывает (Evidence/ribbon-pulldown-2022.txt, прогон 4: Toggle «Additional Settings» за 10 с не вернулся в Off),
        /// Esc закрывает (прогон 3: Toggle вернулся в Off).
        /// </summary>
        private void CloseMenu(AutomationElement menu, string description)
        {
            ITogglePattern toggle = UiQuery.Read(() => menu.Patterns.Toggle.PatternOrDefault, Wait.DefaultTimeout, "TogglePattern " + description);
            if (toggle == null || UiQuery.Read(() => toggle.ToggleState.Value, Wait.DefaultTimeout, "ToggleState " + description) != ToggleState.On)
            {
                return;
            }
            UiAct.KeysToForeground(_app.ProcessId, "Esc: " + description, VirtualKeyShort.ESCAPE);
            Wait.Strict(() => UiQuery.Read(() => toggle.ToggleState.Value, Wait.DefaultTimeout, "ToggleState " + description) != ToggleState.On,
                Wait.DefaultTimeout, description + ": меню закрыто (Toggle не On)", null);
        }

        /// <summary>Свернуть или закрыть после сбоя поиска; сбой этого действия не заслоняет исходную ошибку — он в журнале.</summary>
        private static void AfterFailure(Action restore, string description, Exception original)
        {
            try
            {
                restore();
            }
            catch (Exception ex)
            {
                // исходная ошибка важнее: её пробрасывает вызывающий; сбой возврата виден в журнале шагов
                UiLog.Warn(description + ": не возвращено после сбоя «" + original.Message + "» — " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>HWND всплывающих окон ленты (<see cref="PopupWindows"/>): снимок перед раскрытием для <see cref="CollapsePanel"/>.</summary>
        internal HashSet<IntPtr> PopupHandles()
        {
            return new HashSet<IntPtr>(PopupWindows().Select(w => w.Key));
        }

        /// <summary>Всплывающие окна ленты, которых не было в before (окно, появившееся после раскрытия).</summary>
        private List<AutomationElement> NewPopups(HashSet<IntPtr> before)
        {
            return NewPopupWindows(before).Select(w => w.Value).ToList();
        }

        /// <summary>Всплывающие окна ленты (HWND и элемент), которых не было в before.</summary>
        private List<KeyValuePair<IntPtr, AutomationElement>> NewPopupWindows(HashSet<IntPtr> before)
        {
            return PopupWindows().Where(w => !before.Contains(w.Key)).ToList();
        }

        /// <summary>
        /// Видимые окна процесса Revit (EnumWindows), у которых ClassName в UI Automation — <see cref="RevitIds.Dialogs.PopupClass"/>. Класс Win32
        /// у них другой (окно WPF), поэтому отбор — по UIA после FromHandle (прогон 4: отбор по классу Win32 не нашёл ни одного окна).
        /// Окно, закрывшееся до запроса UI Automation, пропускается.
        /// </summary>
        private List<KeyValuePair<IntPtr, AutomationElement>> PopupWindows()
        {
            List<KeyValuePair<IntPtr, AutomationElement>> popups = new List<KeyValuePair<IntPtr, AutomationElement>>();
            foreach (NativeWindowInfo window in RevitNative.TopLevelWindows(_app.ProcessId).Where(w => w.IsVisible))
            {
                try
                {
                    AutomationElement element = UiQuery.Read(() => _app.Automation.FromHandle(window.Handle), Wait.DefaultTimeout, "окно Revit " + window.Describe());
                    if (RibbonMatch.IsPopup(element))
                    {
                        popups.Add(new KeyValuePair<IntPtr, AutomationElement>(window.Handle, element));
                    }
                }
                catch (Exception ex) when (UiFault.Classify(ex) == UiFaultKind.Gone || !RevitNative.IsWindow(window.Handle))
                {
                    // окно закрылось между EnumWindows и запросом UI Automation — его нет
                    continue;
                }
            }
            return popups;
        }

        /// <summary>
        /// Всплывающие окна ленты: видимые окна процесса Revit с ClassName UIA <see cref="RevitIds.Dialogs.PopupClass"/> по EnumWindows. Меню
        /// выпадающей кнопки и свёрнутой панели среди детей рабочего стола в UI Automation нет и в <see cref="RevitWindows.Popups"/> не попадают
        /// (Evidence/ribbon-pulldown-2022.txt, ribbon-collapsed-2022.txt), поэтому перечисляются здесь.
        /// </summary>
        internal List<AutomationElement> RibbonPopups()
        {
            return PopupWindows().Select(w => w.Value).ToList();
        }

        private IReadOnlyList<string> VisibleCaptions(string tabId)
        {
            AutomationElement content = TabContent(tabId);
            if (content == null)
            {
                return new[] { $"(содержимого вкладки «{tabId}» нет)" };
            }
            By anyButton = By.Where(f => RevitIds.Ribbon.ButtonTypes.Contains(f.ControlType) && f.Name.Length > 0, "кнопки ленты");
            return UiQuery.FindAllComplete(content, anyButton, Wait.DefaultTimeout, RevitIds.Ribbon.SearchDepth)
                .Where(b => !b.SafeIsOffscreen()).Select(b => CaptionText.Normalize(b.SafeName())).Distinct().Take(60).ToList();
        }

        private static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Нужна подпись или имя элемента ленты.", name);
            }
        }

        /// <summary>Итог чтения вкладки: видимая кнопка или показанные свёрнутые панели.</summary>
        private sealed class ButtonLookup
        {
            internal ButtonLookup(AutomationElement button, List<AutomationElement> collapsedPanels)
            {
                Button = button;
                CollapsedPanels = collapsedPanels;
            }

            internal AutomationElement Button { get; }

            internal List<AutomationElement> CollapsedPanels { get; }
        }
    }
}
