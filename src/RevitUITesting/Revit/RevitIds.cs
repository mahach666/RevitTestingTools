using System.Linq;
using FlaUI.Core.Definitions;
using RevitUITesting.Uia;

namespace RevitUITesting.Revit
{
    /// <summary>
    /// AutomationId, классы окон и параметры поиска Revit 2022 в UI Automation. const string — точный AutomationId или ClassName (суффикс Class);
    /// static readonly — параметры поиска, которые фиксируют контрактные задачи 10–12 (CatalogAssert их не сверяет). Уровни — как в <see cref="RevitTitles"/>.
    /// </summary>
    public static class RevitIds
    {
        /// <summary>Надстройка RevitTestHost.</summary>
        public static class TestHost
        {
            /// <summary>
            /// [К] TestHostApplication.cs: внутреннее имя PushButtonData кнопки «Run All\nTests». [Р] Evidence/ribbon-2022.txt: полный AutomationId кнопки
            /// в AdWindows — «CustomCtrl_%CustomCtrl_%TestHost%Test Runner%RunAllTests» (ControlType Button, ClassName «Button», паттерн Invoke).
            /// </summary>
            public const string RunAllTests = "RunAllTests";

            /// <summary>[К] C:\ProgramData\Autodesk\Revit\Addins\2022\RevitTestHost.addin (ClientId) — для trustAddins.</summary>
            public const string AddinId = "770c23e2-3b3a-4ea3-89a2-d59899bed9a0";
        }

        /// <summary>Окна и диалоги.</summary>
        public static class Dialogs
        {
            /// <summary>
            /// [Р] Evidence/save-changes-2022.txt: ClassName окна TaskDialog Revit — «#32770» (вопрос о сохранении «Save File»: тексты — Text
            /// ClassName «Element», id «MainInstruction»/«ContentText»; кнопки — Button ClassName «CCPushButton», id «CommandButton_&lt;id кнопки&gt;», Invoke).
            /// </summary>
            public const string TaskDialogClass = "#32770";

            /// <summary>
            /// [Р] Evidence/save-changes-2022.txt: id кнопки «No» вопроса о сохранении — 7 (IDNO; Button id «CommandButton_7»; «Yes» — 6, «Cancel» — 2).
            /// Подписи кнопок TaskDialog не следуют языку Revit: Revit 2022 ENU на test-machine показал «Да | Нет | Отмена» (причина не проверена), поэтому вопрос
            /// отвечается сообщением TDM_CLICK_BUTTON с этим id, а не по подписи.
            /// </summary>
            public const int TaskDialogNoButtonId = 7;

            /// <summary>
            /// ClassName всплывающих окон WPF. [Р] Evidence/ribbon-pulldown-2022.txt: меню выпадающей кнопки ленты «Additional Settings» — окно
            /// «Popup» (Window, id «_PopupRoot») → List (id = id кнопки, «HID_SETTINGS_RibbonListButton») → ListItem → Button с Invoke (id
            /// «ID_SETTINGS_LINE_STYLES», Name «Line Styles»). Это окно видно через EnumWindows (класс Win32 у окна WPF другой — «Popup» только ClassName в UIA, прогон 4), но не среди детей рабочего стола в UI Automation,
            /// поэтому его нет в <see cref="RevitWindows.Popups"/>; лента ищет свои меню сама (RevitRibbon). [Г] выпадающие списки окон плагинов.
            /// </summary>
            public const string PopupClass = "Popup";

            /// <summary>
            /// [Р] Evidence/closing-run-2022.txt (запуск D), ribbon-pulldown-2022.txt (запуск A): подсказка кнопки ленты — окно <see cref="PopupClass"/>,
            /// прямой потомок — ToolTip с ClassName <see cref="RibbonToolTipClass"/> и Name «Autodesk.Windows.RibbonToolTip». Всплывает под курсором,
            /// оставленным на ленте, и не закрывается по Esc; уборка окон её не трогает.
            /// </summary>
            public const string RibbonToolTipName = "Autodesk.Windows.RibbonToolTip";

            /// <summary>[Р] ClassName узла ToolTip подсказки ленты (см. <see cref="RibbonToolTipName"/>).</summary>
            public const string RibbonToolTipClass = "ToolTip";

