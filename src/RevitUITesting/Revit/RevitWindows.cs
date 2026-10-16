using System;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using RevitUITesting.Uia;

namespace RevitUITesting.Revit
{
    /// <summary>Что сделала уборка окон Revit и что осталось открытым.</summary>
    public sealed class UiResetReport
    {
        internal UiResetReport(IReadOnlyList<string> actions, IReadOnlyList<string> revitErrors, IReadOnlyList<string> remaining)
        {
            Actions = actions;
            RevitErrors = revitErrors;
            Remaining = remaining;
        }

        public IReadOnlyList<string> Actions { get; }

        /// <summary>Тексты закрытых ошибок и предупреждений Revit (для отчёта теста, тест из-за них не падает).</summary>
        public IReadOnlyList<string> RevitErrors { get; }

        /// <summary>Что не удалось убрать: неизвестное модальное окно, окно из каталога с действием Fail, недоступное главное окно.</summary>
        public IReadOnlyList<string> Remaining { get; }

        public bool Clean => Remaining.Count == 0;

        public override string ToString()
        {
            return (Clean ? "окна убраны" : "осталось: " + string.Join("; ", Remaining)) + (Actions.Count == 0 ? "" : "; сделано: " + string.Join("; ", Actions));
        }
    }

    /// <summary>
    /// Окна процесса Revit верхнего уровня, кроме главного (без повторов по HWND, только видимые): диалоги Revit и плагинов, TaskDialog,
    /// плавающие панели. Всплывающие окна (класс <see cref="RevitIds.Dialogs.PopupClass"/>) — отдельно. Модальное состояние определяется по
    /// недоступности главного окна (IsWindowEnabled).
    /// </summary>
    public sealed class RevitWindows
    {
        /// <summary>Сколько ждать закрытия TaskDialog после одного TDM_CLICK_BUTTON.</summary>
        private static readonly TimeSpan TaskDialogCloseTimeout = TimeSpan.FromSeconds(5);

        private readonly AutomationBase _automation;
        private readonly int _processId;
        private readonly Func<IntPtr> _mainWindowHandle;

        internal RevitWindows(AutomationBase automation, int processId, Func<IntPtr> mainWindowHandle, IReadOnlyList<KnownDialog> catalog)
        {
            _automation = automation ?? throw new ArgumentNullException(nameof(automation));
            _processId = processId;
            _mainWindowHandle = mainWindowHandle ?? throw new ArgumentNullException(nameof(mainWindowHandle));
            Catalog = catalog ?? RevitDialogs.BuiltIn;
        }

        public IReadOnlyList<KnownDialog> Catalog { get; }

        /// <summary>Главное окно Revit принимает ввод; false — открыто модальное окно (или главного окна нет).</summary>
        public bool IsMainWindowEnabled
        {
            get
            {
                IntPtr main = _mainWindowHandle();
                return main != IntPtr.Zero && RevitNative.IsWindowEnabled(main);
            }
        }

        public IReadOnlyList<AutomationElement> All()
        {
            return Infos(false).Where(i => !i.IsPopup).Select(i => i.Element).ToList();
        }

        public IReadOnlyList<AutomationElement> Popups()
        {
            return Infos(false).Where(i => i.IsPopup).Select(i => i.Element).ToList();
        }

        /// <summary>Выполнить действие и вернуть окно, которого до этого не было (по HWND); больше одного — неоднозначность.</summary>
        public AutomationElement OpenedBy(Action open, By selector = null, TimeSpan? timeout = null)
        {
            if (open == null)
            {
                throw new ArgumentNullException(nameof(open));
            }
            HashSet<IntPtr> before = new HashSet<IntPtr>(Infos(false).Select(i => i.Handle));
            open();
            By match = selector ?? By.Type(ControlType.Window);
            return Wait.ForStrict(() =>
            {
                List<DialogInfo> fresh = Infos(false).Where(i => !i.IsPopup && !before.Contains(i.Handle) && match.Matches(new ElementFacts(i.Element))).ToList();
                if (fresh.Count > 1)
                {
                    throw new AmbiguousElementException("новое окно Revit " + match, fresh.Select(f => f.Describe()).ToList());
                }
                return fresh.Count == 1 ? fresh[0].Element : null;
            }, timeout ?? Wait.DefaultTimeout, "новое окно Revit " + match, DescribeOnce);
        }

