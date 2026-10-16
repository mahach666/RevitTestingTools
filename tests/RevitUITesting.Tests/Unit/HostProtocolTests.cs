using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RevitUITesting.Revit;
using RevitUITesting.Uia;

namespace RevitUITesting.Tests.Unit
{
    [TestFixture]
    public class HostProtocolTests
    {
        [Test]
        public void Request_is_single_line_with_id_cmd_and_empty_args()
        {
            Assert.That(HostProtocol.Request(7, "ping", null), Is.EqualTo("{\"id\":7,\"cmd\":\"ping\",\"args\":{}}"));
        }

        [Test]
        public void Request_writes_nested_parameters_keeps_cyrillic_and_escapes_newlines()
        {
            string line = HostProtocol.Request(8, "run", new Dictionary<string, object>
            {
                ["filter"] = "Ns.Class.*",
                ["model"] = "@active",
                ["parameters"] = new Dictionary<string, string> { ["family"] = "Ванна \"угловая\"\nвторая строка" },
            });

            Assert.That(line, Is.EqualTo("{\"id\":8,\"cmd\":\"run\",\"args\":{\"filter\":\"Ns.Class.*\",\"model\":\"@active\",\"parameters\":{\"family\":\"Ванна \\\"угловая\\\"\\nвторая строка\"}}}"));
            Assert.That(line, Does.Not.Contain("\n"));
        }

        [Test]
        public void Request_writes_booleans()
        {
            string line = HostProtocol.Request(9, "open", new Dictionary<string, object> { ["path"] = "@new", ["copyToTemp"] = true });
            Assert.That(line, Is.EqualTo("{\"id\":9,\"cmd\":\"open\",\"args\":{\"path\":\"@new\",\"copyToTemp\":true}}"));
        }

        [Test]
        public void Request_rejects_unsupported_value_type()
        {
            Assert.That(() => HostProtocol.Request(1, "run", new Dictionary<string, object> { ["x"] = 1.5 }),
                Throws.ArgumentException.With.Message.Contains("Double"));
        }

        [Test]
        public void Ok_ping_response_is_read()
        {
            HostResponse response = HostProtocol.ParseResponse(
                "{\"id\":3,\"ok\":true,\"result\":{\"serverVersion\":\"1.0\",\"revitVersion\":\"2022\",\"pid\":4242,\"idle\":true," +
                "\"activeDocument\":{\"title\":\"new-1\",\"path\":\"C:\\\\Temp\\\\new-1.rvt\",\"isFamilyDocument\":false}}}");

            Assert.That(response.Id, Is.EqualTo(3));
            Assert.That(response.Ok, Is.True);
            HostPing ping = HostProtocol.ReadPing(response.Result);
            Assert.That(ping.ServerVersion, Is.EqualTo("1.0"));
            Assert.That(ping.RevitVersion, Is.EqualTo("2022"));
            Assert.That(ping.Pid, Is.EqualTo(4242));
            Assert.That(ping.Idle, Is.True);
            Assert.That(ping.ActiveDocument.Title, Is.EqualTo("new-1"));
            Assert.That(ping.ActiveDocument.Path, Is.EqualTo(@"C:\Temp\new-1.rvt"));
            Assert.That(ping.ActiveDocument.IsActive, Is.True);
        }

        [Test]
        public void Ping_without_document_has_null_active_document()
        {
            HostResponse response = HostProtocol.ParseResponse("{\"id\":1,\"ok\":true,\"result\":{\"serverVersion\":\"1.0\",\"revitVersion\":\"2022\",\"pid\":1,\"idle\":false,\"activeDocument\":null}}");
            Assert.That(HostProtocol.ReadPing(response.Result).ActiveDocument, Is.Null);
        }

        [Test]
        public void Ping_with_unsaved_active_document_has_empty_path()
        {
            HostResponse response = HostProtocol.ParseResponse(
                "{\"id\":1,\"ok\":true,\"result\":{\"serverVersion\":\"1.0\",\"revitVersion\":\"2022\",\"pid\":1,\"idle\":true," +
                "\"activeDocument\":{\"title\":\"Project1\",\"path\":\"\",\"isFamilyDocument\":false}}}");
            Assert.That(HostProtocol.ReadPing(response.Result).ActiveDocument.Path, Is.EqualTo(""));
        }

