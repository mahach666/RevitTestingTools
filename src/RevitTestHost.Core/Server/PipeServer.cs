using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RevitTestHost.Core.Protocol;

namespace RevitTestHost.Core.Server
{
    /// <summary>
    /// Сервер named pipe режима сервера (контракт 1): <c>\\.\pipe\&lt;имя&gt;</c>, InOut, Byte, доступ только
    /// текущему пользователю, одно подключение за раз, после разрыва — снова ожидание подключения.
    /// Строки UTF-8 без BOM, одна строка — один JSON. Каждая строка разбирается здесь же (ошибка формата —
    /// ответ <c>BAD_REQUEST</c>), корректный запрос уходит в <see cref="RequestQueue"/>; ответ пишется, когда поток
    /// Revit его выполнил. Запросы можно слать, не дожидаясь ответов: ответы идут в порядке запросов и несут их id.
    /// </summary>
    public sealed class PipeServer : IDisposable
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

        private readonly RequestQueue _queue;
        private readonly IServerLog _log;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _streamLock = new object();
        private NamedPipeServerStream? _current;
        private PipeConnection? _connection;
        private Thread? _thread;

        public PipeServer(string pipeName, RequestQueue queue, IServerLog log)
        {
            if (!ServeWorkspace.IsValidPipeName(pipeName))
                throw new ArgumentException($"Недопустимое имя канала '{pipeName}'.", nameof(pipeName));
            PipeName = pipeName;
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public string PipeName { get; }

        /// <summary>Запускает фоновый поток сервера. Канал создаётся в нём же; ошибка создания пишется в журнал.</summary>
        public void Start()
        {
            if (_thread != null) throw new InvalidOperationException("Сервер канала уже запущен.");
            _thread = new Thread(Loop) { IsBackground = true, Name = "RevitTestHost pipe server" };
            _thread.Start();
        }

        /// <summary>Сколько остановка ждёт отправки уже готовых ответов текущему клиенту.</summary>
        private static readonly TimeSpan ReadyResponsesTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Останавливает сервер: отправляет текущему клиенту уже готовые ответы (например, <c>REVIT_ERROR</c> после
        /// <see cref="RequestQueue.Shutdown"/>), отменяет ожидание подключения и закрывает текущее подключение.
        /// Ответы, которые ещё выполняются в потоке Revit, не ждёт.
        /// </summary>
        public void Dispose()
        {
            PipeConnection? connection;
            lock (_streamLock)
            {
                connection = _connection;
            }
            if (connection != null && !connection.WaitForReadyResponses(ReadyResponsesTimeout))
                _log.Error($"Готовые ответы не отправлены клиенту за {ReadyResponsesTimeout.TotalSeconds:0} с; канал закрывается.");

            _cts.Cancel();
            lock (_streamLock)
            {
                _current?.Dispose();
            }
            if (_thread != null && !_thread.Join(TimeSpan.FromSeconds(5)))
                _log.Error("Поток сервера канала не завершился за 5 с.");
        }

        /// <summary>DACL канала: полный доступ только SID текущего пользователя процесса и явный запрет для сетевых подключений.</summary>
        internal static PipeSecurity CreateSecurity()
        {
            var user = WindowsIdentity.GetCurrent().User
                       ?? throw new InvalidOperationException("Не удалось определить SID текущего пользователя для ACL канала.");
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
            // Запрет сетевых подключений: тот же пользователь не должен попасть в канал с другой машины (SMB, S-1-5-2)
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            return security;
        }

        internal static NamedPipeServerStream CreateStream(string pipeName)
        {
#if NETFRAMEWORK
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 0, 0, CreateSecurity());
#else
            // В .NET 8 и новее конструктора с PipeSecurity нет: ACL задаётся через NamedPipeServerStreamAcl
            return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 0, 0, CreateSecurity());
#endif
        }