        public void WaitGone(AutomationElement window, TimeSpan? timeout = null)
        {
            IntPtr handle = UiQuery.Read(() => window.Properties.NativeWindowHandle.ValueOrDefault, Wait.DefaultTimeout, "HWND окна Revit");
            string title = window.SafeName();
            Wait.Strict(() => !Native.IsShown(handle), timeout ?? Wait.DefaultTimeout, $"закрытие окна Revit «{title}»", DescribeOnce);
        }

        public void WaitGone(string title, TimeSpan? timeout = null)
        {
            Wait.Strict(() => Infos(false).All(i => i.IsPopup || i.Title != title), timeout ?? Wait.DefaultTimeout, $"закрытие окна Revit «{title}»", DescribeOnce);
        }

        /// <summary>Открытый TaskDialog (окно класса <see cref="RevitIds.Dialogs.TaskDialogClass"/>); нет — null; больше одного — неоднозначность.</summary>
        public RevitTaskDialog TryTaskDialog()
        {
            List<DialogInfo> dialogs = Infos(false).Where(i => !i.IsPopup && i.ClassName == RevitIds.Dialogs.TaskDialogClass).ToList();
            if (dialogs.Count > 1)
            {
                throw new AmbiguousElementException("TaskDialog Revit", dialogs.Select(d => d.Describe()).ToList());
            }
            return dialogs.Count == 1 ? new RevitTaskDialog(dialogs[0].Element) : null;
        }

        public RevitTaskDialog WaitTaskDialog(TimeSpan? timeout = null)
        {
            return Wait.ForStrict(TryTaskDialog, timeout ?? Wait.DefaultTimeout, "TaskDialog Revit", DescribeOnce);
        }

        /// <summary>Открытые окна ошибок и предупреждений Revit («заголовок: текст»).</summary>
        public IReadOnlyList<string> RevitErrors()
        {
            return Infos(true).Where(i => !i.IsPopup && ResetPlanner.Match(Catalog, i)?.IsRevitError == true).Select(i => $"«{i.Title}»: {i.Text}").ToList();
        }

        /// <summary>Заголовки, классы и тексты открытых окон Revit (для артефакта revit-dialogs.txt).</summary>
        public string DescribeDialogs()
        {
            List<DialogInfo> infos = Infos(true);
            return infos.Count == 0 ? "окон Revit, кроме главного, нет" : string.Join(Environment.NewLine + Environment.NewLine, infos.Select(i => (i.IsPopup ? "всплывающее " : "") + i.Describe()));
        }

