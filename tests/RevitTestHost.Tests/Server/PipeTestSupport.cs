using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RevitTestHost.Core.Json;
using RevitTestHost.Core.Protocol;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Tests.Server
{
    /// <summary>Журнал в памяти.</summary>
    internal sealed class ListLog : IServerLog
    {
        public readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();
        public void Info(string message) => Lines.Enqueue("INFO " + message);
        public void Error(string message, Exception? exception = null) => Lines.Enqueue("ERROR " + message + " " + exception?.Message);
        public bool Contains(string text) => Lines.Any(l => l.Contains(text));
    }

    /// <summary>
    /// Подмена потока Revit: TryRaise будит отдельный поток, который выполняет Drain — как ExternalEvent.
    /// </summary>
    internal sealed class FakeRevitThread : IExternalEventRaiser, IDisposable
    {
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly Thread _thread;
        private volatile bool _stop;

        public FakeRevitThread(Func<RequestQueue> queue, IRequestHandler handler)
        {
            _thread = new Thread(() =>
            {
                while (true)
                {
                    _signal.WaitOne();
                    if (_stop) return;
                    queue().Drain(handler);
                }
            }) { IsBackground = true, Name = "fake Revit thread" };
            _thread.Start();
        }

        public bool TryRaise(out string failureReason)
        {
            failureReason = "";
            _signal.Set();
            return true;
        }

        public void Dispose()
        {
            _stop = true;
            _signal.Set();
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// Обработчик-заглушка: отвечает {"cmd":..}, команда "block" ждёт <see cref="Release"/>,
    /// команда "null" возвращает null (непредвиденный сбой: поток записи ответов падает).
    /// </summary>
    internal sealed class GateHandler : IRequestHandler
    {
        private readonly ManualResetEventSlim _gate = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false);

        public void Release() => _gate.Set();

        public HostResponse Handle(HostRequest request, bool idle)
        {
            if (request.Command == "block")
            {
                Entered.Set();
                if (!_gate.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("тест не отпустил обработчик");
            }
            if (request.Command == "null") return null!;
            return HostResponse.Success(request.Id, new JsonPayload(w => w.BeginObject().Property("cmd", request.Command).EndObject()));
        }
    }

    /// <summary>Клиент канала для тестов: строки UTF-8, асинхронное чтение с таймаутом.</summary>
    internal sealed class TestPipeClient : IDisposable
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly NamedPipeClientStream _pipe;
        private readonly StreamReader _reader;

        private TestPipeClient(NamedPipeClientStream pipe)
        {
            _pipe = pipe;
            _reader = new StreamReader(pipe, Utf8NoBom, false, 4096, leaveOpen: true);
        }

        public static TestPipeClient Connect(string pipeName, int timeoutMs = 5000)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(timeoutMs);
            return new TestPipeClient(pipe);
        }

        public void SendRaw(byte[] bytes)
        {
            _pipe.Write(bytes, 0, bytes.Length);
            _pipe.Flush();
        }

        public void Send(string line) => SendRaw(Utf8NoBom.GetBytes(line + "\n"));

        public Task<string?> ReadLineAsync() => _reader.ReadLineAsync();

        public Dictionary<string, object?> Read(int timeoutMs = 5000)
        {
            var task = ReadLineAsync();
            if (!task.Wait(timeoutMs)) throw new TimeoutException("нет ответа от сервера канала за " + timeoutMs + " мс");
            return Parse(task.Result);
        }

        public static Dictionary<string, object?> Parse(string? line)
        {
            if (line == null) throw new EndOfStreamException("сервер закрыл канал");
            return (Dictionary<string, object?>)JsonReader.Parse(line)!;
        }

        public void Dispose()
        {
            _reader.Dispose();
            _pipe.Dispose();
        }
    }
}
