using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Unit
{
    [TestFixture]
    [NonParallelizable]
    public class RevitHostClientTests
    {
        private static RevitHostClient Client(FakeHostServer server, double timeoutSeconds = 5, Func<string> windows = null, Action ensureAlive = null)
        {
            return new RevitHostClient(server.PipeName, TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromSeconds(timeoutSeconds), windows ?? (() => "окон нет"), ensureAlive);
        }

        private static string Cmd(JsonElement request) => request.GetProperty("cmd").GetString();

        [Test]
        public void Ping_returns_server_fields()
        {
            using (FakeHostServer server = new FakeHostServer(r => FakeHostServer.Ping(r.GetProperty("id").GetInt64(), 4242)))
            using (RevitHostClient client = Client(server))
            {
                HostPing ping = client.Ping();

                Assert.That(ping.Pid, Is.EqualTo(4242));
                Assert.That(ping.RevitVersion, Is.EqualTo("2022"));
                Assert.That(server.Requests().Single(), Is.EqualTo("{\"id\":1,\"cmd\":\"ping\",\"args\":{}}"));
            }
        }

        [Test]
        public void Error_response_becomes_HostErrorException_with_code()
        {
            using (FakeHostServer server = new FakeHostServer(r => FakeHostServer.Error(r, HostErrorCodes.ActiveDocument, "активный документ закрывает UI")))
            using (RevitHostClient client = Client(server))
            {
                HostErrorException ex = Assert.Throws<HostErrorException>(() => client.Close("new-1"));

                Assert.That(ex.Code, Is.EqualTo(HostErrorCodes.ActiveDocument));
                Assert.That(ex.Command, Is.EqualTo("close"));
                Assert.That(server.Requests().Single(), Does.Contain("\"title\":\"new-1\""));
                Assert.That(server.Requests().Single(), Does.Contain("\"save\":false"));
            }
        }

        [Test]
        public void No_reply_within_timeout_is_RevitBusyException_with_open_windows()
        {
            using (FakeHostServer server = new FakeHostServer(r => null))
            using (RevitHostClient client = Client(server, 0.5, () => "открыты окна: «Save changes» (#32770)"))
            {
                Stopwatch sw = Stopwatch.StartNew();
                RevitBusyException ex = Assert.Throws<RevitBusyException>(() => client.Ping());

                Assert.That(sw.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(0.45)));
                Assert.That(ex.Command, Is.EqualTo("ping"));
                Assert.That(ex.OpenWindows, Does.Contain("Save changes"));
            }
        }

        [Test]
        public void Late_reply_is_skipped_by_next_request()
        {
            using (FakeHostServer server = new FakeHostServer(r =>
            {
                long id = r.GetProperty("id").GetInt64();
                return id == 1 ? null : FakeHostServer.Ping(id, (int)id);
            }))
            using (RevitHostClient client = Client(server, 0.5))
            {
                Assert.Throws<RevitBusyException>(() => client.Ping());
                server.Send(FakeHostServer.Ping(1, 111));

                HostPing second = client.Ping();

                Assert.That(second.Pid, Is.EqualTo(2), "второй запрос получил свой ответ, а не запоздалый ответ на первый");
                Assert.That(server.Requests(), Has.Count.EqualTo(2));
            }
        }

        [Test]
        public void Reply_to_unsent_request_is_InvalidDataException()
        {
            using (FakeHostServer server = new FakeHostServer(r => FakeHostServer.Ping(99, 1)))
            using (RevitHostClient client = Client(server))
            {
                Assert.That(() => client.Ping(), Throws.TypeOf<InvalidDataException>().With.Message.Contains("#99"));
            }
        }

        [Test]
        public void Bad_request_with_id_zero_is_error_of_current_request()
        {
            using (FakeHostServer server = new FakeHostServer(r => "{\"id\":0,\"ok\":false,\"error\":{\"code\":\"BAD_REQUEST\",\"message\":\"id не прочитан\"}}"))
            using (RevitHostClient client = Client(server))
            {
                HostErrorException ex = Assert.Throws<HostErrorException>(() => client.Ping());
                Assert.That(ex.Code, Is.EqualTo(HostErrorCodes.BadRequest));
            }
        }

        [Test]
        public void Server_disconnect_is_IOException_and_next_call_reconnects()
        {
            FakeHostServer server = null;
            server = new FakeHostServer(r =>
            {
                long id = r.GetProperty("id").GetInt64();
                if (id == 1)
                {
                    server.DropClient();
                    return null;
                }
                return FakeHostServer.Ping(id, 5);
            });
            using (server)
            using (RevitHostClient client = Client(server))
            {
                Assert.That(() => client.Ping(), Throws.TypeOf<IOException>().With.Message.Contains("закрыт"));

                Assert.That(client.Ping().Pid, Is.EqualTo(5));
                Assert.That(server.Connections, Is.EqualTo(2));
            }
        }

        [Test]
        public void Disconnect_consults_ensureAlive_first()
        {
            FakeHostServer server = null;
            server = new FakeHostServer(r =>
            {
                server.DropClient();
                return null;
            });
            using (server)
            using (RevitHostClient client = Client(server, ensureAlive: () => throw new RevitExitedException(42, 1, "хвост")))
            {
                Assert.Throws<RevitExitedException>(() => client.Ping());
            }
        }

        [Test]
        public void Missing_server_is_TimeoutException_not_busy()
        {
            TimeSpan saved = RevitHostClient.ConnectTimeout;
            RevitHostClient.ConnectTimeout = TimeSpan.FromMilliseconds(200);
            try
            {
                using (RevitHostClient client = new RevitHostClient("RevitUITesting.Tests-none-" + Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), null, null))
                {
                    TimeoutException ex = Assert.Throws<TimeoutException>(() => client.Ping());
                    Assert.That(ex, Is.Not.InstanceOf<RevitBusyException>());
                    Assert.That(ex.Message, Does.Contain("не принял подключение"));
                }
            }
            finally
            {
                RevitHostClient.ConnectTimeout = saved;
            }
        }

        [Test]
        public void WaitIdle_polls_until_idle()
        {
            using (FakeHostServer server = new FakeHostServer(r =>
            {
                long id = r.GetProperty("id").GetInt64();
                return FakeHostServer.Ping(id, 7, idle: id >= 3);
            }))
            using (RevitHostClient client = Client(server))
            {
                Assert.That(client.WaitIdle().Idle, Is.True);
                Assert.That(server.Requests(), Has.Count.EqualTo(3));
            }
        }

        [Test]
        public void WaitIdle_without_idle_fails_with_last_ping()
        {
            using (FakeHostServer server = new FakeHostServer(r => FakeHostServer.Ping(r.GetProperty("id").GetInt64(), 7, idle: false)))
            using (RevitHostClient client = Client(server, 0.6))
            {
                UiWaitTimeoutException ex = Assert.Throws<UiWaitTimeoutException>(() => client.WaitIdle());

                Assert.That(ex, Is.Not.InstanceOf<RevitBusyException>(), "сервер отвечал — это не «Revit занят», а «idle не наступил»");
                Assert.That(ex.Message, Does.Contain("idle = true"));
                Assert.That(ex.LastObservation, Does.Contain("idle=False"));
            }
        }

        [Test]
        public void Run_sends_filter_model_and_parameters()
        {
            string runResult = "{\"summary\":{\"total\":1,\"passed\":1,\"failed\":0,\"errored\":0,\"skipped\":0,\"durationMs\":5,\"revitVersion\":\"2022\"},\"results\":[{\"class\":\"Ns.P\",\"method\":\"M\",\"status\":\"passed\",\"durationMs\":5}]}";
            using (FakeHostServer server = new FakeHostServer(r => Cmd(r) == "run" ? FakeHostServer.Ok(r, runResult) : null))
            using (RevitHostClient client = Client(server))
            {
                HostRunReport report = client.Run("Ns.P.*", new Dictionary<string, string> { ["family"] = "Ванна" });

                Assert.That(report.Passed, Is.EqualTo(1));
                using (JsonDocument sent = JsonDocument.Parse(server.Requests().Single()))
                {
                    JsonElement args = sent.RootElement.GetProperty("args");
                    Assert.That(args.GetProperty("filter").GetString(), Is.EqualTo("Ns.P.*"));
                    Assert.That(args.GetProperty("model").GetString(), Is.EqualTo("@active"));
                    Assert.That(args.GetProperty("parameters").GetProperty("family").GetString(), Is.EqualTo("Ванна"));
                }
            }
        }

        [Test]
        public void Run_passes_discovery_rows_and_warnings_through_unfiltered()
        {
            string runResult = "{\"summary\":{\"total\":2,\"passed\":1,\"failed\":0,\"errored\":1,\"skipped\":0,\"durationMs\":5},\"results\":["
                + "{\"class\":\"Ns.Broken\",\"method\":\"(discovery)\",\"status\":\"error\",\"message\":\"не удалось загрузить тип\",\"durationMs\":0},"
                + "{\"class\":\"Ns.P\",\"method\":\"M\",\"status\":\"passed\",\"durationMs\":5}],\"warnings\":[\"сборка X пропущена\"]}";
            using (FakeHostServer server = new FakeHostServer(r => FakeHostServer.Ok(r, runResult)))
            using (RevitHostClient client = Client(server))
            {
                HostRunReport report = client.Run("Ns.*");

                Assert.That(report.Results.Select(x => x.Method), Is.EqualTo(new[] { "(discovery)", "M" }));
                Assert.That(report.Warnings, Is.EqualTo(new[] { "сборка X пропущена" }));
                Assert.That(RevitHostClient.DescribeFailure("Ns.*", report), Does.Contain("[error] Ns.Broken.(discovery): не удалось загрузить тип"));
            }
        }

        [Test]
        public void Write_to_dropped_pipe_is_IOException_and_Dispose_does_not_throw()
        {
            using (FakeHostServer server = new FakeHostServer(r => FakeHostServer.Ping(r.GetProperty("id").GetInt64(), 5)))
            using (RevitHostClient client = Client(server))
            {
                Assert.That(client.Ping().Pid, Is.EqualTo(5));
                server.DropClient();

                // второй запрос уходит в канал, у которого нет серверного конца: канал «нарушен» (Broken), и освобождение
                // StreamWriter не должно падать на Flush — иначе IOException уйдёт из Drop() и из Dispose()
                Assert.That(() => client.Ping(), Throws.TypeOf<IOException>().With.Message.Contains("закрыт"));
                Assert.DoesNotThrow(client.Dispose);
            }
        }

        [Test]
        public void Server_disconnect_is_not_reported_as_busy()
        {
            FakeHostServer server = null;
            server = new FakeHostServer(r =>
            {
                server.DropClient();
                return null;
            });
            using (server)
            using (RevitHostClient client = Client(server))
            {
                Exception ex = Assert.Catch(() => client.Ping());

                Assert.That(ex, Is.TypeOf<IOException>());
                Assert.That(ex, Is.Not.InstanceOf<RevitBusyException>());
            }
        }

        [Test]
        public void AssertRun_fails_when_filter_matches_nothing()
        {
            string empty = "{\"summary\":{\"total\":0,\"passed\":0,\"failed\":0,\"errored\":0,\"skipped\":0,\"durationMs\":0},\"results\":[]}";
            using (FakeHostServer server = new FakeHostServer(r => FakeHostServer.Ok(r, empty)))
            using (RevitHostClient client = Client(server))
            {
                Assert.That(() => client.AssertRun("Ns.Опечатка.*"), Throws.TypeOf<AssertionException>().With.Message.Contains("не найдены").And.Message.Contains("Ns.Опечатка.*"));
            }
        }

        [Test]
        public void AssertRun_with_nothing_found_shows_discovery_warnings_and_Run_logs_them()
        {
            string empty = "{\"summary\":{\"total\":0,\"passed\":0,\"failed\":0,\"errored\":0,\"skipped\":0,\"durationMs\":0},\"results\":[],"
                + "\"warnings\":[\"сборка Plugin.RevitTests.dll пропущена: не найдена зависимость Plugin.Core\"]}";
            using (FakeHostServer server = new FakeHostServer(r => FakeHostServer.Ok(r, empty)))
            using (RevitHostClient client = Client(server))
            {
                Assert.That(() => client.AssertRun("Plugin.RevitTests.*"), Throws.TypeOf<AssertionException>()
                    .With.Message.Contains("total = 0")
                    .And.Message.Contains("сборка Plugin.RevitTests.dll пропущена: не найдена зависимость Plugin.Core"));
                Assert.That(UiLog.Recent(5).Any(l => l.Contains("Revit-тесты «Plugin.RevitTests.*»: всего 0") && l.Contains("сборка Plugin.RevitTests.dll пропущена")),
                    Is.True, "строка журнала Run с итогом содержит предупреждения: " + string.Join(" | ", UiLog.Recent(5)));
            }
        }

        [Test]
        public void Failure_message_with_failed_tests_also_shows_warnings()
        {
            HostRunReport failed = new HostRunReport(1, 0, 1, 0, 0, 10, "2022", new[] { new HostTestResult("Ns.P", "Bad", "failed", "семейства нет", null, "@active", 9) },
                new[] { "тип Ns.Broken пропущен" });

            Assert.That(RevitHostClient.DescribeFailure("Ns.P.*", failed), Does.Contain("[failed] Ns.P.Bad: семейства нет").And.Contain("тип Ns.Broken пропущен"));
        }

        [Test]
        public void AssertRun_lists_failures_and_all_skipped_is_failure()
        {
            HostRunReport failed = new HostRunReport(2, 1, 1, 0, 0, 10, "2022", new[]
            {
                new HostTestResult("Ns.P", "Ok", "passed", null, null, "@active", 1),
                new HostTestResult("Ns.P", "Bad", "failed", "семейства нет", null, "@active", 9),
            }, new string[0]);
            HostRunReport skipped = new HostRunReport(1, 0, 0, 0, 1, 0, "2022", new[] { new HostTestResult("Ns.P", "S", "skipped", "нет документа", null, "@active", 0) }, new string[0]);
            HostRunReport passed = new HostRunReport(1, 1, 0, 0, 0, 1, "2022", new[] { new HostTestResult("Ns.P", "Ok", "passed", null, null, "@active", 1) }, new string[0]);

            Assert.That(RevitHostClient.DescribeFailure("Ns.P.*", failed), Does.Contain("[failed] Ns.P.Bad: семейства нет"));
            Assert.That(RevitHostClient.DescribeFailure("Ns.P.*", skipped), Does.Contain("ни один не прошёл"));
            Assert.That(RevitHostClient.DescribeFailure("Ns.P.*", passed), Is.Null);
        }

        [Test]
        public void Open_rejects_relative_and_long_prefix_paths_before_sending()
        {
            using (RevitHostClient client = new RevitHostClient("RevitUITesting.Tests-unused", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), null, null))
            {
                Assert.That(() => client.Open(@"models\a.rvt", true), Throws.ArgumentException.With.Message.Contains("полным"));
                Assert.That(() => client.Open(@"\\?\C:\models\a.rvt", true), Throws.ArgumentException.With.Message.Contains("Illegal characters"));
            }
        }
    }
}