        private void Loop()
        {
            try
            {
                _log.Info($"Сервер канала запущен: \\\\.\\pipe\\{PipeName}");
                while (!_cts.IsCancellationRequested)
                {
                    NamedPipeServerStream stream;
                    try
                    {
                        stream = CreateStream(PipeName);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        _log.Error($"Не удалось создать канал '{PipeName}' (занят другим процессом?). Сервер остановлен.", ex);
                        return;
                    }

                    lock (_streamLock)
                    {
                        if (_cts.IsCancellationRequested)
                        {
                            stream.Dispose();
                            return;
                        }
                        _current = stream;
                    }

                    try
                    {
                        if (!WaitForConnection(stream)) return;
                        _log.Info("Клиент подключился.");
                        Serve(stream);
                        _log.Info(_cts.IsCancellationRequested
                            ? "Подключение закрыто: сервер останавливается."
                            : "Клиент отключился; ждём следующее подключение.");
                    }
                    finally
                    {
                        lock (_streamLock)
                        {
                            _current = null;
                        }
                        stream.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                // Последний рубеж: необработанное исключение фонового потока завершило бы процесс Revit
                _log.Error("Сервер канала остановлен из-за непредвиденной ошибки.", ex);
            }
            finally
            {
                _log.Info("Сервер канала остановлен.");
            }
        }

        /// <summary>false — сервер останавливается.</summary>
        private bool WaitForConnection(NamedPipeServerStream stream)
        {
            try
            {
                stream.WaitForConnectionAsync(_cts.Token).GetAwaiter().GetResult();
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (ObjectDisposedException) when (_cts.IsCancellationRequested)
            {
                return false;
            }
        }

        private void Serve(NamedPipeServerStream stream)
        {
            var connection = new PipeConnection(stream, _log);
            lock (_streamLock)
            {
                _connection = connection;
            }
            try
            {
                // BOM в начале потока (клиент со StreamWriter(Encoding.UTF8)) пропускается, а не ломает первый запрос
                using (var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
                {
                    while (true)
                    {
                        string? line;
                        try
                        {
                            line = reader.ReadLine();
                        }
                        catch (IOException ex)
                        {
                            _log.Info($"Чтение из канала прервано: {ExceptionText.Describe(ex)}");
                            return;
                        }
                        catch (ObjectDisposedException) when (_cts.IsCancellationRequested || connection.IsClosed)
                        {
                            // Сервер останавливается или поток отправки ответов упал и закрыл канал
                            return;
                        }

                        if (line == null) return; // клиент закрыл канал
                        if (line.Trim().Length == 0) continue;

                        if (ProtocolCodec.TryParseRequest(line, out var request, out var error))
                            connection.Post(request!.Id, _queue.Enqueue(request));
                        else
                            connection.Post(error!.Id, Task.FromResult(error));
                    }
                }
            }
            finally
            {
                lock (_streamLock)
                {
                    _connection = null;
                }
                connection.Close();
            }
        }
    }

    /// <summary>
    /// Отправка ответов одного подключения строго в порядке поступления запросов: отдельный поток ждёт
    /// ответы по очереди и пишет их в канал. После разрыва ответы не пишутся — только запись в журнал.
    /// </summary>
    internal sealed class PipeConnection
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly Stream _stream;
        private readonly IServerLog _log;
        private readonly BlockingCollection<KeyValuePair<long, Task<HostResponse>>> _outgoing =
            new BlockingCollection<KeyValuePair<long, Task<HostResponse>>>();
        private readonly object _lock = new object();

        /// <summary>Ответы, поставленные в отправку и ещё не отправленные, в порядке отправки (под <see cref="_lock"/>).</summary>
        private readonly Queue<Task<HostResponse>> _unsent = new Queue<Task<HostResponse>>();
        private bool _closed;

        public PipeConnection(Stream stream, IServerLog log)
        {
            _stream = stream;
            _log = log;
            new Thread(WriteLoop) { IsBackground = true, Name = "RevitTestHost pipe writer" }.Start();
        }

        /// <summary>Ставит ответ в очередь отправки (задача ответа может ещё выполняться в потоке Revit).</summary>
        public void Post(long requestId, Task<HostResponse> response)
        {
            lock (_lock)
            {
                _unsent.Enqueue(response);
            }
            _outgoing.Add(new KeyValuePair<long, Task<HostResponse>>(requestId, response));
        }

        /// <summary>Подключение закрыто: клиент отключился, сервер останавливается или упал поток отправки ответов.</summary>
        public bool IsClosed
        {
            get
            {
                lock (_lock)
                {
                    return _closed;
                }
            }
        }

        /// <summary>Подключение разорвано: новые ответы не принимаются, ожидающие будут только записаны в журнал.</summary>
        public void Close()
        {
            lock (_lock)
            {
                _closed = true;
                Monitor.PulseAll(_lock);
            }
            _outgoing.CompleteAdding();
        }

        /// <summary>
        /// Ждёт, пока будут отправлены ответы, готовые к отправке: очередной ответ уже вычислен.
        /// Возвращается, когда неотправленных нет, очередной ещё выполняется (его не ждём) или подключение закрыто.
        /// false — истёк <paramref name="timeout"/>.
        /// </summary>
        public bool WaitForReadyResponses(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            lock (_lock)
            {
                while (!_closed && _unsent.Count > 0 && _unsent.Peek().IsCompleted)
                {
                    var left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) return false;
                    Monitor.Wait(_lock, left);
                }
                return true;
            }
        }

        private void WriteLoop()
        {
            try
            {
                foreach (var item in _outgoing.GetConsumingEnumerable())
                {
                    HostResponse response;
                    try
                    {
                        response = item.Value.GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        response = HostResponse.Failure(item.Key, ErrorCodes.RevitError, "Внутренняя ошибка очереди: " + ExceptionText.Describe(ex));
                    }
                    Send(response);
                    lock (_lock)
                    {
                        _unsent.Dequeue();
                        Monitor.PulseAll(_lock);
                    }
                }
            }
            catch (Exception ex)
            {
                // Последний рубеж: необработанное исключение фонового потока завершило бы процесс Revit
                _log.Error("Поток отправки ответов остановлен из-за непредвиденной ошибки.", ex);
                lock (_lock)
                {
                    _closed = true;
                    Monitor.PulseAll(_lock);
                }
                // Ответов больше не будет: закрываем канал, чтобы клиент получил конец потока, а не ждал до таймаута
                try
                {
                    _stream.Dispose();
                }
                catch (IOException closeEx)
                {
                    _log.Error("Не удалось закрыть канал после остановки потока отправки ответов.", closeEx);
                }
            }
        }

        private void Send(HostResponse response)
        {
            string line;
            try
            {
                line = ProtocolCodec.FormatResponse(response);
            }
            catch (Exception ex)
            {
                _log.Error($"Не удалось сформировать ответ на запрос {response.Id}.", ex);
                line = ProtocolCodec.FormatResponse(HostResponse.Failure(response.Id, ErrorCodes.RevitError,
                    "Не удалось сформировать ответ: " + ExceptionText.Describe(ex)));
            }

            var bytes = Utf8NoBom.GetBytes(line + "\n");
            bool closed;
            lock (_lock)
            {
                closed = _closed;
            }
            if (closed)
            {
                _log.Info($"Ответ на запрос {response.Id} не отправлен: клиент отключился.");
                return;
            }

            // Запись — вне _lock: пишет только этот поток, а клиент, который не читает, не должен блокировать
            // Close и WaitForReadyResponses (иначе остановка Revit зависла бы). Закрытый поток канала — исключение ниже.
            try
            {
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                lock (_lock)
                {
                    _closed = true;
                    Monitor.PulseAll(_lock);
                }
                _log.Info($"Ответ на запрос {response.Id} не отправлен: {ExceptionText.Describe(ex)}");
            }
        }
    }
}
