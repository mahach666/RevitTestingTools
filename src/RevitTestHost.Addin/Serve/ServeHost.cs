using System;
using Autodesk.Revit.UI;
using RevitTestHost.Core.Protocol;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Addin.Serve
{
    /// <summary>
    /// Режим сервера внутри Revit: очередь запросов, ExternalEvent, сервер канала и журнал
    /// <c>%TEMP%\RevitTestHost\serve\&lt;канал&gt;\server.log</c> (temp пользователя, не папка сеанса Revit).
    /// Создаётся на ApplicationInitialized.
    /// </summary>
    internal sealed class ServeHost : IDisposable
    {
        private readonly RequestQueue _queue;
        private readonly ExternalEvent _event;
        private readonly PipeServer _server;
        private readonly IServerLog _log;
        private readonly AutomationPeerWarmer _peers;

        private ServeHost(RequestQueue queue, ExternalEvent externalEvent, PipeServer server, IServerLog log)
        {
            _queue = queue;
            _event = externalEvent;
            _server = server;
            _log = log;
            _peers = AutomationPeerWarmer.Start(log);
        }

        public static ServeHost Start(string pipeName, string revitVersion)
        {
            var workspace = ServeWorkspace.ForPipe(pipeName);
            workspace.EnsureCreated();
            var log = new FileServerLog(workspace.LogPath);

            var raiser = new ExternalEventRaiser();
            var queue = new RequestQueue(raiser);
            var externalEvent = ExternalEvent.Create(new ServeEventHandler(queue, workspace, log));
            raiser.Event = externalEvent;

            PipeServer server;
            try
            {
                server = new PipeServer(pipeName, queue, log);
                server.Start();
            }
            catch
            {
                // Сервер не поднялся — освобождаем событие; исходная ошибка уходит вызывающему
                externalEvent.Dispose();
                throw;
            }

            log.Info($"Режим сервера включён: Revit {revitVersion}, папка {workspace.RootDirectory}, протокол {ServerInfo.ProtocolVersion}.");
            return new ServeHost(queue, externalEvent, server, log);
        }

        /// <summary>
        /// Из обработчика Idling (поток Revit). Возвращает true, если наблюдение простоя для <c>ping.idle</c> было нужно
        /// (после завершённого запроса Idling ещё не приходил) — тогда вызывающий просит у Revit следующий Idling без задержки.
        /// </summary>
        public bool OnIdling()
        {
            // Читаем ДО OnIdling: он сбрасывает признак
            var observationPending = _queue.IdleObservationPending;
            _queue.OnIdling();
            return observationPending;
        }

        public void Dispose()
        {
            _log.Info("Revit завершается: останавливаем сервер канала.");
            _peers.Dispose();
            // Сначала ответы на невыполненные запросы (пока клиент ещё подключён), потом остановка канала
            _queue.Shutdown(_log);
            _server.Dispose();
            _event.Dispose();
        }

        private sealed class ExternalEventRaiser : IExternalEventRaiser
        {
            public ExternalEvent? Event { get; set; }

            public bool TryRaise(out string failureReason)
            {
                if (Event == null)
                {
                    failureReason = "ExternalEvent ещё не создан";
                    return false;
                }

                var result = Event.Raise();
                failureReason = result.ToString();
                return result == ExternalEventRequest.Accepted || result == ExternalEventRequest.Pending;
            }
        }

        private sealed class ServeEventHandler : IExternalEventHandler
        {
            private readonly RequestQueue _queue;
            private readonly ServeWorkspace _workspace;
            private readonly IServerLog _log;

            public ServeEventHandler(RequestQueue queue, ServeWorkspace workspace, IServerLog log)
            {
                _queue = queue;
                _workspace = workspace;
                _log = log;
            }

            public void Execute(UIApplication app)
            {
                try
                {
                    _queue.Drain(new RequestDispatcher(new RevitRequestExecutor(app, _log), _workspace));
                }
                catch (Exception ex)
                {
                    // Drain сам превращает ошибки запросов в ответы; сюда попадает только сбой самой очереди
                    _log.Error("Сбой обработчика ExternalEvent режима сервера: " + ExceptionText.Describe(ex), ex);
                }
            }

            public string GetName() => "RevitTestHost: команды канала";
        }
    }
}
