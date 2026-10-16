# Режим сервера RevitTestHost

В режиме сервера Revit остаётся открытым, а надстройка принимает команды через именованный канал (named pipe). Так работает RevitUITesting: тест запускает Revit, прокликивает интерфейс плагина и просит надстройку проверить состояние модели.

```powershell
$env:REVITTESTHOST_SERVE = "RevitTestHost-" + [guid]::NewGuid().ToString("N")
& "C:\Program Files\Autodesk\Revit 2022\Revit.exe" /language ENU
```

## Включение и канал

- Режим включает переменная окружения процесса Revit `REVITTESTHOST_SERVE=<имя канала>`. В имени допустимы латиница, цифры, `-` и `_`, длина до 128 символов. Клиент генерирует имя вида `RevitTestHost-<guid без дефисов>`. С недопустимым именем сервер не поднимается.
- Вместе с `REVITTESTHOST_CLI=1` режим сервера не включается: ошибка пишется в `%TEMP%\RevitTestHost\serve\startup.log` и в журнал Revit, работает только CLI-режим.
- Канал `\\.\pipe\<имя>` создаётся на событии `ApplicationInitialized`. Он двунаправленный, байтовый, доступен только текущему пользователю (ACL на SID), сетевые подключения запрещены. За один раз принимается одно подключение, после разрыва сервер снова ждёт клиента.

## Формат сообщений

Строки UTF-8 без BOM (BOM в начале потока допускается). Одна строка это один JSON, она завершается `\n`.

```json
{"id":7,"cmd":"ping","args":{}}
{"id":7,"ok":true,"result":{}}
{"id":7,"ok":false,"error":{"code":"NOT_FOUND","message":"..."}}
```

Нумеруйте запросы с 1. Строка, из которой не читается `id`, получает ответ `BAD_REQUEST` с `"id":0`.

## Команды

| Команда | `args` | `result` |
|---|---|---|
| `ping` | нет | `serverVersion` (`"1.0"`), `revitVersion`, `pid`, `idle`, `activeDocument` (`title`, `path`, `isFamilyDocument`) или `null` |
| `documents` | нет | `documents`: `title`, `path`, `isActive`, `isModified`, `isFamilyDocument` (связанные файлы не входят) |
| `open` | `path` (абсолютный) или `"@new"`, `copyToTemp` | `title`, `path`. Документ открывается и активируется в интерфейсе. `@new` создаёт метрический проект `new-<n>.rvt` во временной папке канала, `copyToTemp` открывает копию файла в той же папке |
| `close` | `title` или `path`, `save` (допустимо только `false`) | `closed: true`. Активный документ закрыть нельзя, ответ `ACTIVE_DOCUMENT` (Revit API его не закрывает) |
| `run` | `filter`, `model` (по умолчанию `"@active"`, иначе заголовок или путь открытого документа), `parameters` (строки) | отчёт в формате JSON-отчёта CLI: `summary`, `results`, `warnings` |

### Результат `run`

- `summary`: `total`, `passed`, `failed`, `errored`, `skipped`, `durationMs`, `revitVersion`, `host`, `startedAt` (ISO 8601).
- `results[]`: `class`, `method`, `model` (если задана), `status` (`passed`, `failed`, `skipped` или `error`), `message` и `stackTrace` (если есть), `durationMs`.
- `warnings`: массив строк, поле есть только когда массив не пуст. Это сборки и типы, пропущенные при обнаружении тестов.
- Строка `results` с `"method":"(discovery)"` означает ошибку обнаружения тестов в типе, а не тест.

`run` без `filter` запускает все развёрнутые тесты из `TestHost\Tests\` (как CLI без фильтра), в том числе тесты других плагинов, которые открывают модели. Revit при этом надолго занят, а клиент видит таймаут. Клиенту стоит всегда передавать `filter`.

## Ошибки

Коды: `BAD_REQUEST`, `UNKNOWN_COMMAND`, `NOT_FOUND`, `ACTIVE_DOCUMENT`, `REVIT_ERROR` (текст в `message`).

`REVIT_ERROR` приходит, когда:

- команда бросила исключение в потоке Revit (исключение Revit API);
- Revit отказал в `ExternalEvent.Raise`;
- Revit завершается (см. ниже);
- произошла внутренняя ошибка очереди или формирования ответа.

## Занятость и простой

Все команды, включая `ping`, выполняются в потоке Revit через `ExternalEvent`. Пока открыто модальное окно, ответа нет. Ошибки `BUSY` нет, таймаут выбирает клиент. Ответы одного подключения идут в порядке запросов. Клиент может отключиться по таймауту и подключиться снова: ответ на старый запрос в новое подключение не попадёт (об этом остаётся запись в `server.log`).

Поле `idle` в ответе `ping` равно `true`, если после предыдущего запроса сработало событие `Idling` и в очереди нет других запросов.

## Клиент на .NET Framework

Открывайте `NamedPipeClientStream` с `PipeOptions.Asynchronous`. Подключение к каналу, который занят другим клиентом или ещё не создан, заканчивается `IOException` или `TimeoutException`, клиент должен обработать оба типа. Готовый клиент есть в RevitUITesting (`RevitHostClient`).

## Папка канала

`%TEMP%\RevitTestHost\serve\<имя>\` содержит `server.log`, файлы `new-<n>.rvt` и копии `copyToTemp`. Сама папка не очищается. `%TEMP%` здесь и у `startup.log` это временная папка пользователя (значение `TEMP` из его окружения, иначе `%LOCALAPPDATA%\Temp`), а не папка сеанса `%TEMP%\<guid>\`, которую Revit назначает своему процессу.

## Завершение Revit

Запросы, которые ещё ждут в очереди, получают `REVIT_ERROR` («Revit завершается, запрос не выполнен.»), пока клиент подключён. О каждом таком запросе остаётся строка в `server.log`.

## Документ `@active` и параметры

Тест с `Model = RevitTestAttribute.ActiveDocument` (`"@active"`) работает с документом, активным в интерфейсе Revit. Хост этот документ не открывает и не закрывает. Параметры из `run.parameters` доступны через `ctx.Parameters`.

```csharp
[RevitTest(Model = RevitTestAttribute.ActiveDocument)]
public void FamilyLoaded(ITestContext ctx)
{
    var family = ctx.Parameters["family"];   // из run.parameters
    // ctx.Document и ctx.UiDoc: документ, активный в интерфейсе
}
```

Если активного документа нет, тест получает статус `error` с пояснением. В CLI-режиме активного документа нет, а `ctx.Parameters` пуст. Тот же пустой словарь тест получает при запуске кнопкой ленты.

## Живая проверка

Скрипт [tools/serve-smoke.ps1](../tools/serve-smoke.ps1) запускает Revit в режиме сервера и проходит по командам. Условия: Revit нужного года (параметр `-RevitVersion`, по умолчанию 2022, вживую проверены 2020, 2022, 2025 и 2027), Revit не запущен, свободный commit не меньше 5 ГБ, собраны в конфигурации Debug надстройка и пример `samples/RevitTestHost.SampleTests`.

```powershell
dotnet build src/RevitTestHost.Addin/RevitTestHost.Addin.csproj -c Debug
dotnet build samples/RevitTestHost.SampleTests/RevitTestHost.SampleTests.csproj -c Debug
powershell -ExecutionPolicy Bypass -File tools/serve-smoke.ps1
```

Пока пример лежит в `TestHost\Tests\`, его тест попадает и в прогоны CLI без фильтра, где он даёт `error` (нет активного документа).
