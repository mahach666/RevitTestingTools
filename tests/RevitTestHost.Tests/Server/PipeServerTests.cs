using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using NUnit.Framework;
using RevitTestHost.Core.Protocol;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Tests.Server
{
    [TestFixture]
    public class PipeServerTests
    {
        private string _pipeName = null!;
        private ListLog _log = null!;
        private GateHandler _handler = null!;
        private RequestQueue _queue = null!;
        private FakeRevitThread _revit = null!;
        private PipeServer _server = null!;

        [SetUp]
        public void SetUp()
        {
            _pipeName = "RevitTestHost-" + Guid.NewGuid().ToString("N");
            _log = new ListLog();
            _handler = new GateHandler();
            _revit = new FakeRevitThread(() => _queue, _handler);
            _queue = new RequestQueue(_revit);
            _server = new PipeServer(_pipeName, _queue, _log);
            _server.Start();
        }

        [TearDown]
        public void TearDown()
        {
            _handler.Release();
            _server.Dispose();
            _revit.Dispose();
        }

        private static string Code(Dictionary<string, object?> response) =>
            (string)((Dictionary<string, object?>)response["error"]!)["code"]!;

        [Test]
        public void Request_GetsResponseWithSameId()
        {
            using (var client = TestPipeClient.Connect(_pipeName))
            {
                client.Send("{\"id\":42,\"cmd\":\"ping\"}");
                var response = client.Read();

                Assert.That(response["id"], Is.EqualTo(42L));
                Assert.That(response["ok"], Is.EqualTo(true));
            }
        }

        [Test]
        public void MalformedLine_GetsBadRequest_AndConnectionStaysUsable()
        {
            using (var client = TestPipeClient.Connect(_pipeName))
            {
                client.Send("{это не json");
                var bad = client.Read();
                Assert.That(bad["id"], Is.EqualTo(0L));
                Assert.That(Code(bad), Is.EqualTo(ErrorCodes.BadRequest));

                client.Send("{\"id\":2,\"cmd\":\"ping\"}");
                Assert.That(client.Read()["ok"], Is.EqualTo(true));
            }
        }

        [Test]
        public void EmptyLines_AreIgnored()
        {
            using (var client = TestPipeClient.Connect(_pipeName))
            {
                client.Send("");
                client.Send("   ");
                client.Send("{\"id\":3,\"cmd\":\"ping\"}");
                Assert.That(client.Read()["id"], Is.EqualTo(3L));
            }
        }

        [Test]
        public void PipelinedRequests_AnsweredInOrderWithMatchingIds()
        {
            using (var client = TestPipeClient.Connect(_pipeName))
            {
                client.SendRaw(Encoding.UTF8.GetBytes(
                    "{\"id\":1,\"cmd\":\"a\"}\n{\"id\":2,\"cmd\":\"b\"}\nне json\n{\"id\":4,\"cmd\":\"c\"}\n"));

                var ids = Enumerable.Range(0, 4).Select(_ => client.Read()["id"]).ToList();

                // ответы идут в порядке запросов; BAD_REQUEST (id 0) не обгоняет выполняемые запросы
                Assert.That(ids, Is.EqualTo(new object[] { 1L, 2L, 0L, 4L }));
            }
        }

        [Test]
        public void CyrillicAndCrlf_RoundTrip()
        {
            using (var client = TestPipeClient.Connect(_pipeName))
            {
                client.SendRaw(new UTF8Encoding(false).GetBytes("{\"id\":5,\"cmd\":\"команда\"}\r\n"));
                var response = client.Read();
                Assert.That(((Dictionary<string, object?>)response["result"]!)["cmd"], Is.EqualTo("команда"));
            }
        }

        [Test]
        public void LeadingBom_IsTolerated()
        {
            using (var client = TestPipeClient.Connect(_pipeName))
            {
                client.SendRaw(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("{\"id\":6,\"cmd\":\"ping\"}\n")).ToArray());
                Assert.That(client.Read()["ok"], Is.EqualTo(true));
            }
        }

        [Test]
        public void BusyRevit_NoResponseUntilRequestExecuted()
        {
            using (var client = TestPipeClient.Connect(_pipeName))
            {
                client.Send("{\"id\":7,\"cmd\":\"block\"}");
                Assert.That(_handler.Entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

                var pending = client.ReadLineAsync();
                Assert.That(pending.Wait(300), Is.False, "сервер не должен отвечать, пока поток Revit занят");

                _handler.Release();
                Assert.That(pending.Wait(5000), Is.True);
                Assert.That(TestPipeClient.Parse(pending.Result)["id"], Is.EqualTo(7L));
            }
        }

        [Test]
        public void Disconnect_ThenReconnect_Works()
        {
            using (var first = TestPipeClient.Connect(_pipeName))
            {
                first.Send("{\"id\":1,\"cmd\":\"ping\"}");
                first.Read();
            }

            using (var second = TestPipeClient.Connect(_pipeName))
            {
                second.Send("{\"id\":2,\"cmd\":\"ping\"}");
                Assert.That(second.Read()["id"], Is.EqualTo(2L));
            }
        }

        [Test]
        public void ReconnectWhileRevitBusy_NewClientGetsItsOwnAnswerAfterRelease()
        {
            using (var first = TestPipeClient.Connect(_pipeName))
            {
                first.Send("{\"id\":1,\"cmd\":\"block\"}");
                Assert.That(_handler.Entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            } // клиент ушёл по таймауту, не дождавшись ответа

            using (var second = TestPipeClient.Connect(_pipeName))
            {
                second.Send("{\"id\":2,\"cmd\":\"ping\"}");
                _handler.Release();

                var response = second.Read();
                Assert.That(response["id"], Is.EqualTo(2L), "ответ на запрос первого подключения в новое не попадает");
            }

            Assert.That(SpinWait.SpinUntil(() => _log.Contains("Ответ на запрос 1 не отправлен"), TimeSpan.FromSeconds(5)), Is.True);
        }

        [Test]
        public void WriterThreadFailure_ClosesConnection_ClientSeesEndOfStream_AndServerAcceptsNextClient()
        {
            using (var client = TestPipeClient.Connect(_pipeName))
            {
                // Обработчик вернул null: Send бросает вне перехватываемых ошибок, поток записи останавливается
                client.Send("{\"id\":1,\"cmd\":\"null\"}");
                var pending = client.ReadLineAsync();

                // клиент видит конец потока (null) или разрыв канала, а не тишину до таймаута
                var completed = false;
                try
                {
                    completed = pending.Wait(5000);
                }
                catch (AggregateException ex) when (ex.InnerException is System.IO.IOException)
                {
                    completed = true;
                }
                Assert.That(completed, Is.True, "после падения потока записи подключение должно закрыться");
                if (pending.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                    Assert.That(pending.Result, Is.Null);
                Assert.That(_log.Contains("Поток отправки ответов остановлен"), Is.True);
            }

            using (var next = TestPipeClient.Connect(_pipeName))
            {
                next.Send("{\"id\":2,\"cmd\":\"ping\"}");
                Assert.That(next.Read()["id"], Is.EqualTo(2L));
            }
        }

        [Test]
        public void SecondConnectionAtOnce_IsNotAccepted()
        {
            using (var first = TestPipeClient.Connect(_pipeName))
            {
                // .NET Framework: занятый канал — IOException «Превышен таймаут семафора», отсутствующий — TimeoutException
                Assert.That(() => TestPipeClient.Connect(_pipeName, timeoutMs: 300).Dispose(),
                    Throws.InstanceOf<TimeoutException>().Or.InstanceOf<System.IO.IOException>());
                first.Send("{\"id\":1,\"cmd\":\"ping\"}");
                Assert.That(first.Read()["id"], Is.EqualTo(1L));
            }
        }

        [Test]
        public void Dispose_StopsListening()
        {
            _server.Dispose();

            Assert.Throws<TimeoutException>(() => TestPipeClient.Connect(_pipeName, timeoutMs: 300).Dispose());
            Assert.That(_log.Contains("Сервер канала остановлен."), Is.True);
        }

        [Test]
        public void SameNameTwice_SecondServerLogsErrorAndStops()
        {
            var log = new ListLog();
            using (TestPipeClient.Connect(_pipeName)) // первый сервер занял единственный экземпляр
            using (var second = new PipeServer(_pipeName, _queue, log))
            {
                second.Start();
                Assert.That(SpinWait.SpinUntil(() => log.Contains("Не удалось создать канал"), TimeSpan.FromSeconds(5)), Is.True);
            }
        }

        [Test]
        public void Acl_GrantsOnlyCurrentUser_AndDeniesNetwork()
        {
            var me = WindowsIdentity.GetCurrent().User!;
            var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
            using (var stream = PipeServer.CreateStream("RevitTestHost-" + Guid.NewGuid().ToString("N")))
            {
                var rules = stream.GetAccessControl()
                    .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                    .Cast<AccessRule>()
                    .ToList();

                Assert.That(rules, Is.Not.Empty);
                var allow = rules.Where(r => r.AccessControlType == AccessControlType.Allow).ToList();
                var deny = rules.Where(r => r.AccessControlType == AccessControlType.Deny).ToList();
                Assert.That(allow, Is.Not.Empty);
                Assert.That(allow.Select(r => r.IdentityReference), Is.All.EqualTo(me));
                Assert.That(deny.Select(r => r.IdentityReference), Is.EqualTo(new[] { network }), "ожидается ровно один запрет — для NETWORK (S-1-5-2)");
            }
        }

        [Test]
        public void LocalClient_ConnectsDespiteNetworkDeny()
        {
            using (var client = TestPipeClient.Connect(_pipeName))
            {
                client.Send("{\"id\":1,\"cmd\":\"ping\"}");
                Assert.That(client.Read()["id"], Is.EqualTo(1L));
            }
        }

        [Test]
        public void Dispose_WhileRequestBlocked_ReturnsPromptlyAndClosesClient()
        {
            using (var client = TestPipeClient.Connect(_pipeName))
            {
                client.Send("{\"id\":9,\"cmd\":\"block\"}");
                Assert.That(_handler.Entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                var pending = client.ReadLineAsync();

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                _server.Dispose();
                stopwatch.Stop();

                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)), "Dispose не должен ждать поток Revit");
                Assert.That(_log.Contains("не завершился"), Is.False);
                Assert.That(_log.Contains("ждём следующее подключение"), Is.False, "при остановке сервер не ждёт нового подключения");

                // клиент видит конец потока (null) или разрыв канала, но не зависает
                var completed = false;
                try
                {
                    completed = pending.Wait(5000);
                }
                catch (AggregateException ex) when (ex.InnerException is System.IO.IOException)
                {
                    completed = true;
                }
                Assert.That(completed, Is.True, "ожидающее чтение клиента должно завершиться после остановки сервера");
                if (pending.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                    Assert.That(pending.Result, Is.Null);

                // поток Revit заканчивает запрос уже после остановки: ответ в закрытое подключение не пишется и ничего не бросает
                _handler.Release();
                Assert.That(SpinWait.SpinUntil(() => _log.Contains("Ответ на запрос 9 не отправлен"), TimeSpan.FromSeconds(5)), Is.True);
            }
        }

        [Test]
        public void Shutdown_QueuedRequestsAreAnsweredBeforeConnectionCloses()
        {
            // Поток Revit не выбирает очередь (как при завершении Revit): запросы стоят в очереди
            var pipeName = "RevitTestHost-" + Guid.NewGuid().ToString("N");
            var log = new ListLog();
            var queue = new RequestQueue(new RequestQueueTests.FakeRaiser());
            var server = new PipeServer(pipeName, queue, log);
            server.Start();
            try
            {
                using (var client = TestPipeClient.Connect(pipeName))
                {
                    client.Send("{\"id\":1,\"cmd\":\"ping\"}");
                    client.Send("{\"id\":2,\"cmd\":\"documents\"}");
                    Assert.That(SpinWait.SpinUntil(() => queue.PendingCount == 2, TimeSpan.FromSeconds(5)), Is.True);

                    // Клиент читает, пока сервер останавливается (как живой клиент)
                    var reading = System.Threading.Tasks.Task.Run(() => new[] { client.Read(), client.Read() });
                    queue.Shutdown(log);
                    server.Dispose();

                    Assert.That(reading.Wait(TimeSpan.FromSeconds(10)), Is.True, "ответы не пришли");
                    var responses = reading.Result;
                    foreach (var (response, id) in new[] { (responses[0], 1L), (responses[1], 2L) })
                    {
                        Assert.That(response["id"], Is.EqualTo(id));
                        Assert.That(Code(response), Is.EqualTo(ErrorCodes.RevitError));
                        Assert.That(((Dictionary<string, object?>)response["error"]!)["message"], Is.EqualTo(RequestQueue.ShutdownMessage));
                    }
                    Assert.That(log.Contains("Запрос 1 (ping) не выполнен: Revit завершается."), Is.True);
                    Assert.That(log.Contains("Запрос 2 (documents) не выполнен: Revit завершается."), Is.True);
                    Assert.That(log.Contains("не отправлен"), Is.False);
                }
            }
            finally
            {
                server.Dispose();
            }
        }

        [Test]
        public void Dispose_ClientNotReading_DoesNotHang()
        {
            var pipeName = "RevitTestHost-" + Guid.NewGuid().ToString("N");
            var log = new ListLog();
            var queue = new RequestQueue(new RequestQueueTests.FakeRaiser());
            var server = new PipeServer(pipeName, queue, log);
            server.Start();
            try
            {
                using (var client = TestPipeClient.Connect(pipeName))
                {
                    // Много запросов: ответы не помещаются в буфер канала, а клиент их не читает
                    var requests = new StringBuilder();
                    for (var i = 1; i <= 2000; i++) requests.Append("{\"id\":" + i + ",\"cmd\":\"ping\"}\n");
                    client.SendRaw(Encoding.UTF8.GetBytes(requests.ToString()));
                    Assert.That(SpinWait.SpinUntil(() => queue.PendingCount == 2000, TimeSpan.FromSeconds(10)), Is.True);

                    queue.Shutdown(log);
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    server.Dispose();
                    stopwatch.Stop();

                    Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(4)), "остановка не должна зависать на нечитающем клиенте");
                }
            }
            finally
            {
                server.Dispose();
            }
        }

        [Test]
        public void InvalidPipeName_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PipeServer(@"..\x", _queue, _log));
        }
    }
}
