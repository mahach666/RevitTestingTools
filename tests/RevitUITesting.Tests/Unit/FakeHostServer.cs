using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>
    /// Сервер канала по контракту 1 внутри процесса тестов: читает строки-запросы, отвечает функцией respond (null — не отвечать, как Revit
    /// с модальным окном), умеет отправить строку вручную (запоздалый ответ) и оборвать подключение (после этого снова ждёт клиента).
    /// </summary>
    internal sealed class FakeHostServer : IDisposable
    {
        private readonly Func<JsonElement, string> _respond;
        private readonly Thread _thread;
        private readonly object _sync = new object();
        private readonly List<string> _requests = new List<string>();
        private NamedPipeServerStream _current;
        private StreamWriter _writer;
        private volatile bool _stopping;
        private int _connections;

        internal FakeHostServer(Func<JsonElement, string> respond)
        {
            PipeName = "RevitUITesting.Tests-" + Guid.NewGuid().ToString("N");
            _respond = respond;
            _thread = new Thread(Serve) { IsBackground = true, Name = "FakeHostServer" };
            _thread.Start();
        }

        internal string PipeName { get; }

        internal int Connections => Volatile.Read(ref _connections);

        internal IReadOnlyList<string> Requests()
        {
            lock (_sync)
            {
                return _requests.ToArray();
            }
        }

        internal static string Ok(JsonElement request, string resultJson)
        {
            return "{\"id\":" + request.GetProperty("id").GetInt64() + ",\"ok\":true,\"result\":" + resultJson + "}";
        }

        internal static string Error(JsonElement request, string code, string message)
        {
            return "{\"id\":" + request.GetProperty("id").GetInt64() + ",\"ok\":false,\"error\":{\"code\":\"" + code + "\",\"message\":\"" + message + "\"}}";
        }

        internal static string Ping(long id, int pid, bool idle = true)
        {
            return "{\"id\":" + id + ",\"ok\":true,\"result\":{\"serverVersion\":\"1.0\",\"revitVersion\":\"2022\",\"pid\":" + pid + ",\"idle\":" + (idle ? "true" : "false") + ",\"activeDocument\":null}}";
        }

        /// <summary>Отправить строку текущему клиенту (например, запоздалый ответ).</summary>
        internal void Send(string line)
        {
            lock (_sync)
            {
                if (_writer == null)
                {
                    throw new InvalidOperationException("FakeHostServer: клиента нет — отправлять некому.");
                }
                _writer.Write(line + "\n");
                _writer.Flush();
            }
        }

        /// <summary>Оборвать текущее подключение; сервер снова ждёт клиента.</summary>
        internal void DropClient()
        {
            lock (_sync)
            {
                _current?.Dispose();
                _current = null;
                _writer = null;
            }
        }

        public void Dispose()
        {
            _stopping = true;
            DropClient();
            if (!_thread.Join(TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("FakeHostServer: поток сервера не завершился за 5 с.");
            }
        }

        private void Serve()
        {
            while (!_stopping)
            {
                NamedPipeServerStream server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                Task connect = server.WaitForConnectionAsync();
                while (!connect.Wait(100))
                {
                    if (_stopping)
                    {
                        server.Dispose();
                        return;
                    }
                }
                Interlocked.Increment(ref _connections);
                lock (_sync)
                {
                    _current = server;
                    _writer = new StreamWriter(server, new UTF8Encoding(false), 4096, true);
                }
                StreamReader reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, true);
                while (!_stopping)
                {
                    string line;
                    try
                    {
                        line = reader.ReadLine();
                    }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
                    {
                        // подключение оборвано (DropClient или клиент закрыл канал) — ждём следующего клиента
                        break;
                    }
                    if (line == null)
                    {
                        break;
                    }
                    lock (_sync)
                    {
                        _requests.Add(line);
                    }
                    string reply;
                    using (JsonDocument doc = JsonDocument.Parse(line))
                    {
                        reply = _respond(doc.RootElement);
                    }
                    if (reply != null)
                    {
                        Send(reply);
                    }
                }
                DropClient();
            }
        }
    }
}