        /// <summary>
        /// Убрать то, что осталось от упавшего теста: сначала TaskDialog каталога с действием TaskDialogButton, найденные через user32
        /// (<see cref="AnswerTaskDialogs"/>); затем всплывающие окна — Esc; окна каталога — по действию (тексты ошибок Revit — в отчёт);
        /// неизвестное модальное окно, запись Fail или недоступное главное окно — в Remaining (окно не трогается). До 10 кругов.
        /// </summary>
        public UiResetReport ResetUi()
        {
            List<string> actions = new List<string>();
            List<string> errors = new List<string>();
            List<string> remaining = new List<string>();
            // в журнал ответы попадут из Report вместе с остальными действиями уборки
            actions.AddRange(PressTaskDialogs());
            HashSet<IntPtr> toolTips = new HashSet<IntPtr>();
            for (int round = 0; round < 10; round++)
            {
                List<DialogInfo> infos = Infos(true);
                // подсказка ленты не модальная и по Esc не закрывается: уборка её не трогает, в отчёт — один раз за уборку
                foreach (DialogInfo tip in infos.Where(i => i.IsRibbonToolTip && toolTips.Add(i.Handle)))
                {
                    actions.Add("подсказка ленты не мешает, оставлена: " + tip.Describe());
                }
                ResetStep step = ResetPlanner.Next(infos, IsMainWindowEnabled, Catalog);
                DialogInfo window = step.Window;
                switch (step.Kind)
                {
                    case ResetStepKind.Done:
                        return Report(actions, errors, remaining);
                    case ResetStepKind.Escape:
                        UiAct.KeysToForeground(_processId, "Esc: всплывающее окно Revit " + window.Describe(), VirtualKeyShort.ESCAPE);
                        if (!Wait.Holds(() => !Native.IsShown(window.Handle), TimeSpan.FromSeconds(3)))
                        {
                            remaining.Add("всплывающее окно не закрылось по Esc: " + window.Describe());
                            return Report(actions, errors, remaining);
                        }
                        actions.Add("Esc: всплывающее окно " + window.Describe());
                        break;
                    case ResetStepKind.Answer:
                        new RevitTaskDialog(window.Element).Answer(step.Known.AnswerCaption);
                        actions.Add($"{window.Describe()} ({step.Known.Name}): «{step.Known.AnswerCaption}»");
                        Note(step, window, errors);
                        break;
                    case ResetStepKind.TaskDialogButton:
                        string target = $"{window.Describe()} ({step.Known.Name})";
                        if (PressTaskDialogButton(window.Handle, step.Known, target))
                        {
                            actions.Add(TaskDialogAction(step.Known, target));
                            Note(step, window, errors);
                        }
                        break;
                    case ResetStepKind.Close:
                        if (!RevitNative.PostClose(window.Handle, window.Describe()))
                        {
                            actions.Add($"окно закрылось до WM_CLOSE: {window.Describe()} ({step.Known.Name})");
                            break;
                        }
                        if (!Wait.Holds(() => !Native.IsShown(window.Handle), TimeSpan.FromSeconds(10)))
                        {
                            remaining.Add($"окно не закрылось за 10 с после WM_CLOSE ({step.Known.Name}): {window.Describe()}");
                            return Report(actions, errors, remaining);
                        }
                        actions.Add($"закрыто {window.Describe()} ({step.Known.Name})");
                        Note(step, window, errors);
                        break;
                    case ResetStepKind.Fail:
                        remaining.Add($"{window.Describe()} — {step.Known.Hint}");
                        return Report(actions, errors, remaining);
                    case ResetStepKind.UnknownModal:
                        remaining.Add("неизвестное модальное окно " + window.Describe());
                        return Report(actions, errors, remaining);
                    case ResetStepKind.MainDisabled:
                        // модальное окно, которого нет в дереве UIA, может быть TaskDialog каталога, появившийся уже во время уборки
                        List<string> pressed = PressTaskDialogs();
                        actions.AddRange(pressed);
                        if (pressed.Count > 0 || Wait.Holds(() => IsMainWindowEnabled, TimeSpan.FromSeconds(3)))
                        {
                            break;
                        }
                        remaining.Add("главное окно Revit недоступно, а модального окна процесса в UI Automation нет");
                        return Report(actions, errors, remaining);
                    default:
                        throw new InvalidOperationException("Неизвестный шаг уборки окон: " + step.Kind);
                }
            }
            remaining.Add("после 10 кругов уборки окна Revit продолжают появляться: " + DescribeOnce());
            return Report(actions, errors, remaining);
        }

        /// <summary>
        /// Нажать кнопки TaskDialog процесса Revit, записанные в каталоге с действием <see cref="KnownDialogAction.TaskDialogButton"/>
        /// (например, «Always Load» в «Security - Unsigned Add-In»). Окна ищутся через user32 (EnumWindows: процесс, класс
        /// <see cref="RevitIds.Dialogs.TaskDialogClass"/>, заголовок), а не через UI Automation: такой диалог не виден от корня дерева UIA,
        /// а у его кнопок нет паттерна Invoke. Кнопка нажимается сообщением TDM_CLICK_BUTTON; окно не закрылось за 5 с — ещё одно сообщение,
        /// снова не закрылось — InvalidOperationException. Возвращает описания отвеченных окон (закрывшееся до ответа окно в перечень не входит);
        /// каждый ответ пишется в журнал шагов.
        /// </summary>
        internal IReadOnlyList<string> AnswerTaskDialogs()
        {
            List<string> answered = PressTaskDialogs();
            foreach (string answer in answered)
            {
                UiLog.Action(answer);
            }
            return answered;
        }