            /// <summary>
            /// [К] память revit-addin-trust-codesigning: id кнопок диалога «Security - Unsigned Add-In» — 1001 «Always Load», 1002 «Load Once», 1003 «Do Not Load».
            /// Нажимается сообщением TDM_CLICK_BUTTON (0x0466): окна нет в дереве UIA от корня, у кнопок нет Invoke, а WM_CLOSE равносилен «Do Not Load».
            /// </summary>
            public const int UnsignedAddinAlwaysLoadButtonId = 1001;
        }

        /// <summary>Лента AdWindows. Значения фиксирует задача 10 (дамп UIA) и уточняет задача 11 (свёрнутые панели).</summary>
        public static class Ribbon
        {
            /// <summary>
            /// [Р] Evidence/ribbon-2022.txt: корня ленты нет — поиск во всём главном окне Revit. Вкладки («Architecture», «TestHost» и др.) и их
            /// содержимое («&lt;id вкладки&gt;_PanelBarScrollViewer») — прямые потомки главного окна (ClassName «Window»), так что их ближайший общий
            /// предок — само главное окно; элемента-контейнера ленты с собственным ClassName или AutomationId в дереве UIA нет.
            /// </summary>
            public static readonly By Root = null;

            /// <summary>
            /// [Р] Evidence/ribbon-2022.txt: вкладка ленты — Button (ClassName «Button», AutomationId — внутреннее имя вкладки, например «TestHost»,
            /// «thePrecastTab» у «Precast»; Name — подпись; паттерн Invoke), прямой потомок главного окна. Тип сам по себе вкладку не отличает:
            /// Button с Name «Modify» — и вкладка (id «Modify»), и кнопка панели Select (id «ID_BUTTON_SELECT»); скрытые вкладки редактора
            /// семейств (id «Manage_Family», Name «Manage») — offscreen.
            /// </summary>
            public static readonly ControlType TabType = ControlType.Button;

            /// <summary>
            /// [Р] Evidence/ribbon-2022.txt: «Run All\nTests», «Project Units» и «Additional Settings» — Button (ClassName «Button»). SplitButton и
            /// MenuItem в ленте не наблюдались. [Р] Evidence/ribbon-pulldown-2022.txt: пункты меню выпадающей кнопки — тоже Button (внутри ListItem).
            /// </summary>
            public static readonly ControlType[] ButtonTypes = { ControlType.Button };

            /// <summary>
            /// [Р] Evidence/ribbon-2022.txt: окончание AutomationId содержимого вкладки — «&lt;id вкладки&gt;_PanelBarScrollViewer» (Custom, прямой
            /// потомок главного окна, Name — AutomationId вкладки: «thePrecastTab», а не подпись «Precast»). У активной вкладки содержимое на экране (Architecture: {X=0,Y=75,Width=2882,Height=144}),
            /// у остальных — offscreen с пустым прямоугольником. Вкладка = Button-потомок корня, у которого есть такое содержимое; у кнопок
            /// ID_ApplicationMenuButton, ID_MinimizeButton, ID_OverflowButton (тоже Button-потомки главного окна) его нет. Кнопки вкладки ищутся в нём.
            /// </summary>
            public const string TabContentSuffix = "_PanelBarScrollViewer";

            /// <summary>
            /// [Р] дамп UIA Revit 2025.1 с открытой моделью (живой прогон 2026-10-15): кнопки вкладок — Button-потомки Custom с этим AutomationId
            /// (прямой потомок главного окна), а не самого окна, как в 2020 и 2022. Без такой полосы — раскладка 2020–2022.
            /// </summary>
            public const string TabStripId = "mMainTabs";

            /// <summary>
            /// [Р] тот же дамп 2025.1: содержимое вкладки — главное окно → List «ItemsControl» → DataItem «UIFramework.RvtRibbonTab» → «&lt;id&gt;_PanelBarScrollViewer»
            /// (глубина 3), и только у активной вкладки: DataItem остальных вкладок пустые. В 2020 и 2022 содержимое на глубине 1.
            /// </summary>
            public const int TabContentDepth = 3;

            /// <summary>
            /// [Р] Evidence/ribbon-collapsed-2022.txt: AutomationId картинки внутри кнопки свёрнутой панели ленты («mCollapsedPanelImage»).
            /// </summary>
            public const string CollapsedPanelImageId = "mCollapsedPanelImage";

