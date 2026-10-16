using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using RevitTestHost.Core.Json;
using RevitTestHost.Core.Protocol;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Tests.Server
{
    [TestFixture]
    public class RequestQueueTests
    {
        internal sealed class FakeRaiser : IExternalEventRaiser
        {
            public int Raised;
            public string? FailWith;
            public Exception? ThrowOnRaise;

            public bool TryRaise(out string failureReason)
            {
                Raised++;
                if (ThrowOnRaise != null) throw ThrowOnRaise;
                failureReason = FailWith ?? "";
                return FailWith == null;
            }
        }

        /// <summary>Отвечает командой и признаком idle, записывает порядок вызовов.</summary>
        private sealed class EchoHandler : IRequestHandler
        {
            public readonly List<long> Order = new List<long>();

            public HostResponse Handle(HostRequest request, bool idle)
            {
                Order.Add(request.Id);
                if (request.Command == "boom") throw new InvalidOperationException("сбой обработчика");
                return HostResponse.Success(request.Id,
                    new JsonPayload(w => w.BeginObject().Property("cmd", request.Command).Property("idle", idle).EndObject()));
            }
        }

        private static HostRequest Req(long id, string cmd = "ping") =>
            new HostRequest(id, cmd, new Dictionary<string, object?>());

        private static bool IdleOf(HostResponse r) =>
            (bool)((Dictionary<string, object?>)((Dictionary<string, object?>)JsonReader.Parse(ProtocolCodec.FormatResponse(r))!)["result"]!)["idle"]!;

        [Test]
        public void Enqueue_RaisesEventAndWaitsForDrain()
        {
            var raiser = new FakeRaiser();
            var queue = new RequestQueue(raiser);

            var task = queue.Enqueue(Req(1));

            Assert.That(raiser.Raised, Is.EqualTo(1));
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(queue.PendingCount, Is.EqualTo(1));

            queue.Drain(new EchoHandler());

            Assert.That(task.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(task.Result.Id, Is.EqualTo(1L));
            Assert.That(queue.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Drain_ExecutesInFifoOrderAndCorrelatesIds()
        {
            var queue = new RequestQueue(new FakeRaiser());
            var tasks = new[] { queue.Enqueue(Req(5)), queue.Enqueue(Req(3)), queue.Enqueue(Req(9)) };
            var handler = new EchoHandler();

            queue.Drain(handler);

            Assert.That(handler.Order, Is.EqualTo(new[] { 5L, 3L, 9L }));
            Assert.That(tasks.Select(t => t.Result.Id), Is.EqualTo(new[] { 5L, 3L, 9L }));
        }

        [Test]
        public void RaiseDenied_FailsRequestWithRevitErrorAndDoesNotQueueIt()
        {
            var queue = new RequestQueue(new FakeRaiser { FailWith = "Denied" });

            var task = queue.Enqueue(Req(4));

            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result.Ok, Is.False);
            Assert.That(task.Result.Id, Is.EqualTo(4L));
            Assert.That(task.Result.ErrorCode, Is.EqualTo(ErrorCodes.RevitError));
            Assert.That(task.Result.ErrorMessage, Does.Contain("Denied"));
            Assert.That(queue.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void HandlerException_BecomesRevitError_AndNextRequestsStillRun()
        {
            var queue = new RequestQueue(new FakeRaiser());
            var bad = queue.Enqueue(Req(1, "boom"));
            var good = queue.Enqueue(Req(2));

            queue.Drain(new EchoHandler());

            Assert.That(bad.Result.ErrorCode, Is.EqualTo(ErrorCodes.RevitError));
            Assert.That(bad.Result.ErrorMessage, Does.Contain("сбой обработчика"));
            Assert.That(good.Result.Ok, Is.True);
        }

        [Test]
        public void Idle_TrueOnlyAfterIdlingAndWithEmptyQueue()
        {
            var queue = new RequestQueue(new FakeRaiser());
            var handler = new EchoHandler();

            var beforeIdling = queue.Enqueue(Req(1));
            queue.Drain(handler);
            Assert.That(IdleOf(beforeIdling.Result), Is.False, "Idling ещё не срабатывал");

            queue.OnIdling();
            var afterIdling = queue.Enqueue(Req(2));
            queue.Drain(handler);
            Assert.That(IdleOf(afterIdling.Result), Is.True);

            var noIdlingSinceLast = queue.Enqueue(Req(3));
            queue.Drain(handler);
            Assert.That(IdleOf(noIdlingSinceLast.Result), Is.False, "после предыдущего запроса Idling не было");

            queue.OnIdling();
            var first = queue.Enqueue(Req(4));
            var second = queue.Enqueue(Req(5));
            queue.Drain(handler);
            Assert.That(IdleOf(first.Result), Is.False, "за запросом в очереди есть другой");
            Assert.That(IdleOf(second.Result), Is.False, "после первого запроса Idling не было");
        }

        [Test]
        public void OnIdling_WithPendingRequests_RaisesAgain()
        {
            var raiser = new FakeRaiser();
            var queue = new RequestQueue(raiser);
            queue.Enqueue(Req(1));

            queue.OnIdling();

            Assert.That(raiser.Raised, Is.EqualTo(2));
        }

        [Test]
        public void OnIdling_WithEmptyQueue_DoesNotRaise()
        {
            var raiser = new FakeRaiser();
            new RequestQueue(raiser).OnIdling();
            Assert.That(raiser.Raised, Is.EqualTo(0));
        }

        [Test]
        public void OnIdling_RaiseFails_FailsAllPending()
        {
            var raiser = new FakeRaiser();
            var queue = new RequestQueue(raiser);
            var a = queue.Enqueue(Req(1));
            var b = queue.Enqueue(Req(2));
            raiser.FailWith = "TimedOut";

            queue.OnIdling();

            Assert.That(new[] { a.Result.ErrorCode, b.Result.ErrorCode }, Is.All.EqualTo(ErrorCodes.RevitError));
            Assert.That(queue.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Continuations_DoNotRunOnDrainingThread()
        {
            var queue = new RequestQueue(new FakeRaiser());
            var task = queue.Enqueue(Req(1));
            var drainThread = -1;
            var continuationThread = -1;
            var continuation = task.ContinueWith(_ => continuationThread = Thread.CurrentThread.ManagedThreadId,
                System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);

            var thread = new Thread(() =>
            {
                drainThread = Thread.CurrentThread.ManagedThreadId;
                queue.Drain(new EchoHandler());
                Thread.Sleep(200); // если бы продолжение шло синхронно, оно уже выполнилось бы здесь
            });
            thread.Start();
            thread.Join();
            Assert.That(continuation.Wait(TimeSpan.FromSeconds(5)), Is.True, "продолжение должно выполниться");

            Assert.That(continuationThread, Is.Not.EqualTo(drainThread));
        }

        [Test]
        public void IdleObservationPending_TrueFromCompletedRequestUntilNextIdling()
        {
            var queue = new RequestQueue(new FakeRaiser());
            Assert.That(queue.IdleObservationPending, Is.False, "ни один запрос ещё не завершён");

            queue.Enqueue(Req(1));
            queue.Drain(new EchoHandler());
            Assert.That(queue.IdleObservationPending, Is.True);

            queue.OnIdling();
            Assert.That(queue.IdleObservationPending, Is.False);

            queue.Enqueue(Req(2));
            queue.Drain(new EchoHandler());
            Assert.That(queue.IdleObservationPending, Is.True);
        }

        [Test]
        public void RaiserThrows_OnEnqueue_FailsRequestWithRevitErrorAndDoesNotQueueIt()
        {
            var queue = new RequestQueue(new FakeRaiser { ThrowOnRaise = new ObjectDisposedException("ExternalEvent") });

            var task = queue.Enqueue(Req(7));

            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.IsFaulted, Is.False);
            Assert.That(task.Result.ErrorCode, Is.EqualTo(ErrorCodes.RevitError));
            Assert.That(task.Result.Id, Is.EqualTo(7L));
            Assert.That(task.Result.ErrorMessage, Does.Contain(typeof(ObjectDisposedException).FullName));
            Assert.That(queue.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void RaiserThrows_OnIdling_FailsAllPending()
        {
            var raiser = new FakeRaiser();
            var queue = new RequestQueue(raiser);
            var a = queue.Enqueue(Req(1));
            var b = queue.Enqueue(Req(2));
            raiser.ThrowOnRaise = new ObjectDisposedException("ExternalEvent");

            Assert.DoesNotThrow(() => queue.OnIdling());

            Assert.That(new[] { a.Result.ErrorCode, b.Result.ErrorCode }, Is.All.EqualTo(ErrorCodes.RevitError));
            Assert.That(queue.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Shutdown_FailsQueuedRequestsWithRevitErrorAndLogsEachId()
        {
            var queue = new RequestQueue(new FakeRaiser());
            var a = queue.Enqueue(Req(1));
            var b = queue.Enqueue(Req(2, "run"));
            var log = new ListLog();

            queue.Shutdown(log);

            Assert.That(queue.PendingCount, Is.EqualTo(0));
            foreach (var (task, id) in new[] { (a, 1L), (b, 2L) })
            {
                Assert.That(task.IsCompleted, Is.True);
                Assert.That(task.Result.Id, Is.EqualTo(id));
                Assert.That(task.Result.ErrorCode, Is.EqualTo(ErrorCodes.RevitError));
                Assert.That(task.Result.ErrorMessage, Is.EqualTo(RequestQueue.ShutdownMessage));
            }
            Assert.That(RequestQueue.ShutdownMessage, Is.EqualTo("Revit завершается, запрос не выполнен."));
            Assert.That(log.Lines, Is.EqualTo(new[]
            {
                "INFO Запрос 1 (ping) не выполнен: Revit завершается.",
                "INFO Запрос 2 (run) не выполнен: Revit завершается.",
            }));
        }

        [Test]
        public void Shutdown_EmptyQueue_LogsNothing()
        {
            var queue = new RequestQueue(new FakeRaiser());
            var log = new ListLog();

            queue.Shutdown(log);

            Assert.That(log.Lines, Is.Empty);
        }

        [Test]
        public void EnqueueAfterShutdown_FailsImmediatelyWithoutRaiseAndLogs()
        {
            var raiser = new FakeRaiser();
            var queue = new RequestQueue(raiser);
            var log = new ListLog();
            queue.Shutdown(log);

            var task = queue.Enqueue(Req(5));

            Assert.That(raiser.Raised, Is.EqualTo(0));
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result.ErrorCode, Is.EqualTo(ErrorCodes.RevitError));
            Assert.That(task.Result.ErrorMessage, Is.EqualTo(RequestQueue.ShutdownMessage));
            Assert.That(queue.PendingCount, Is.EqualTo(0));
            Assert.That(log.Lines, Is.EqualTo(new[] { "INFO Запрос 5 (ping) не выполнен: Revit завершается." }));
        }
    }
}
