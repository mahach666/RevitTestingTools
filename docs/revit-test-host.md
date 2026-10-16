# RevitTestHost

RevitTestHost запускает тесты внутри живого процесса Revit. Это нужно потому, что `RevitAPI.dll` из NuGet-пакетов поставляется как reference assembly: методы экземпляров бросают `NotImplementedException` без запущенного Revit. Чистая логика плагина проверяется обычным `dotnet test`, а всё, что трогает `Document`, `Element`, `Transaction` или `UIDocument`, снаружи не проверить. RevitTestHost закрывает этот пробел: надстройка грузится в Revit один раз, находит тестовые сборки плагинов, сама открывает нужные модели и пишет отчёт.

## Состав

| Проект | Целевая платформа | Назначение |
|---|---|---|
| `RevitTestHost.Sdk` | netstandard2.0 | атрибуты и контракты для тестовых проектов плагинов, NuGet-пакет |
| `RevitTestHost.Core` | net47, net8.0-windows | ядро без Revit API и без внешних пакетов: JSON, отчёт, отбор тестов, протокол и сервер канала |
| `RevitTestHost.Addin` | net47, net48, net8.0-windows или net10.0-windows (по году Revit) | сама надстройка Revit |
| `RevitTestHost.Cli` | net8.0 | запуск Revit с тестами из консоли и разбор отчёта |

Core и Addin грузятся в процесс Revit рядом друг с другом, поэтому внешних зависимостей у них нет: сериализация JSON написана заново.

## Установка

Надстройка собирается под Revit с 2020 по 2027, под каждый год отдельная сборка (платформы и статус проверки в [versions.md](versions.md)). Вживую проверена на Revit 2020, 2022, 2025 и 2027.

Установщиком: запустите `RevitTestHost-Addin-<версия>.msi` из релиза. Он ставит надстройку для текущего пользователя (права администратора не нужны) в `%AppData%\Autodesk\Revit\Addins\<год>\` для каждого найденного Revit 2020–2027 (`C:\Program Files\Autodesk\Revit <год>\Revit.exe`). Если надстройка уже лежит в `%ProgramData%\Autodesk\Revit\Addins\<год>\` для Revit 2020–2026, установщик останавливается: та же надстройка в двух папках даёт повтор AddInId. Удаление — через «Приложения» Windows; папка `TestHost\Tests\` с тестовыми сборками при этом остаётся.

Из архива релиза: возьмите архив для своего года и распакуйте содержимое в `%ProgramData%\Autodesk\Revit\Addins\<год>\` (для Revit 2027 в `%AppData%\Autodesk\Revit\Addins\2027\`: из `%ProgramData%` Revit 2027 манифесты не загружает). В корне появится `RevitTestHost.addin`, рядом папка `TestHost\` с тремя сборками.

Из исходников: Debug-сборка сама копирует файлы в нужные места.

```powershell
dotnet build src/RevitTestHost.Addin/RevitTestHost.Addin.csproj -c Debug
```

Версия Revit по умолчанию 2022, другую задаёт параметр `-p:RevitVersion=2024`. Манифест `.addin` кладётся в корень `Addins\<версия>\`, потому что в подпапках Revit его не ищет, а сборки лежат в `TestHost\` (путь в манифесте `TestHost\RevitTestHost.Addin.dll`).

Тестовые сборки плагинов кладутся в `TestHost\Tests\`. Загружаются только файлы `*Tests.dll` из самой этой папки, без вложенных.

## Как писать тесты

Тестовый проект плагина ссылается на пакет `RevitTestHost.Sdk` (файл `.nupkg` из релиза подключается как локальный источник NuGet, либо ссылкой на проект `src/RevitTestHost.Sdk`) и на Revit API (пакеты `Nice3Point.Revit.Api.RevitAPI` и `Nice3Point.Revit.Api.RevitAPIUI` с `PrivateAssets="all"`). Собственную копию `RevitTestHost.Sdk.dll` в `TestHost\Tests\` класть нельзя: она сломает идентичность типов атрибутов. Поэтому в проекте стоит `CopyLocalLockFileAssemblies=false`, а в `Tests\` копируется только сборка с тестами. Пример проекта лежит в [samples/RevitTestHost.SampleTests](../samples/RevitTestHost.SampleTests).

```csharp
using RevitTestHost.Sdk.Attributes;
using RevitTestHost.Sdk.Contracts;

public class WallTests
{
    [RevitFact]
    public void Xyz_distance_is_computed()
    {
        // Revit запущен, модель не открыта
    }

    [RevitTest(Model = RevitTestAttribute.NewProject)]
    public void Wall_can_be_created(ITestContext ctx)
    {
        using (ctx.Transactions.Begin("create wall"))   // откатывается при Dispose
        {
            // ctx.Document: свежий метрический проект
        }
    }