        [Test]
        public void Ping_without_pid_is_rejected_with_field_name()
        {
            HostResponse response = HostProtocol.ParseResponse("{\"id\":1,\"ok\":true,\"result\":{\"serverVersion\":\"1.0\",\"revitVersion\":\"2022\",\"idle\":true}}");
            Assert.That(() => HostProtocol.ReadPing(response.Result), Throws.TypeOf<InvalidDataException>().With.Message.Contains("«pid»"));
        }

        [Test]
        public void Ping_result_that_is_not_an_object_is_InvalidDataException()
        {
            HostResponse response = HostProtocol.ParseResponse("{\"id\":1,\"ok\":true,\"result\":[]}");
            Assert.That(() => HostProtocol.ReadPing(response.Result), Throws.TypeOf<InvalidDataException>().With.Message.Contains("«serverVersion»"));
        }

        [Test]
        public void Error_response_keeps_code_and_message()
        {
            HostResponse response = HostProtocol.ParseResponse("{\"id\":4,\"ok\":false,\"error\":{\"code\":\"ACTIVE_DOCUMENT\",\"message\":\"активный документ закрывает UI\"}}");
            Assert.That(response.Ok, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(HostErrorCodes.ActiveDocument));
            Assert.That(response.ErrorMessage, Is.EqualTo("активный документ закрывает UI"));
        }

        [TestCase("not json", "не JSON")]
        [TestCase("[1,2]", "объект")]
        [TestCase("{\"ok\":true,\"result\":{}}", "«id»")]
        [TestCase("{\"id\":1,\"result\":{}}", "«ok»")]
        [TestCase("{\"id\":1,\"ok\":true}", "«result»")]
        [TestCase("{\"id\":1,\"ok\":false}", "«error»")]
        [TestCase("{\"id\":1,\"ok\":false,\"error\":{\"message\":\"x\"}}", "«code»")]
        public void Broken_response_is_InvalidDataException(string line, string expected)
        {
            Assert.That(() => HostProtocol.ParseResponse(line), Throws.TypeOf<InvalidDataException>().With.Message.Contains(expected));
        }

        [Test]
        public void Documents_are_read()
        {
            HostResponse response = HostProtocol.ParseResponse(
                "{\"id\":5,\"ok\":true,\"result\":{\"documents\":[" +
                "{\"title\":\"a\",\"path\":\"C:\\\\a.rvt\",\"isActive\":true,\"isModified\":false,\"isFamilyDocument\":false}," +
                "{\"title\":\"b\",\"path\":\"\",\"isActive\":false,\"isModified\":true,\"isFamilyDocument\":true}]}}");

            IReadOnlyList<HostDocument> docs = HostProtocol.ReadDocuments(response.Result);

            Assert.That(docs, Has.Count.EqualTo(2));
            Assert.That(docs[0].IsActive, Is.True);
            Assert.That(docs[1].IsModified, Is.True);
            Assert.That(docs[1].IsFamilyDocument, Is.True);
            Assert.That(docs[1].Path, Is.EqualTo(""));
        }

        [Test]
        public void Closed_result_must_be_true()
        {
            Assert.That(HostProtocol.ReadClosed(HostProtocol.ParseResponse("{\"id\":1,\"ok\":true,\"result\":{\"closed\":true}}").Result), Is.True);
            Assert.That(HostProtocol.ReadClosed(HostProtocol.ParseResponse("{\"id\":1,\"ok\":true,\"result\":{\"closed\":false}}").Result), Is.False);
        }

        [Test]
        public void Run_report_is_read_in_JsonReporter_format()
        {
            HostResponse response = HostProtocol.ParseResponse(
                "{\"id\":6,\"ok\":true,\"result\":{\"summary\":{\"total\":2,\"passed\":1,\"failed\":1,\"errored\":0,\"skipped\":0,\"durationMs\":120,\"revitVersion\":\"2022\",\"host\":\"PC\",\"startedAt\":\"2026-10-07T20:00:00.0000000Z\"}," +
                "\"results\":[{\"class\":\"Ns.Probe\",\"method\":\"Ok\",\"status\":\"passed\",\"durationMs\":10}," +
                "{\"class\":\"Ns.Probe\",\"method\":\"Bad\",\"model\":\"@active\",\"status\":\"failed\",\"message\":\"семейства нет\",\"stackTrace\":\"at X\",\"durationMs\":110}]}}");

            HostRunReport report = HostProtocol.ReadRun(response.Result);

            Assert.That(report.Total, Is.EqualTo(2));
            Assert.That(report.Failed, Is.EqualTo(1));
            Assert.That(report.DurationMs, Is.EqualTo(120));
            Assert.That(report.Results[1].FullName, Is.EqualTo("Ns.Probe.Bad"));
            Assert.That(report.Results[1].Model, Is.EqualTo("@active"));
            Assert.That(report.Results[1].StackTrace, Is.EqualTo("at X"));
            Assert.That(report.Failures, Has.Count.EqualTo(1));
            Assert.That(report.Failures[0].Message, Is.EqualTo("семейства нет"));
            Assert.That(report.Results[0].Message, Is.Null);
            Assert.That(report.Warnings, Is.Empty);
        }

