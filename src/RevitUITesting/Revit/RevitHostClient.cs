using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RevitUITesting.Uia;

namespace RevitUITesting.Revit
{
    /// <summary>
    /// Клиент канала режима сервера RevitTestHost (контракт 1). Запросы строго по одному; ответ сопоставляется по id: запоздалый ответ
    /// на запрос, отвалившийся по таймауту, отбрасывается следующим запросом. Сервер не отвечает, пока Revit показывает модальное окно, —
    /// таймаут даёт <see cref="RevitBusyException"/> с перечнем окон Revit. После обрыва канала следующий вызов подключается заново.
    /// </summary>
    public sealed class RevitHostClient : IDisposable
    {
        public const string NewModel = "@new";

        public const string ActiveModel = "@active";

        private readonly object _sync = new object();
        private readonly Func<string> _describeWindows;
        private readonly Action _ensureAlive;
        private NamedPipeClientStream _pipe;
        private StreamReader _reader;
        private StreamWriter _writer;
        private Task<string> _pendingLine;
        private long _nextId;
        private bool _disposed;

        internal RevitHostClient(string pipeName, TimeSpan timeout, TimeSpan longTimeout, Func<string> describeWindows, Action ensureAlive)
        {
            if (string.IsNullOrEmpty(pipeName))
            {
                throw new ArgumentException("Нужно имя канала", nameof(pipeName));
            }
            if (timeout <= TimeSpan.Zero || longTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Таймауты канала должны быть положительными.");
            }
            PipeName = pipeName;
            Timeout = timeout;
            LongTimeout = longTimeout;
            _describeWindows = describeWindows;
            _ensureAlive = ensureAlive;
        }

        /// <summary>Сколько ждать подключения к каналу за одну попытку.</summary>
        internal static TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(2);

        public string PipeName { get; }

        /// <summary>Таймаут ping/documents/close (hostTimeoutSeconds).</summary>
        public TimeSpan Timeout { get; }

        /// <summary>Таймаут open/run (modelOpenTimeoutSeconds).</summary>
        public TimeSpan LongTimeout { get; }

        public HostPing Ping()
        {
            return Ping(Timeout);
        }

        internal HostPing Ping(TimeSpan timeout)
        {
            return HostProtocol.ReadPing(Send("ping", null, timeout));
        }

