namespace RevitUITesting.Revit
{
    /// <summary>
    /// Подписи (UIA Name) Revit 2022 ENU, запущенного с /language ENU. Уровни: [Р] — проверено контрактным тестом (файл Contract/Evidence/*.txt);
    /// [Э] — видно на снимке экрана живого Revit 2022.1 ENU 07.10.2026 (explore-main.png), в UIA не проверено; [К] — код или документация;
    /// [Г] — гипотеза, её подтверждает или заменяет контрактная задача. Перенос строки «\n» в подписях кнопок ленты сохраняется как в PushButtonData.
    /// </summary>
    public static class RevitTitles
    {
        /// <summary>Окна верхнего уровня.</summary>
        public static class Windows
        {
            /// <summary>[Р] Evidence/main-window-2022.txt: «Autodesk Revit 2022.1 - [Home]» (без документа), в дампе с моделью — «Autodesk Revit 2022.1 - [new-1.rvt - Structural Plan: Level 1]»; [Э] «Autodesk Revit 2022.1 - UiSmokeProject.rvt - Sheet: A001 - Title Sheet». Начало заголовка главного окна; дальше — номер версии и документ.</summary>
            public const string MainPrefix = "Autodesk Revit ";
        }

        /// <summary>Вкладки ленты.</summary>
        public static class Tabs
        {
            /// <summary>[Р] Evidence/ribbon-2022.txt: Button с Name и AutomationId «Architecture», прямой потомок главного окна.</summary>
            public const string Architecture = "Architecture";

            /// <summary>[Р] Evidence/ribbon-2022.txt: Button с Name и AutomationId «Manage», прямой потомок главного окна.</summary>
            public const string Manage = "Manage";

            /// <summary>[Р] Evidence/ribbon-2022.txt: Button с Name и AutomationId «View», прямой потомок главного окна.</summary>
            public const string View = "View";

            /// <summary>[Р] Evidence/ribbon-2022.txt: Button с Name и AutomationId «Add-Ins», прямой потомок главного окна.</summary>
            public const string AddIns = "Add-Ins";

            /// <summary>[Р] Evidence/ribbon-2022.txt: Button с Name и AutomationId «Modify» (с открытой моделью); контекстная вкладка, есть не всегда. Name «Modify» есть и у кнопки панели Select (id «ID_BUTTON_SELECT») и у скрытой вкладки «Second_Modify».</summary>
            [ShownWhen("выделен элемент или открыта модель (контекстная вкладка)")]
            public const string Modify = "Modify";

            /// <summary>[Р] Evidence/ribbon-2022.txt: вкладка — Button с Name «TestHost» и AutomationId «TestHost»; [К] RevitTestHost TestHostApplication.cs (CreateRibbonTab("TestHost")).</summary>
            public const string TestHost = "TestHost";
        }

        /// <summary>Лента RevitTestHost.</summary>
        public static class TestHost
        {
            /// <summary>[К] TestHostApplication.cs (CreateRibbonPanel(tabName, "Test Runner")).</summary>
            public const string Panel = "Test Runner";

            /// <summary>
            /// [К] TestHostApplication.cs (PushButtonData text "Run All\nTests"). [Р] Evidence/ribbon-2022.txt: UIA Name кнопки — «Run All\nTests» с одним LF
            /// (13 символов), как в PushButtonData; у встроенных кнопок Revit перенос — CR LF («Project\r\nUnits»). Нажатие прогоняет ВСЕ Revit-тесты
            /// из TestHost\Tests — в контрактных тестах не нажимать.
            /// </summary>
            public const string RunAllTests = "Run All\nTests";

            /// <summary>[К] SummaryDialog.cs: заголовок TaskDialog с итогами прогона.</summary>
            public const string ResultsDialog = "RevitTestHost — Test Results";
        }

        /// <summary>Вкладка Manage.</summary>
        public static class Manage
        {
            /// <summary>
            /// [К] документация Revit: Manage › Settings › Project Units; открывает модальное окно с тем же заголовком. [Р] Evidence/ribbon-2022.txt:
            /// UIA Name кнопки — «Project\r\nUnits» (перенос CR LF, подписи нормализуются), Button с AutomationId «ID_SETTINGS_UNITS», паттерн Invoke.
            /// [Р] Evidence/project-units-2022.txt: нажатие открывает модальное окно «Project Units» (ClassName «#32770»; в UIA — потомок главного окна).
            /// </summary>
            public const string ProjectUnits = "Project Units";

            /// <summary>
            /// [К] документация Revit: Manage › Settings › Additional Settings (выпадающая кнопка). [Р] Evidence/ribbon-2022.txt: UIA Name —
            /// «Additional\r\nSettings», Button с AutomationId «HID_SETTINGS_RibbonListButton»; паттерны LegacyIAccessible, SynchronizedInput, Toggle —
            /// ExpandCollapse и Invoke нет (нужно задаче 11, ClickInMenu).
            /// </summary>
            public const string AdditionalSettings = "Additional Settings";

            /// <summary>
            /// [Р] Evidence/ribbon-pulldown-2022.txt: пункт меню Additional Settings — Button с Name «Line Styles», AutomationId «ID_SETTINGS_LINE_STYLES»,
            /// паттерн Invoke. [Р] Evidence/ribbon-pulldown-2022.txt (прогон 5): нажатие открывает модальное окно «Line Styles» (ClassName «#32770»),
            /// меню при этом закрывается (Toggle «Additional Settings» снова Off).
            /// </summary>
            public const string LineStyles = "Line Styles";
        }