        /// <summary>То же, что <see cref="AnswerTaskDialogs"/>, без записи ответов в журнал (их пишет вызывающий — уборка пишет в Report).</summary>
        private List<string> PressTaskDialogs()
        {
            List<string> answered = new List<string>();
            foreach ((NativeWindowInfo window, KnownDialog known) in ResetPlanner.TaskDialogTargets(RevitNative.TopLevelWindows(_processId), _processId, Catalog))
            {
                string target = $"{window.Describe()} ({known.Name})";
                if (PressTaskDialogButton(window.Handle, known, target))
                {
                    answered.Add(TaskDialogAction(known, target));
                }
            }
            return answered;
        }

        /// <summary>
        /// Ответить на открытое окно записи каталога по её действию (Answer — кнопка по подписи, TaskDialogButton — TDM_CLICK_BUTTON по id,
        /// только окну класса TaskDialog — <see cref="ResetPlanner.Applies"/>); подходящего окна нет — false. Ответ пишется в журнал шагов. Запись с другим действием — InvalidOperationException (здесь только ответы).
        /// </summary>
        internal bool AnswerKnown(KnownDialog known)
        {
            DialogInfo info = Infos(true).FirstOrDefault(i => !i.IsPopup && ResetPlanner.Applies(known, i));
            if (info == null)
            {
                return false;
            }
            switch (known.Action)
            {
                case KnownDialogAction.Answer:
                    new RevitTaskDialog(info.Element).Answer(known.AnswerCaption);
                    return true;
                case KnownDialogAction.TaskDialogButton:
                    string target = $"{info.Describe()} ({known.Name})";
                    if (PressTaskDialogButton(info.Handle, known, target))
                    {
                        UiLog.Action(TaskDialogAction(known, target));
                    }
                    return true;
                default:
                    throw new InvalidOperationException($"Запись каталога «{known.Name}» ({known.Action}) — не ответ на окно: отвечать можно только записями {nameof(KnownDialogAction.Answer)} и {nameof(KnownDialogAction.TaskDialogButton)}.");
            }
        }

        /// <summary>Открытое окно каталога с действием Fail («заголовок: текст — подсказка»); нет — null.</summary>
        internal string KnownFailure()
        {
            foreach (DialogInfo info in Infos(true).Where(i => !i.IsPopup))
            {
                KnownDialog known = ResetPlanner.Match(Catalog, info);
                if (known != null && known.Action == KnownDialogAction.Fail)
                {
                    return $"{info.Describe()} — {known.Hint}";
                }
            }
            return null;
        }

        /// <summary>Окна Revit одним проходом без повторов (для сообщений об ошибках: не ждёт занятый Revit).</summary>
        internal string DescribeOnce()
        {
            try
            {
                IntPtr main = _mainWindowHandle();
                List<string> parts = new List<string>();
                foreach (AutomationElement window in _automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(_processId)))
                {
                    IntPtr handle = window.Properties.NativeWindowHandle.ValueOrDefault;
                    if (handle == main || !Native.IsShown(handle))
                    {
                        continue;
                    }
                    parts.Add($"«{window.Properties.Name.ValueOrDefault}» ({window.Properties.ClassName.ValueOrDefault})");
                }
                string state = main == IntPtr.Zero ? "главное окно Revit не найдено" : RevitNative.IsWindowEnabled(main) ? "главное окно доступно" : "главное окно недоступно (открыто модальное окно)";
                return state + "; " + (parts.Count == 0 ? "других окон нет" : "открыты окна: " + string.Join("; ", parts));
            }
            catch (Exception ex) when (UiFault.IsTransient(ex))
            {
                // описание для сообщения об ошибке: занятый или закрывшийся Revit не должен подменить исходную ошибку новой
                return "окна Revit не перечислены: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>
        /// Окна процесса: дети рабочего стола с pid Revit и окна, которые UI Automation показывает прямыми потомками главного окна. Окно
        /// с владельцем (модальный диалог «Project Units», #32770) UI Automation показывает под владельцем, и среди детей рабочего стола
        /// его нет (задача 11, Evidence/project-units-2022.txt). Всплывающие окна (<see cref="RevitIds.Dialogs.PopupClass"/>) под главным окном
        /// не берутся: не наблюдались. EnumWindows не подходит: в Revit 2022 на стартовой странице он даёт видимое окно класса «Popup»
        /// без заголовка, которого нет в UI Automation и которое не закрывается по Esc (задача 11, прогон 2).
        /// </summary>
        internal List<DialogInfo> Infos(bool withTexts)
        {
            IntPtr main = _mainWindowHandle();
            // окна главного окна — первыми: при модальном состоянии уборка и её отчёт берут первое неизвестное окно, и это должен быть
            // модальный диалог, а не всегда открытое окно «Family» (Afx, за экраном) среди детей рабочего стола
            List<AutomationElement> top = main != IntPtr.Zero ? OwnedByMain(main) : new List<AutomationElement>();
            top.AddRange(UiQuery.Read(() => _automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(_processId)), Wait.DefaultTimeout, "окна процесса Revit"));
            return Collect(top, main, Native.IsShown, withTexts);
        }