        /// <summary>Ждать, пока Revit простаивает (idle = true), не дольше <see cref="Timeout"/>.</summary>
        public HostPing WaitIdle()
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                HostPing ping = Ping();
                if (ping.Idle)
                {
                    return ping;
                }
                if (sw.Elapsed >= Timeout)
                {
                    throw new UiWaitTimeoutException("Revit простаивает (idle = true в ответе ping)", Timeout, sw.Elapsed, ping.ToString(), null);
                }
                Thread.Sleep(Wait.PollInterval);
            }
        }

        public IReadOnlyList<HostDocument> Documents()
        {
            return Documents(Timeout);
        }

        internal IReadOnlyList<HostDocument> Documents(TimeSpan timeout)
        {
            return HostProtocol.ReadDocuments(Send("documents", null, timeout));
        }

        /// <summary>Открыть и активировать в UI модель (полный путь) или новый проект «@new»; copyToTemp — открыть копию во временной папке прогона.</summary>
        public HostDocument Open(string pathOrNew, bool copyToTemp)
        {
            if (string.IsNullOrWhiteSpace(pathOrNew))
            {
                throw new ArgumentException("Нужен путь модели или «@new».", nameof(pathOrNew));
            }
            if (pathOrNew.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                throw new ArgumentException(@"Префикс \\?\ в процессе Revit 2022 даёт «Illegal characters in path» — передайте обычный путь: " + pathOrNew, nameof(pathOrNew));
            }
            if (pathOrNew != NewModel && !Path.IsPathRooted(pathOrNew))
            {
                throw new ArgumentException($"Путь модели должен быть полным: «{pathOrNew}».", nameof(pathOrNew));
            }
            UiLog.Action($"Revit: открыть {(pathOrNew == NewModel ? "новый проект" : pathOrNew)}{(copyToTemp ? " (копия во временной папке)" : "")}");
            HostDocument document = HostProtocol.ReadOpened(Send("open", new Dictionary<string, object> { ["path"] = pathOrNew, ["copyToTemp"] = copyToTemp }, LongTimeout));
            UiLog.Action("Revit: открыт документ " + document);
            return document;
        }

        /// <summary>Закрыть неактивный документ без сохранения по заголовку. Активный — ошибка ACTIVE_DOCUMENT (его закрывает UI).</summary>
        public void Close(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException("Нужен заголовок документа.", nameof(title));
            }
            CloseBy("title", title);
        }

        /// <summary>Закрыть неактивный документ без сохранения по полному пути.</summary>
        public void ClosePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Нужен путь документа.", nameof(path));
            }
            CloseBy("path", path);
        }

        /// <summary>Прогнать Revit-тесты RevitTestHost по фильтру на модели (по умолчанию — активный документ UI).</summary>
        public HostRunReport Run(string filter, IDictionary<string, string> parameters = null, string model = ActiveModel)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                throw new ArgumentException("Нужен фильтр Revit-тестов (Namespace.Class.Method, «*»).", nameof(filter));
            }
            Dictionary<string, string> values = new Dictionary<string, string>(parameters ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            UiLog.Action($"Revit-тесты «{filter}» на {model ?? ActiveModel}" + (values.Count == 0 ? "" : "; параметры: " + string.Join(", ", values.Select(p => p.Key + "=" + p.Value))));
            HostRunReport report = HostProtocol.ReadRun(Send("run", new Dictionary<string, object>
            {
                ["filter"] = filter,
                ["model"] = model ?? ActiveModel,
                ["parameters"] = values,
            }, LongTimeout));
            IReadOnlyList<string> warnings = report.Warnings ?? new string[0];
            UiLog.Action($"Revit-тесты «{filter}»: {report}" + (warnings.Count == 0 ? "" : "; предупреждения сервера: " + string.Join("; ", warnings)));
            return report;
        }

        /// <summary>Прогнать Revit-тесты на активном документе; ничего не найдено, ни один не прошёл или есть упавшие — провал NUnit с их текстами.</summary>
        public void AssertRun(string filter, IDictionary<string, string> parameters = null)
        {
            string failure = DescribeFailure(filter, Run(filter, parameters));
            if (failure != null)
            {
                throw new AssertionException(failure);
            }
        }

        /// <summary>Текст провала проверки (null — проверка прошла); предупреждения сервера (сборки и типы, пропущенные при обнаружении) — в конце текста.</summary>
        internal static string DescribeFailure(string filter, HostRunReport report)
        {
            string failure = null;
            if (report.Total == 0)
            {
                failure = $"Revit-тесты по фильтру «{filter}» не найдены (total = 0): проверьте фильтр и что сборка *Tests.dll лежит в TestHost\\Tests и загружается (см. предупреждения сервера).";
            }
            else if (report.Failures.Count > 0)
            {
                failure = $"Revit-тесты по фильтру «{filter}»: {report}." + Environment.NewLine + string.Join(Environment.NewLine, report.Failures.Select(f => "  " + f));
            }
            else if (report.Passed == 0)
            {
                failure = $"Revit-тесты по фильтру «{filter}»: ни один не прошёл ({report})." + Environment.NewLine + string.Join(Environment.NewLine, report.Results.Select(r => "  " + r));
            }
            IReadOnlyList<string> warnings = report.Warnings ?? new string[0];
            if (failure == null || warnings.Count == 0)
            {
                return failure;
            }
            return failure + Environment.NewLine + $"Предупреждения сервера RevitTestHost ({warnings.Count}):" + Environment.NewLine + string.Join(Environment.NewLine, warnings.Select(w => "  " + w));
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                Drop();
            }
        }

        private void CloseBy(string key, string value)
        {
            UiLog.Action($"Revit: закрыть документ без сохранения ({key} = {value})");
            JsonElement result = Send("close", new Dictionary<string, object> { [key] = value, ["save"] = false }, Timeout);
            if (!HostProtocol.ReadClosed(result))
            {
                throw new InvalidDataException($"RevitTestHost ответил closed = false на закрытие документа ({key} = {value}).");
            }
        }

        private JsonElement Send(string command, IReadOnlyDictionary<string, object> args, TimeSpan timeout)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(RevitHostClient));
                }
                long id = ++_nextId;
                string line = HostProtocol.Request(id, command, args);
                Stopwatch sw = Stopwatch.StartNew();
                EnsureConnected(command);
                try
                {
                    _writer.Write(line);
                    _writer.Write('\n');
                    _writer.Flush();
                }
                catch (IOException ex)
                {
                    Drop();
                    _ensureAlive?.Invoke();
                    throw new IOException($"Канал RevitTestHost «{PipeName}» закрыт при отправке «{command}»: {ex.Message}", ex);
                }
                HostResponse response = ReadResponse(id, command, timeout, sw);
                if (!response.Ok)
                {
                    throw new HostErrorException(command, response.ErrorCode, response.ErrorMessage);
                }
                return response.Result;
            }
        }

        private void EnsureConnected(string command)
        {
            if (_pipe != null && _pipe.IsConnected)
            {
                return;
            }
            Drop();
            NamedPipeClientStream pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                pipe.Connect((int)ConnectTimeout.TotalMilliseconds);
            }
            catch (TimeoutException ex)
            {
                pipe.Dispose();
                _ensureAlive?.Invoke();
                throw new TimeoutException($"Канал RevitTestHost «{PipeName}» не принял подключение за {ConnectTimeout.TotalSeconds:0.#} с (команда «{command}»): режим сервера не запущен, Revit ещё стартует или канал занят другим клиентом.", ex);
            }
            _pipe = pipe;
            _reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            _writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true);
            _pendingLine = null;
            UiLog.Action("подключение к каналу RevitTestHost " + PipeName);
        }

        private HostResponse ReadResponse(long id, string command, TimeSpan timeout, Stopwatch sw)
        {
            while (true)
            {
                if (_pendingLine == null)
                {
                    _pendingLine = _reader.ReadLineAsync();
                }
                TimeSpan left = timeout - sw.Elapsed;
                bool completed;
                try
                {
                    completed = _pendingLine.Wait(left > TimeSpan.Zero ? left : TimeSpan.Zero);
                }
                catch (AggregateException ex)
                {
                    Exception inner = ex.InnerException ?? ex;
                    Drop();
                    _ensureAlive?.Invoke();
                    throw new IOException($"Канал RevitTestHost «{PipeName}» закрыт при ожидании ответа на «{command}»: {inner.Message}", inner);
                }
                if (!completed)
                {
                    // чтение строки продолжается: оно достанется следующему запросу, и запоздалый ответ будет отброшен по id
                    _ensureAlive?.Invoke();
                    throw new RevitBusyException(command, timeout, sw.Elapsed, DescribeWindows());
                }
                string line = _pendingLine.Result;
                _pendingLine = null;
                if (line == null)
                {
                    Drop();
                    _ensureAlive?.Invoke();
                    throw new IOException($"Канал RevitTestHost «{PipeName}» закрыт сервером до ответа на «{command}» (Revit закрылся или сервер перезапустился).");
                }
                HostResponse response = HostProtocol.ParseResponse(line);
                if (response.Id == id)
                {
                    return response;
                }
                if (response.Id == 0 && !response.Ok)
                {
                    // сервер не смог прочитать id запроса и ответил с id 0 (BAD_REQUEST, см. docs/server-protocol.md); запросы идут строго по одному — это ответ на текущий
                    return response;
                }
                if (response.Id < id)
                {
                    UiLog.Warn($"Отброшен запоздалый ответ RevitTestHost на запрос #{response.Id} (ждём #{id}, «{command}»).");
                    continue;
                }
                throw new InvalidDataException($"RevitTestHost ответил на запрос #{response.Id}, которого клиент не отправлял (ждём #{id}, «{command}»).");
            }
        }

        private string DescribeWindows()
        {
            if (_describeWindows == null)
            {
                return "";
            }
            try
            {
                return _describeWindows();
            }
            catch (Exception ex) when (UiFault.IsTransient(ex))
            {
                return "окна Revit не перечислены: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private void Drop()
        {
            // reader и writer созданы с leaveOpen и своих ресурсов не держат; буфер writer пуст (каждая запись заканчивается Flush).
            // Их Dispose нельзя вызывать: StreamWriter.Dispose делает Flush в канал, а на оборванном канале (Broken) он бросает
            // IOException «Канал нарушен» — до ensureAlive и из Dispose клиента. Освобождается только сам канал.
            _pipe?.Dispose();
            _reader = null;
            _writer = null;
            _pipe = null;
            _pendingLine = null;
        }
    }
}
