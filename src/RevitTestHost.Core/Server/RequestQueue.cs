using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RevitTestHost.Core.Protocol;

namespace RevitTestHost.Core.Server
{
    /// <summary>Будит поток Revit: в надстройке — <c>ExternalEvent.Raise()</c>.</summary>
    public interface IExternalEventRaiser
    {
        /// <summary>
        /// true — событие принято (Accepted или Pending: обработчик ещё выполнится);
        /// false — отказ (Denied, TimedOut) с причиной в <paramref name="failureReason"/>.
        /// </summary>
        bool TryRaise(out string failureReason);
    }

    /// <summary>
    /// Очередь запросов между потоком канала и потоком Revit. Поток канала вызывает <see cref="Enqueue"/>
    /// и получает задачу с ответом; обработчик ExternalEvent в потоке Revit вызывает <see cref="Drain"/>,
    /// который выполняет запросы по одному в порядке поступления. Продолжения задач выполняются асинхронно,
    /// поэтому запись ответа в канал никогда не блокирует поток Revit.
    /// </summary>
    public sealed class RequestQueue
    {
        private sealed class Pending
        {
            private readonly TaskCompletionSource<HostResponse> _tcs =
                new TaskCompletionSource<HostResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Pending(HostRequest request) => Request = request;

            public HostRequest Request { get; }
            public Task<HostResponse> Task => _tcs.Task;
            public void Complete(HostResponse response) => _tcs.TrySetResult(response);
        }

        private const string RaiseDeniedFormat = "Revit не принял внешнее событие (ExternalEvent.Raise: {0}); запрос не выполнен.";

        /// <summary>Текст ответа <c>REVIT_ERROR</c> на запрос, не выполненный из-за завершения Revit.</summary>
        public const string ShutdownMessage = "Revit завершается, запрос не выполнен.";

        private readonly object _lock = new object();
        private readonly LinkedList<Pending> _items = new LinkedList<Pending>();
        private readonly IExternalEventRaiser _raiser;
        private bool _idlingSinceLastRequest;
        private bool _idleObservationPending;

        /// <summary>Не null после <see cref="Shutdown"/>: журнал для строк о невыполненных запросах.</summary>
        private IServerLog? _shutdownLog;

        public RequestQueue(IExternalEventRaiser raiser)
        {
            _raiser = raiser ?? throw new ArgumentNullException(nameof(raiser));
        }

        /// <summary>Число запросов, ещё не взятых на выполнение.</summary>
        public int PendingCount
        {
            get { lock (_lock) return _items.Count; }
        }

        /// <summary>
        /// true с момента завершения очередного запроса (в <see cref="Drain"/>) до следующего <see cref="OnIdling"/>:
        /// наблюдение простоя Revit ещё нужно. Пока true, надстройка вызывает <c>IdlingEventArgs.SetRaiseWithoutDelay()</c>,
        /// чтобы Idling пришёл как можно скорее. Читать нужно ДО вызова <see cref="OnIdling"/>, иначе значение уже сброшено.
        /// До первого завершённого запроса — false.
        /// </summary>
        public bool IdleObservationPending
        {
            get { lock (_lock) return _idleObservationPending; }
        }

        /// <summary>
        /// Ставит запрос в очередь и будит поток Revit. Если Revit отказал в событии — запрос снимается
        /// с очереди и сразу получает <c>REVIT_ERROR</c>. Задача никогда не завершается исключением.
        /// </summary>
        public Task<HostResponse> Enqueue(HostRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var pending = new Pending(request);
            IServerLog? shutdownLog;
            lock (_lock)
            {
                shutdownLog = _shutdownLog;
                if (shutdownLog == null) _items.AddLast(pending);
            }

            if (shutdownLog != null)
            {
                FailOnShutdown(pending, shutdownLog);
                return pending.Task;
            }

            if (!TryRaise(out var reason))
            {
                bool removed;
                lock (_lock) removed = _items.Remove(pending);
                // removed == false: обработчик уже взял запрос по прошлому событию и ответит сам
                if (removed)
                    pending.Complete(HostResponse.Failure(request.Id, ErrorCodes.RevitError,
                        string.Format(RaiseDeniedFormat, reason)));
            }

            return pending.Task;
        }

        /// <summary>
        /// Вызывается из обработчика Idling. Отмечает простой Revit для <c>ping.idle</c> и, если в очереди остались
        /// запросы (событие могло потеряться), будит поток Revit ещё раз; при отказе запросы получают <c>REVIT_ERROR</c>.
        /// </summary>
        public void OnIdling()
        {
            bool hasPending;
            lock (_lock)
            {
                _idlingSinceLastRequest = true;
                _idleObservationPending = false;
                hasPending = _items.Count > 0;
            }

            if (hasPending && !TryRaise(out var reason))
            {
                List<Pending> failed;
                lock (_lock)
                {
                    failed = new List<Pending>(_items);
                    _items.Clear();
                }
                foreach (var p in failed)
                    p.Complete(HostResponse.Failure(p.Request.Id, ErrorCodes.RevitError,
                        string.Format(RaiseDeniedFormat, reason)));
            }
        }

        /// <summary>
        /// Выполняет все запросы очереди по одному (поток Revit). <c>idle</c> для запроса = Idling срабатывал
        /// после предыдущего запроса и за этим запросом в очереди никого нет. Исключение обработчика
        /// превращается в <c>REVIT_ERROR</c> для этого запроса, остальные выполняются дальше.
        /// </summary>
        public void Drain(IRequestHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            while (true)
            {
                Pending item;
                bool idle;
                lock (_lock)
                {
                    if (_items.Count == 0) return;
                    item = _items.First!.Value;
                    _items.RemoveFirst();
                    idle = _idlingSinceLastRequest && _items.Count == 0;
                }

                HostResponse response;
                try
                {
                    response = handler.Handle(item.Request, idle);
                }
                catch (Exception ex)
                {
                    response = HostResponse.Failure(item.Request.Id, ErrorCodes.RevitError, ExceptionText.Describe(ex));
                }

                lock (_lock)
                {
                    _idlingSinceLastRequest = false;
                    _idleObservationPending = true;
                }
                item.Complete(response);
            }
        }

        /// <summary>
        /// Revit завершается (поток Revit, до остановки сервера канала): запросы из очереди и все последующие
        /// получают <c>REVIT_ERROR</c> <see cref="ShutdownMessage"/>, о каждом — строка в <paramref name="log"/>.
        /// Ответы уходят клиенту, пока подключение ещё открыто.
        /// </summary>
        public void Shutdown(IServerLog log)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));
            List<Pending> dropped;
            lock (_lock)
            {
                _shutdownLog = log;
                dropped = new List<Pending>(_items);
                _items.Clear();
            }
            foreach (var p in dropped)
                FailOnShutdown(p, log);
        }

        private static void FailOnShutdown(Pending pending, IServerLog log)
        {
            log.Info($"Запрос {pending.Request.Id} ({pending.Request.Command}) не выполнен: Revit завершается.");
            pending.Complete(HostResponse.Failure(pending.Request.Id, ErrorCodes.RevitError, ShutdownMessage));
        }

        /// <summary>Вызов <see cref="IExternalEventRaiser.TryRaise"/>; исключение (например, у освобождённого ExternalEvent) считается отказом.</summary>
        private bool TryRaise(out string failureReason)
        {
            try
            {
                return _raiser.TryRaise(out failureReason);
            }
            catch (Exception ex)
            {
                failureReason = ExceptionText.Describe(ex);
                return false;
            }
        }
    }
}