        /// <summary>Вкладка View.</summary>
        public static class View
        {
            /// <summary>
            /// [К] документация Revit: View › Create › 3D View. [Р] Evidence/view-3d-2022.txt: разделённая кнопка — Button
            /// «ID_VIEW_DEFAULT_3DVIEW_FlyoutButtonShowFlyout» (Toggle, раскрывает меню — окно Popup «ID_ListButtonPopup_PopupRoot») и Button
            /// «ID_VIEW_DEFAULT_3DVIEW_FlyoutButtonExecute» (Invoke), обе с Name «3D\r\nView»: по подписи кнопка неоднозначна
            /// (AmbiguousElementException) — меню раскрывается по AutomationId «…_FlyoutButtonShowFlyout».
            /// </summary>
            public const string ThreeDView = "3D View";

            /// <summary>
            /// [К] документация Revit: пункт 3D View › Default 3D View; в новом проекте создаёт вид {3D} — документ становится изменённым.
            /// [Р] Evidence/view-3d-2022.txt: пункт меню — Button с Name «Default 3D View», AutomationId «ID_VIEW_DEFAULT_3DVIEW», Invoke
            /// (внутри ListItem списка «ID_VIEW_DEFAULT_3DVIEW»); нажатие изменяет документ (задача 12, прогон 2).
            /// </summary>
            public const string DefaultThreeDView = "Default 3D View";
        }

        /// <summary>Панели (DockablePane).</summary>
        public static class Panes
        {
            /// <summary>
            /// [Р] Evidence/panes-2022.txt: Name рамки закреплённой панели (Pane, ClassName «Afx:ControlBar:…», AutomationId «60164»).
            /// [Р] Evidence/properties-pane-2022.txt (задача 12): RevitPanes.Find находит рамку; узлов Text с Name в панели нет (Texts() пуст: Text «12520»
            /// без Name, сетка свойств — Pane «GXWND» id «1102» — строк в UI Automation не выставляет); читается ComboBox id «3119» (Value — текущий вид).
            /// Скрытие сочетанием PP (VK_P дважды) вживую не сработало: рамка осталась на экране (IsOffscreen=False; properties-pane-2022.txt в коммите 22bd06a). [Р] Evidence/properties-toggle-2022.txt:
            /// панель переключает клик мышью по <see cref="RevitIds.Panes.PropertiesToggle"/>; скрытой панели в дереве UIA нет (рамка исчезает, а не
            /// становится offscreen); снова показанная рамка (id «60164») лежит глубже — в TabControl AvalonDock, а не прямым потомком главного окна.
            /// </summary>
            public const string Properties = "Properties";

            /// <summary>[Р] Evidence/panes-2022.txt: «Project Browser - new-1.rvt» — Name заголовка вкладки AvalonDock (Text, id «mAnchorablerHeader»); рамка панели за невыбранной вкладкой в дереве не наблюдалась. [Э] снимок экрана: «Project Browser - UiSmokeProject.rvt»; дальше — имя документа.</summary>
            public const string ProjectBrowserPrefix = "Project Browser - ";
        }

        /// <summary>Диалоги Revit и подписи их кнопок.</summary>
        public static class Dialogs
        {
            /// <summary>
            /// [Р] Evidence/save-changes-2022.txt: фрагмент текста вопроса о сохранении при закрытии изменённого документа — TaskDialog
            /// «Save File» (#32770), текст «Do you want to save changes to new-1.rvt?» (Text id «MainInstruction»); главное окно при нём недоступно.
            /// </summary>
            public const string SaveChangesText = "save changes";

            /// <summary>
            /// [К] английская подпись кнопки «не сохранять»; вживую не совпало (Evidence/save-changes-2022.txt: при Revit 2022 ENU на test-machine кнопки
            /// «Да | Нет | Отмена», причина не проверена). Библиотека отвечает на вопрос о сохранении по id кнопки
            /// (<see cref="RevitIds.Dialogs.TaskDialogNoButtonId"/>), а не по этой подписи.
            /// </summary>
            public const string No = "No";

            /// <summary>[К] английская подпись кнопки «сохранить»; вживую не совпало (Evidence/save-changes-2022.txt: «Да»). Библиотекой не используется — оставлена для совместимости API.</summary>
            public const string Yes = "Yes";

            /// <summary>[Р] Evidence/project-units-2022.txt: кнопка отмены окна «Project Units» — Button «Cancel» (id «2», Invoke); [К] так же в других стандартных окнах Revit.</summary>
            public const string Cancel = "Cancel";

            /// <summary>[К] память revit-addin-trust-codesigning: заголовок TaskDialog_Security_Unsigned_File_Loading в ENU.</summary>
            public const string UnsignedAddinEn = "Security - Unsigned Add-In";

            /// <summary>[К] память revit-addin-trust-codesigning: тот же диалог в русской установке (без /language ENU).</summary>
            public const string UnsignedAddinRu = "Безопасность — Надстройка без подписи";

            /// <summary>[Г] вопрос при закрытии модели совместной работы без синхронизации.</summary>
            public const string CloseWithoutSaving = "Close Project Without Saving";

            /// <summary>[Г] начало заголовка ошибок Revit («Error - cannot be ignored»).</summary>
            public const string ErrorPrefix = "Error";

            /// <summary>[Г] начало заголовка предупреждений Revit («Warning», «Warning - can be ignored»).</summary>
            public const string WarningPrefix = "Warning";
        }
    }
}