            /// <summary>
            /// [Р] Evidence/ribbon-collapsed-2022.txt (главное окно 1063×700 — меньше Revit не сжимается): свёрнутая панель — Button (ClassName «Button»,
            /// AutomationId = id панели, например «settings_shr», Name пустой, паттерн Toggle — ни Invoke, ни ExpandCollapse) с потомком Image
            /// «mCollapsedPanelImage»; такая кнопка есть у каждой панели, у развёрнутой она offscreen (отбор по экрану — в <see cref="RevitRibbon"/>).
            /// Кнопки свёрнутой панели остаются в дереве под панелью, но offscreen. [Р] Evidence/ribbon-collapsed-2022.txt (прогон 4): Toggle раскрывает
            /// панель — появляется видимое (EnumWindows) окно с ClassName UIA <see cref="Dialogs.PopupClass"/> и id «&lt;id панели&gt;_SlideOutPanelPopup_PopupRoot»
            /// с кнопками панели (Project Units — Button «ID_SETTINGS_UNITS», Invoke); возврат Toggle сворачивает панель (Toggle снова Off).
            /// [Р] прогон 5: окно видимо (EnumWindows, {X=1326,Y=223,W=1153,H=144}), но в UIA окно и его кнопки и через 5 с после раскрытия
            /// offscreen с пустым прямоугольником — кнопка в окне панели ищется без отбора по экрану.
            /// [Р] прогон 6 (Evidence/ribbon-collapsed-2022.txt): у Project Location окно панели открыто при Toggle Off (у Settings — On), поэтому
            /// раскрытость панели определяется по её окну, а не по Toggle (<see cref="RevitRibbon.CollapsePanel"/>).
            /// </summary>
            public static readonly By CollapsedPanel = By.Where(f => f.ControlType == ControlType.Button && f.ClassName == "Button"
                && f.Element.FindAllChildren().Any(c => c.Properties.AutomationId.ValueOrDefault == CollapsedPanelImageId), "свёрнутая панель ленты");

            /// <summary>
            /// [Р] Evidence/ribbon-2022.txt: AdWindows выставляет AutomationId кнопок API вида «CustomCtrl_%CustomCtrl_%Вкладка%Панель%ИмяPushButtonData»
            /// (кнопка «Run All\nTests» — «CustomCtrl_%CustomCtrl_%TestHost%Test Runner%RunAllTests»).
            /// </summary>
            public const bool ButtonIdsExposed = true;

            /// <summary>[К] разделитель частей Id кнопки API в AdWindows («%»); кнопка ищется по окончанию «%ИмяPushButtonData».</summary>
            public const string ApiIdSeparator = "%";

            /// <summary>Глубина поиска элементов ленты от корня.</summary>
            public const int SearchDepth = 20;
        }

        /// <summary>Панели (DockablePane). Значения фиксирует задача 10.</summary>
        public static class Panes
        {
            /// <summary>
            /// ControlType рамки панели, у которой Name = заголовок панели. Pane — [Р] Evidence/panes-2022.txt: закреплённые панели «Properties»
            /// (прямой потомок главного окна) и «Выбор по фильтру» (внутри TabControl → TabItem → MFCPaneControl → HwndHost AvalonDock) — Pane
            /// с ClassName «Afx:ControlBar:…» и Name = заголовок; содержимое — Pane «Afx:…» без Name. Window — [Г] плавающая панель (окно верхнего
            /// уровня) не наблюдалась. [Р] Evidence/properties-pane-2022.txt (задача 12): рамка «Properties» — та же (id «60164»). [Р] Evidence/properties-toggle-2022.txt:
            /// скрытой панели в дереве нет (рамка исчезает); снова показанная рамка — в TabControl AvalonDock (TabItem → MFCPaneControl → HwndHost). Панель за невыбранной вкладкой группы AvalonDock («Project Browser - new-1.rvt») рамки в дереве не имеет:
            /// виден только заголовок вкладки (Text, id «mAnchorablerHeader»).
            /// </summary>
            public static readonly ControlType[] FrameTypes = { ControlType.Pane, ControlType.Window };

            /// <summary>
            /// [Р] Evidence/properties-toggle-2022.txt: AutomationId переключателя панели Properties на вкладке Modify (панель «properties_shr») —
            /// Button с Name «Properties», паттерны LegacyIAccessible, SynchronizedInput, Toggle; ToggleState — видимость панели (On — показана).
            /// Переключает только клик мышью: TogglePattern.Toggle и LegacyIAccessible.DoDefaultAction панель не переключают.
            /// </summary>
            public const string PropertiesToggle = "ID_TOGGLE_PROPERTIES_PALETTE";

            /// <summary>Глубина поиска панели от главного окна.</summary>
            public const int SearchDepth = 12;
        }
    }
}