        /// <summary>Окна, которые UI Automation показывает под главным окном (<see cref="OwnedDialogs"/>); главное окно закрылось — нет.</summary>
        private List<AutomationElement> OwnedByMain(IntPtr main)
        {
            try
            {
                AutomationElement mainWindow = UiQuery.Read(() => _automation.FromHandle(main), Wait.DefaultTimeout, "главное окно Revit по HWND");
                return OwnedDialogs(mainWindow);
            }
            catch (Exception ex) when (UiFault.Classify(ex) == UiFaultKind.Gone || !RevitNative.IsWindow(main))
            {
                // главное окно закрылось во время перечня — окон с этим владельцем больше нет
                return new List<AutomationElement>();
            }
        }

        /// <summary>
        /// Прямые потомки главного окна типа Window, кроме всплывающих (<see cref="RevitIds.Dialogs.PopupClass"/>): окна, у которых владелец —
        /// главное окно. Известное ограничение: окно, у которого владелец — такой диалог (диалог из «Project Units», окно плагина из его окна),
        /// UI Automation показывает глубже, под его владельцем, — сюда оно не попадает.
        /// </summary>
        internal static List<AutomationElement> OwnedDialogs(AutomationElement mainWindow)
        {
            By owned = By.Where(f => f.ControlType == ControlType.Window && f.ClassName != RevitIds.Dialogs.PopupClass, "окна с владельцем — главным окном Revit");
            return UiQuery.FindAllComplete(mainWindow, owned, Wait.DefaultTimeout, 1);
        }

        /// <summary>
        /// Окна верхнего уровня, кроме главного, скрытых и повторов (по HWND). HWND читается первым, и заголовок, класс и тексты главного окна
        /// не читаются: тексты — обход всего дерева, а у главного окна живого Revit 2022 с открытой моделью он занимает десятки секунд
        /// (задача 10: подхват ждал 40 с, Quit — 20 с до WM_CLOSE).
        /// </summary>
        internal static List<DialogInfo> Collect(IEnumerable<AutomationElement> top, IntPtr main, Func<IntPtr, bool> isShown, bool withTexts)
        {
            HashSet<IntPtr> seen = new HashSet<IntPtr>();
            List<DialogInfo> result = new List<DialogInfo>();
            foreach (AutomationElement window in top)
            {
                try
                {
                    IntPtr handle = UiQuery.Read(() => window.Properties.NativeWindowHandle.ValueOrDefault, Wait.DefaultTimeout, "HWND окна Revit");
                    if (handle == IntPtr.Zero || handle == main || !isShown(handle) || !seen.Add(handle))
                    {
                        continue;
                    }
                    result.Add(Read(window, handle, withTexts));
                }
                catch (Exception ex) when (UiFault.Classify(ex) == UiFaultKind.Gone)
                {
                    // окно закрылось во время перечня — в перечень не входит
                    continue;
                }
            }
            return result;
        }