        [Test]
        public void Run_report_with_warnings_exposes_them()
        {
            HostResponse response = HostProtocol.ParseResponse(
                "{\"id\":6,\"ok\":true,\"result\":{\"summary\":{\"total\":1,\"passed\":1,\"failed\":0,\"errored\":0,\"skipped\":0,\"durationMs\":5,\"revitVersion\":\"2022\"}," +
                "\"results\":[{\"class\":\"Ns.Probe\",\"method\":\"Ok\",\"status\":\"passed\",\"durationMs\":5}]," +
                "\"warnings\":[\"сборка A.dll пропущена: нет зависимости\",\"тип Ns.Broken пропущен\"]}}");

            HostRunReport report = HostProtocol.ReadRun(response.Result);

            Assert.That(report.Total, Is.EqualTo(1));
            Assert.That(report.Warnings, Is.EqualTo(new[] { "сборка A.dll пропущена: нет зависимости", "тип Ns.Broken пропущен" }));
        }

        [Test]
        public void Run_report_with_non_string_warning_is_rejected()
        {
            HostResponse response = HostProtocol.ParseResponse(
                "{\"id\":6,\"ok\":true,\"result\":{\"summary\":{\"total\":0,\"passed\":0,\"failed\":0,\"errored\":0,\"skipped\":0,\"durationMs\":0}," +
                "\"results\":[],\"warnings\":[1]}}");

            Assert.That(() => HostProtocol.ReadRun(response.Result), Throws.TypeOf<InvalidDataException>().With.Message.Contains("«warnings»"));
        }

        [Test]
        public void Run_report_keeps_discovery_row_as_ordinary_result()
        {
            HostResponse response = HostProtocol.ParseResponse(
                "{\"id\":6,\"ok\":true,\"result\":{\"summary\":{\"total\":1,\"passed\":0,\"failed\":0,\"errored\":1,\"skipped\":0,\"durationMs\":1,\"revitVersion\":\"2022\"}," +
                "\"results\":[{\"class\":\"Ns.Broken\",\"method\":\"(discovery)\",\"status\":\"error\",\"message\":\"не удалось загрузить тип\",\"durationMs\":0}]}}");

            HostRunReport report = HostProtocol.ReadRun(response.Result);

            Assert.That(report.Results, Has.Count.EqualTo(1));
            Assert.That(report.Results[0].Method, Is.EqualTo("(discovery)"));
            Assert.That(report.Results[0].FullName, Is.EqualTo("Ns.Broken.(discovery)"));
            Assert.That(report.Failures, Has.Count.EqualTo(1));
        }

        [Test]
        public void Busy_exception_is_app_busy_and_names_command_and_windows()
        {
            RevitBusyException ex = new RevitBusyException("open", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60.2), "открыты окна: «Save changes» (#32770)");

            Assert.That(ex, Is.InstanceOf<AppBusyException>());
            Assert.That(ex, Is.InstanceOf<TimeoutException>());
            Assert.That(ex.Command, Is.EqualTo("open"));
            Assert.That(ex.Message, Does.Contain("«open»").And.Contain("Save changes"));
        }

        [Test]
        public void Host_error_and_exit_exceptions_keep_details()
        {
            HostErrorException error = new HostErrorException("close", HostErrorCodes.ActiveDocument, "нельзя");
            Assert.That(error.Code, Is.EqualTo("ACTIVE_DOCUMENT"));
            Assert.That(error.Message, Does.Contain("«close»").And.Contain("ACTIVE_DOCUMENT").And.Contain("нельзя"));

            RevitExitedException exited = new RevitExitedException(77, -1073741819, "' 0:< хвост");
            Assert.That(exited.ExitCode, Is.EqualTo(-1073741819));
            Assert.That(exited.Message, Does.Contain("pid 77").And.Contain("-1073741819").And.Contain("хвост"));
            Assert.That(new RevitExitedException(5, null, null).Message, Does.Contain("неизвестен"));
        }
    }
}