    [RevitTest(Model = "TestModels/walls_basic.rvt")]
    public void Model_has_three_walls(ITestContext ctx)
    {
        // путь к модели считается от папки тестовой сборки
    }
}
```

Атрибуты:

- `[RevitFact]` тест без модели. `ctx.Document` и `ctx.UiDoc` равны `null`.
- `[RevitTest(Model = ...)]` тест с моделью. Значение `Model` это путь к `.rvt` относительно тестовой сборки, либо `RevitTestAttribute.NewProject` (`"@new"`), либо `RevitTestAttribute.ActiveDocument` (`"@active"`).
- `[TestModel("...")]` на классе задаёт модель по умолчанию для всех `[RevitTest]` без своей модели.
- У `[RevitFact]` и `[RevitTest]` есть свойство `Skip`: любое значение, кроме `null`, пропускает тест (статус `skipped`), а текст попадает в сообщение результата.

Модели:

- Файл `.rvt` открывается один раз и остаётся открытым между тестами с тем же путём, в конце прогона закрывается без сохранения. Если модель с таким путём уже открыта в Revit (в режиме сервера, например, клиентом через интерфейс), тест работает с ней, а хост её не открывает и не закрывает.
- `"@new"` создаёт свежий метрический проект программно, файл не нужен. Документ отбрасывается в конце прогона.
- `"@active"` работает с документом, активным в интерфейсе. Хост его не открывает и не закрывает. Если активного документа нет, тест получает статус `error`.

Метод теста имеет сигнатуру `void Name()` или `void Name(ITestContext ctx)`. В `ITestContext` доступны `UiApp`, `App`, `Document`, `UiDoc`, `Transactions` (транзакции с автоматическим откатом), `Logger`, `TestName`, `ModelPath` и `Parameters` (параметры прогона, в CLI и при запуске кнопкой ленты пустой словарь).

Статус результата: `passed`, `failed`, `skipped` или `error`. Исключение, в имени типа которого есть `Xunit`, `Assertion` или `FluentAssertions` (например, `AssertionException` из NUnit), даёт `failed`, любое другое исключение даёт `error`.

## Три способа запуска

Кнопка ленты. Надстройка создаёт вкладку `TestHost`, панель `Test Runner` и кнопку `Run All Tests`. Она прогоняет все найденные тесты, пишет JSON-отчёт в `%TEMP%\RevitTestHost\results_<время>.json` и показывает сводку в диалоге.

Консоль (CLI). Для регрессионных прогонов.

```powershell
RevitTestHost.Cli --revit-version 2022 --filter "*XYZ*;MyPlugin.RevitTests.Documents.*" --output results.json
```

| Параметр | Значение |
|---|---|
| `--revit-version` | версия установленного Revit, по умолчанию 2022 |
| `--revit` | явный путь к `Revit.exe`, перекрывает `--revit-version` |
| `--filter` | шаблоны через `;` со звёздочкой, сопоставляются с `Namespace.Class.Method` без учёта регистра и целиком; без фильтра идут все тесты |
| `--output` | путь JSON-отчёта (относительный путь считается от текущей папки), по умолчанию `%TEMP%\RevitTestHost\cli_results_<время>.json` |
| `--timeout-minutes` | сколько ждать завершения Revit, по умолчанию 45 |

CLI выставляет переменные окружения `REVITTESTHOST_CLI=1`, `REVITTESTHOST_FILTER` и `REVITTESTHOST_OUTPUT` и запускает `Revit.exe` с ключом `/language ENU`. Надстройка после события `ApplicationInitialized` прогоняет отфильтрованные тесты, пишет отчёт и завершает процесс Revit.

Коды выхода CLI: 0 если всё прошло, иначе число тестов со статусом `failed` и `error`, 101 при инфраструктурной ошибке (Revit не найден, таймаут, отчёт не появился), 102 при фатальной ошибке внутри прогона.

Режим сервера. Revit остаётся открытым и принимает команды через именованный канал. Подробности в [server-protocol.md](server-protocol.md).

## Обнаружение тестов

Посторонние сборки в `Tests\` прогон не ломают. Тип, ни один тест которого не пройдёт фильтр (начало шаблона до первой звёздочки не подходит к имени класса), даже не открывается. Подходящий тип, который не удалось осмотреть, даёт результат со статусом `error` и методом `(discovery)`. Сборки и типы, которые не загрузились, пропускаются с предупреждением. Предупреждения попадают в поле `warnings` отчёта (оно есть, только когда не пусто), в вывод CLI и в `server.log` режима сервера.

Группы тестов по моделям упорядочиваются порядковым сравнением строк (`StringComparer.Ordinal`), порядок выполнения не зависит от текущей культуры.

## Отчёт

JSON-отчёт содержит `summary` (`total`, `passed`, `failed`, `errored`, `skipped`, `durationMs`, `revitVersion`, `host`, `startedAt`), массив `results` и необязательное поле `warnings`. Поле `host` выглядит как `RevitTestHost 0.1.0`. Если прогон оборвался из-за фатальной ошибки, в корне отчёта появляется поле `error` с её текстом. Подробное описание полей в [server-protocol.md](server-protocol.md#результат-run).

## Проверка самого RevitTestHost

```powershell
dotnet test tests/RevitTestHost.Tests/RevitTestHost.Tests.csproj
```

Модульные тесты запускаются без Revit. Живая проверка режима сервера описана в [server-protocol.md](server-protocol.md#живая-проверка).

## Что пока не сделано

Параметризованные тесты (`[RevitTheory]` и `[InlineData]`), фикстуры уровня модели и окно с деревом тестов.