        /// <summary>
        /// TDM_CLICK_BUTTON с id кнопки записи каталога и ожидание закрытия окна (до двух сообщений). false — окно закрылось до первого сообщения
        /// (ответил пользователь или другой обработчик), в том числе между проверкой и PostMessage; true — окно закрылось после сообщения
        /// (если оно закрылось перед вторым сообщением — ответом считается первое); не закрылось после двух — InvalidOperationException.
        /// Ответ в журнал пишет вызывающий, здесь — только «закрылось до ответа» и предупреждение о повторе.
        /// </summary>
        private static bool PressTaskDialogButton(IntPtr handle, KnownDialog known, string target)
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                bool posted = Native.IsShown(handle) && RevitNative.PostTaskDialogClick(handle, known.TaskDialogButtonId, target);
                if (!posted)
                {
                    if (attempt == 1)
                    {
                        UiLog.Action($"окно Revit закрылось до TDM_CLICK_BUTTON {known.TaskDialogButtonId}, ответа не было: {target}");
                        return false;
                    }
                    UiLog.Action($"окно Revit закрылось после первого TDM_CLICK_BUTTON {known.TaskDialogButtonId} позже {TaskDialogCloseTimeout.TotalSeconds:0} с: {target}");
                    return true;
                }
                if (Wait.Holds(() => !Native.IsShown(handle), TaskDialogCloseTimeout))
                {
                    return true;
                }
                UiLog.Warn($"окно Revit не закрылось за {TaskDialogCloseTimeout.TotalSeconds:0} с после TDM_CLICK_BUTTON {known.TaskDialogButtonId} (попытка {attempt}): {target}");
            }
            throw new InvalidOperationException($"Окно Revit не закрылось после двух TDM_CLICK_BUTTON {known.TaskDialogButtonId} ({TaskDialogCloseTimeout.TotalSeconds:0} с на каждое): {target}. {known.Hint}");
        }

        private static string TaskDialogAction(KnownDialog known, string target)
        {
            return $"TDM_CLICK_BUTTON {known.TaskDialogButtonId}: {target}";
        }

        private static DialogInfo Read(AutomationElement window, IntPtr handle, bool withTexts)
        {
            string title = UiQuery.Read(() => window.Properties.Name.ValueOrDefault ?? "", Wait.DefaultTimeout, "заголовок окна Revit");
            string className = UiQuery.Read(() => window.Properties.ClassName.ValueOrDefault ?? "", Wait.DefaultTimeout, "класс окна Revit");
            string text = withTexts ? string.Join(Environment.NewLine, window.Texts().Where(t => t != title).Distinct()) : "";
            bool popup = className == RevitIds.Dialogs.PopupClass;
            return new DialogInfo(window, handle, title, className, text, popup, popup && IsRibbonToolTip(window));
        }

        /// <summary>Всплывающее окно — подсказка ленты: прямой потомок ToolTip с ClassName и Name подсказки AdWindows (<see cref="RevitIds.Dialogs.RibbonToolTipName"/>).</summary>
        private static bool IsRibbonToolTip(AutomationElement popup)
        {
            return UiQuery.Read(() => popup.FindAllChildren().Any(c => c.ControlType == ControlType.ToolTip
                && c.Properties.ClassName.ValueOrDefault == RevitIds.Dialogs.RibbonToolTipClass
                && c.Properties.Name.ValueOrDefault == RevitIds.Dialogs.RibbonToolTipName), Wait.DefaultTimeout, "подсказка ленты среди детей всплывающего окна");
        }

        private static void Note(ResetStep step, DialogInfo window, List<string> errors)
        {
            if (step.Known.IsRevitError)
            {
                string error = $"«{window.Title}»: {window.Text}";
                errors.Add(error);
                UiLog.Warn("окно Revit закрыто уборкой: " + error);
            }
        }

        private static UiResetReport Report(List<string> actions, List<string> errors, List<string> remaining)
        {
            foreach (string action in actions)
            {
                UiLog.Action("уборка окон Revit: " + action);
            }
            foreach (string left in remaining)
            {
                UiLog.Warn("уборка окон Revit не закончена: " + left);
            }
            return new UiResetReport(actions, errors, remaining);
        }
    }
}
