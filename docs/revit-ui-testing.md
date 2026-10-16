# RevitUITesting

Библиотека для сквозных UI-тестов плагинов Revit. Тест запускает (или подхватывает) настоящий Revit, прокликивает интерфейс плагина через UI Automation (FlaUI) так, как это делает пользователь, а результат проверяет в модели через [режим сервера RevitTestHost](server-protocol.md). Библиотека работает снаружи процесса Revit и не ссылается на Revit API.

Ядро не зависит от Revit. В него входят ожидания, различие «элемента нет» и «приложение занято», нажатие без повторов, мышь и клавиатура только в окно приложения, журнал шагов, конфигурация, артефакты падения и сверка каталога идентификаторов.

## Состав

| Проект | Назначение |
|---|---|
| `src/RevitUITesting` | библиотека (net48; FlaUI.UIA3 5.0, NUnit 4.3, System.Text.Json 8.0) |
| `tests/RevitUITesting.Tests` | модульные тесты без Revit, снимок публичного API, контрактные тесты в живом Revit (`[Explicit]`, `Category=RevitContract`, версия из `revitVersion`) с доказательствами в `Contract/Evidence` |
| `tools/RevitUITesting.Inspector` | консольный инспектор: дамп дерева UI Automation окон процесса, клики, ввод |

## Сборка и тесты

```bash
dotnet build RevitTestingTools.sln
dotnet test tests/RevitUITesting.Tests/RevitUITesting.Tests.csproj
```

## Быстрый старт

Нужно:

- Revit с английским интерфейсом (`/language ENU`), год задаёт `revitVersion` (проверено на 2022.1);
- надстройка RevitTestHost с режимом сервера в `%ProgramData%\Autodesk\Revit\Addins\<год>\TestHost\` (для 2027 в `%AppData%\Autodesk\Revit\Addins\2027\TestHost\`) ([установка](revit-test-host.md#установка));
- сборка с Revit-тестами плагина (`*Tests.dll`) в `TestHost\Tests\`, если нужны проверки в модели.

Перед прогоном Revit не должен быть запущен. Исключение: Revit, оставленный самой библиотекой при `keepRevitOpen`, его подхватывает `attachToRunning` (см. «Конфигурация»). Чужой Revit библиотека не подхватывает и не закрывает, запуск при нём отказывает и перечисляет pid. Свободный commit должен быть не меньше `minFreeCommitGb`.

1. Тестовый проект на `net48`: ссылка на `RevitUITesting` (файл `.nupkg` из релиза как локальный источник NuGet или ссылка на проект `src/RevitUITesting`), пакеты `NUnit 4.3.2`, `NUnit3TestAdapter 5.0.0`, `Microsoft.NET.Test.Sdk 17.14.1`. Файл `revit-ui-tests.json` копируется в выходную папку:

   ```xml
   <None Update="revit-ui-tests.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
   ```

2. В `revit-ui-tests.json` задайте `revitVersion`, `language` и `trustAddins` (GUID ваших надстроек и RevitTestHost `770c23e2-3b3a-4ea3-89a2-d59899bed9a0`).

3. Добавьте один `[SetUpFixture]` в корневом пространстве имён тестов. Тогда Revit закрывается штатно после прогона. Без него Revit, запущенный тестами, убивается принудительно в `ProcessExit`, а в журнал пишется подсказка про этот класс.

   ```csharp
   [SetUpFixture]
   public class RevitRun
   {
       [OneTimeTearDown]
       public void End() => RevitSession.End();
   }
   ```

4. Напишите класс тестов. Названия кнопки, панели и Revit-теста в примере условные. Пространства имён: `RevitUITesting` (`LogWatch`), `RevitUITesting.NUnit` (`RevitUiTestBase`, `RevitUiModel`, `RevitSession`), `RevitUITesting.Pages` (`DockablePanePage`), а также `NUnit.Framework` и `System.Collections.Generic`. Файл модели (`Models\sample.rvt`) копируется в выходную папку так же, как `revit-ui-tests.json`, иначе `[RevitUiModel]` бросает `FileNotFoundException`.

   ```csharp
   using System.Collections.Generic;
   using NUnit.Framework;
   using RevitUITesting;
   using RevitUITesting.NUnit;
   using RevitUITesting.Pages;

   [TestFixture, Explicit("живой Revit"), Category("RevitUi"), NonParallelizable]
   [RevitUiModel(@"Models\sample.rvt", CopyToTemp = true)]          // или [RevitUiModel("@new")]
   public class SearchPaneTests : RevitUiTestBase
   {
       [Test]
       public void Pane_opens_from_ribbon()
       {
           Step("Открыть панель кнопкой ленты");
           App.Ribbon.Click("MyPlugin", "Search\nFamilies");
           DockablePanePage pane = App.Panes.Find("Family search");
           pane.WaitText(t => t.StartsWith("Project"));

           Step("Проверить модель");
           // Revit-тест плагина с [RevitTest(Model = RevitTestAttribute.ActiveDocument)]
           AssertInRevit("MyPlugin.RevitTests.Probes.ProjectStateProbe.*",
               new Dictionary<string, string> { ["expectedTitle"] = Model.Title });
           AssertNoRevitErrors();
       }
   }
   ```

5. Запуск: `dotnet test --filter "Category=RevitUi"`. При падении теста (и при статусе Inconclusive) создаётся папка артефактов `%TEMP%\RevitUITesting\artifacts\<время>_<класс.тест>` (путь задаёт `artifactsDir`, файлы прикладываются к результату теста): `exception.txt`, `steps.log`, `screen.png`, `windows.txt`, `revit-dialogs.txt`, `revit-journal.txt`, `host.txt`, `logs.txt`. Файлы `windows.txt` и `revit-dialogs.txt` пишутся, только пока процесс Revit жив, а если Revit завершился, в `host.txt` записывается только это. Файл `windows.txt` это полный обход дерева UI Automation окон Revit, с открытой моделью он занимает минуты (см. [особенности Revit](revit-quirks.md)).

Журналы самого плагина подключаются переопределением `CreateLogWatch()`, например `new LogWatch(папка, new[] { "MyPlugin" })`. Тогда `AssertNoLogErrors`, `LogLines` и `WaitLog` читают эти журналы, а отметки и артефакты падения включают их строки.

## Основные понятия

| Что | Где | Зачем |
|---|---|---|
| `RevitApp` | `App` в базовом классе | сеанс Revit: `Host`, `MainWindow`, `Ribbon`, `Windows`, `Panes`, `SendKeys`, `CloseActiveDocument()`, `Quit()`, `Restart()`, `Kill(причина)`, `Screenshot(имя)`. Запуск или подхват: `StartOrAttach(config)`. `Restart()` работает только у `RevitApp`, запущенного самим тестом через `StartOrAttach`. У `App` базового класса (Revit сеанса) он бросает `InvalidOperationException`, для сеанса используйте `RevitSession.Restart()` |
| `RevitHostClient` | `Host` | канал RevitTestHost: `Ping`, `WaitIdle` (ответ и `idle = true`), `Documents`, `Open(путь или "@new", copyToTemp)`, `Close(заголовок)`, `ClosePath(путь)`, `Run(фильтр, параметры, модель)`, `AssertRun(...)`. Нет ответа за `hostTimeoutSeconds` (для `Open` и `Run` за `modelOpenTimeoutSeconds`): `RevitBusyException` со списком окон Revit. `WaitIdle`, не дождавшийся `idle = true`: `UiWaitTimeoutException`. Канал не принял подключение (режим сервера не запущен): `TimeoutException`. Ответ с ошибкой: `HostErrorException.Code` |
| `RevitSession` | статический класс | один Revit на прогон. `End()` вызывается в `[SetUpFixture]`. Если Revit умер, следующий класс тестов запускает новый. `Restart()` закрывает и снова запускает Revit сеанса: новый Revit закрывается в `End()`, `App` базового класса читается из сеанса и после перезапуска уже новый, модель класса заново не открывается. Вживую не проверено |
| `RevitUiModelAttribute` | атрибут класса | модель класса через `Host.Open`. Модель прошлого класса закрывается после открытия новой |
| `RevitUiTestBase` | база фикстуры | `Config`, `App`, `Host`, `Model`, `Step`, `AfterTest` и `AfterTests`, `RestoreFileAfterTests`, `AssertInRevit`, `AssertNoRevitErrors`, `AssertNoLogErrors`, `LogLines`, `WaitLog`, `NewOutputPath`. Перед тестом выполняются уборка окон (`ResetUi`) и ожидание простоя Revit, после теста артефакты падения, уборка окон и возвраты `AfterTest` |
| `RevitRibbon` | `App.Ribbon` | `Tabs()`, `Tab(имя)`, `Button(вкладка, кнопка)`, `Click(вкладка, кнопка)`, `ClickInMenu(вкладка, выпадающая, пункт)`. Кнопка задаётся внутренним именем `PushButtonData` или подписью (`"Run All\nTests"` и `"Run All Tests"` равны). Работает с кнопками свёрнутых панелей и пунктами выпадающих кнопок |
| `RevitWindows` | `App.Windows` | окна процесса: `All()`, `Popups()`, `OpenedBy(действие)`, `WaitGone`, `TryTaskDialog()`, `WaitTaskDialog()`, `RevitErrors()`, `ResetUi()` |
| `RevitTaskDialog` | окно TaskDialog | `Title`, `Text`, `Buttons()`, `Answer(подпись)`: кнопки и command links (по первой строке подписи) |
| `RevitDialogs`, `KnownDialog` | каталог | известные окна. «Save changes» закрывается ответом «No» по id кнопки TaskDialog (7). «Security - Unsigned Add-In» получает «Always Load» (id 1001) при ожидании готовности Revit. `Warning*` и `Error*` закрываются с записью текста. Каталог дополняется через `knownDialogs`. Подробности в [особенностях Revit](revit-quirks.md) |
| `UiPage` | база page-object | поиск внутри своего корня: `Find`, `TryFind`, `Has`, `AssertAbsent`, `Click`, `ClickText`, `MouseClick`, `DoubleClick`, `RightClick`, `SetValue`, `ValueOf`, `IsEnabled`, `IsChecked`, `SetChecked`, `Choose`, `ComboItems`, `Texts`, `TryText`, `WaitText`, `ListBy`, `ListById`, `GridById`, `Dump` |
| `DockablePanePage` | панель | `new DockablePanePage(app, "заголовок")` или наследник. Поиск идёт внутри рамки панели (Pane с Name, равным заголовку). Найденная рамка запоминается и на каждом обращении проверяется (дескриптор окна не 0, окно видимо, `IsOffscreen` равен false). Полный поиск по главному окну с открытой моделью занимает десятки секунд, поэтому он выполняется, только если рамка пропала. `App.Panes.Find` ждёт появления панели, `TryFind` возвращает null, если панель скрыта |
| `RevitDialogPage` | окно | окно Revit или плагина: `OpenedBy(app, действие)`, `WaitFor(app, заголовок)`, `ClickAndWaitClosed`, `ClickTextAndWaitClosed`, `Close` (закрытие проверяется по HWND) |
| `RevitIds`, `RevitTitles` | каталог | AutomationId, классы и подписи Revit с уровнем доказательства: `[Р]` проверено контрактным тестом (`Contract/Evidence/*.txt`), `[Э]` видно на снимке экрана, `[К]` взято из кода или документации, `[Г]` гипотеза |

## Конфигурация `revit-ui-tests.json`

Файл лежит рядом с тестовой сборкой. Приоритет источников, от высокого к низкому: переменные окружения `REVIT_UI_*`, файл `revit-ui-tests.local.json` (не добавляется в git, для учётных записей и паролей), файл `revit-ui-tests.json`, значения по умолчанию. Объекты двух файлов сливаются по ключам (вложенные рекурсивно), массивы и значения заменяются целиком. Неизвестный ключ верхнего уровня даёт предупреждение `Validate()` (возможна опечатка).

```jsonc
{
  "revitVersion": "2022",                  // путь по умолчанию: C:\Program Files\Autodesk\Revit {версия}\Revit.exe
  "revitExe": null,                        // явный путь, перекрывает версию
  "language": "ENU",                       // аргумент /language; null оставляет язык установки
  "attachToRunning": false,                // подхватить уже запущенный Revit (только если в нём включён режим сервера)
  "keepRevitOpen": false,                  // не закрывать Revit после прогона (отладка)
  "dpiAware": true,
  "minFreeCommitGb": 5,                    // свободный commit перед запуском Revit
  "trustAddins": [],                       // GUID надстроек: доверие до запуска, без диалога «Надстройка без подписи»
  "knownDialogs": [],                      // [{ "title": "...", "textContains": "...", "answer": "OK" }] к встроенному каталогу
  "defaultTimeoutSeconds": 30, "revitStartTimeoutSeconds": 300, "hostTimeoutSeconds": 60, "modelOpenTimeoutSeconds": 300, "revitExitTimeoutSeconds": 60,
  "artifactsDir": null, "artifactsKeep": 50, // null означает %TEMP%\RevitUITesting\artifacts
  "sections": {                            // настройки потребителя: config.Section<T>("stand")
    "stand": { "standUrl": "http://stand.example/" }
  }
}
```

| Переменная | Ключ |
|---|---|
| `REVIT_UI_REVIT_VERSION`, `REVIT_UI_REVIT_EXE`, `REVIT_UI_LANGUAGE` | `revitVersion`, `revitExe`, `language` |
| `REVIT_UI_ATTACH`, `REVIT_UI_KEEP_REVIT`, `REVIT_UI_DPI_AWARE` (1, 0, true, false) | `attachToRunning`, `keepRevitOpen`, `dpiAware` |
| `REVIT_UI_MIN_FREE_COMMIT_GB`, `REVIT_UI_DEFAULT_TIMEOUT` (целое) | `minFreeCommitGb`, `defaultTimeoutSeconds` |
| `REVIT_UI_ARTIFACTS` | `artifactsDir` |
| `REVIT_UI_<РАЗДЕЛ>__<СВОЙСТВО>` | свойство раздела `sections.<раздел>` (строка, число, 1, 0, true, false) |

Неверный флаг или целое число в переменной основных настроек даёт `FormatException`, тихого значения по умолчанию нет. Раздел потребителя читается так же строго: неверный флаг в переменной окружения для свойства раздела даёт `FormatException`, а всё остальное (в том числе неверное значение в файле) `InvalidDataException`. Это ключ, которого нет в типе раздела, переменная для несуществующего свойства, нечисловое значение числового свойства и переменная для массива.

```csharp
public sealed class StandSettings
{
    public string StandUrl { get; set; }
    public string AdminLogin { get; set; }
    public string AdminPassword { get; set; }   // в revit-ui-tests.local.json или REVIT_UI_STAND__ADMINPASSWORD
}

RevitUiConfig config = RevitUiConfig.Load();
StandSettings stand = config.Section<StandSettings>("stand");
```

### Доверие к надстройкам, диалоги, память

- `trustAddins` пишет `HKCU\Software\Autodesk\Revit\Autodesk Revit <версия>\CodeSigning\<guid> = 1` (DWORD) до запуска Revit и не откатывает запись (это то же, что «Всегда загружать»). Каждое изменение попадает в журнал шагов. GUID берётся из `AddInId`, иначе из `ClientId` файла `.addin`. GUID RevitTestHost: `770c23e2-3b3a-4ea3-89a2-d59899bed9a0`. Это первая линия защиты от диалога «Security - Unsigned Add-In», но не гарантия, см. [особенности Revit](revit-quirks.md).
- `knownDialogs`: `title` это точный заголовок без учёта регистра (звёздочка в конце означает «начинается с»), `textContains` фрагмент текста, `answer` подпись кнопки. Пустой `answer` каталог принимает (окно закрывается сообщением `WM_CLOSE`, для TaskDialog это «Cancel»), но `Validate()` ждёт `title` и `answer` в каждой записи и пишет предупреждение в журнал. Записи проверяются раньше встроенного каталога.
- `minFreeCommitGb`: если свободного commit меньше, запуск отказывает и перечисляет главных потребителей памяти (`vmmem` и `vmmemWSL` это память WSL и Docker).
- Файл сеанса `%TEMP%\RevitUITesting\session.json` (`pid`, `pipe`, `revitVersion`, `startedUtc`) пишет `RevitApp` при запуске. По нему `attachToRunning: true` подхватывает Revit, оставленный при `keepRevitOpen: true`, при этом должны совпасть pid и время старта процесса. Revit без файла сеанса (запущенный вручную) не подхватывается и не закрывается, запуск тестов при нём отказывает и перечисляет pid.

## Ядро UI Automation

```csharp
Wait.DefaultTimeout = config.DefaultTimeout;
AutomationElement search = pane.Find(By.Id("SearchBox"));               // нет элемента: ElementNotFoundException с id области
UiActions.SetValue(search, "ванна", "поле поиска");                       // запись и проверка значения
UiActions.Press(pane.Find(By.Name("Загрузить")), "кнопка «Загрузить»");  // Invoke один раз, без повтора
Wait.Until(() => pane.TryFind(By.Name("Загружено")) != null);           // постусловие вместо повтора нажатия
```

- «Не найдено» говорится только после полного обхода дерева. Если приложение не отвечало на запросы UI Automation, бросается `AppBusyException`.
- Таймаут ожидания даёт `UiWaitTimeoutException` (наследник `TimeoutException`) с описанием, последним наблюдением и последними шагами журнала.
- Клики мышью требуют осведомлённости о DPI (`dpiAware`). Ввод уходит только в окно процесса приложения.
- `UiActions.Choose` и `ComboItems` ищут пункты списка и во всплывающих окнах, которые передаёт вызывающий: `Func<IEnumerable<AutomationElement>> popups`.
- `Wait.Until` и `Wait.For` строгие (и с явным таймаутом, и без него): исключение условия, не относящееся к UI Automation, пробрасывается сразу.

## Особенности Revit

Окна и диалоги, запуск Revit, лента, панели и то, что не проверено, собраны в [revit-quirks.md](revit-quirks.md).

## Контрактные тесты

Контрактные тесты запускаются в живом Revit с английским интерфейсом и по умолчанию пропускаются (`[Explicit]`). Версия берётся из `revitVersion` (или `REVIT_UI_REVIT_VERSION`), файлы доказательств получают имя `<имя>-<год>.txt`. Прогон по нескольким годам: [live-testing.md](live-testing.md).

```powershell
dotnet test tests\RevitUITesting.Tests\RevitUITesting.Tests.csproj --filter "Category=RevitContract"
```

Предусловия: Revit не запущен, свободный commit не меньше 5 ГБ, в `TestHost\Tests\` развёрнуты Debug-сборки RevitTestHost (`RevitTestHost.Addin.dll`, `RevitTestHost.Core.dll`) и пример `RevitTestHost.SampleTests.dll` (`dotnet build samples/RevitTestHost.SampleTests/RevitTestHost.SampleTests.csproj -c Debug`).

Классы: `RevitAppContractTests` (тесты сами запускают Revit и закрывают его, кроме `Leave_open_with_new_model_for_inspection`: он оставляет Revit для инспектора, а закрывает его `Attach_and_quit`), `RibbonContractTests`, `HostContractTests` и `CloseViaUiContractTests` (используют один Revit сеанса). `ContractSession` закрывает его в конце прогона. Тесты пишут доказательства в `tests/RevitUITesting.Tests/Contract/Evidence/`, выводы из них переносятся в `RevitIds` и `RevitTitles` (уровень `[Р]`) и в [revit-quirks.md](revit-quirks.md). После обновления Revit прогон нужно повторить и сверить доказательства.

Последний полный прогон сеансового контракта на Revit 2022.1: 14 из 14.

## Инспектор

Консольная утилита для автора тестов: показывает дерево UI Automation окон процесса, делает снимки и выполняет действия над элементами.

```text
RevitUITesting.Inspector dump  [processName=Revit] [outDir=%TEMP%\RevitUITesting] [maxDepth=12]
RevitUITesting.Inspector shot  [processName=Revit] [outDir]
RevitUITesting.Inspector start "exePath" [outDir] [waitSeconds=60]
RevitUITesting.Inspector click processName "id=AutomationId|name=Имя|text=Часть имени" [outDir] [waitMs=3000]
RevitUITesting.Inspector row   processName "текст строки" [outDir]
RevitUITesting.Inspector type  processName "id=AutomationId" "текст" [outDir]
RevitUITesting.Inspector keys  processName "{ENTER}" [outDir]
```

Коды выхода: 0 успех, 1 ошибка (процесс или элемент не найден), 2 неверные аргументы. Команда `click` берёт первое найденное совпадение (окна процесса перебираются с последнего). Для вкладок ленты Revit используйте `id=`: по `name=` первой может оказаться скрытая `<id>_PanelBarScrollViewer`. Дамп заменяет переносы строк в Name пробелами.

## Снимок публичного API

Файл `tests/RevitUITesting.Tests/RevitUITesting.PublicApi.approved.txt` фиксирует публичный API библиотеки. Из него ничего не должно пропадать, добавления разрешены. После добавлений снимок обновляют так:

```bash
REVIT_UI_UPDATE_API=1 dotnet test tests/RevitUITesting.Tests/RevitUITesting.Tests.csproj --filter "FullyQualifiedName~PublicApiTests"
```
