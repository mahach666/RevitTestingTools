using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using RevitTestHost.Core.Json;
using RevitTestHost.Core.Protocol;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Tests.Protocol
{
    [TestFixture]
    public class RequestDispatcherTests
    {
        private string _tempDir = null!;
        private FakeExecutor _executor = null!;
        private RequestDispatcher _dispatcher = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "RevitTestHost.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _executor = new FakeExecutor();
            _dispatcher = new RequestDispatcher(_executor, new ServeWorkspace(Path.Combine(_tempDir, "serve")));
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        private Dictionary<string, object?> Call(string line, bool idle = true)
        {
            Assert.That(ProtocolCodec.TryParseRequest(line, out var request, out var error), Is.True, error?.ErrorMessage);
            var json = ProtocolCodec.FormatResponse(_dispatcher.Handle(request!, idle));
            Assert.That(json, Does.Not.Contain("\n"));
            return (Dictionary<string, object?>)JsonReader.Parse(json)!;
        }

        private static Dictionary<string, object?> Result(Dictionary<string, object?> response)
        {
            Assert.That(response["ok"], Is.EqualTo(true), () => "ожидался успех, получена ошибка: " + ErrorMessage(response));
            return (Dictionary<string, object?>)response["result"]!;
        }

        private static string ErrorCode(Dictionary<string, object?> response)
        {
            Assert.That(response["ok"], Is.EqualTo(false));
            return (string)((Dictionary<string, object?>)response["error"]!)["code"]!;
        }

        private static string ErrorMessage(Dictionary<string, object?> response) =>
            (string)((Dictionary<string, object?>)response["error"]!)["message"]!;

        [Test]
        public void Ping_WithoutDocument_HasContractFieldsAndNullActiveDocument()
        {
            var response = Call("{\"id\":1,\"cmd\":\"ping\"}", idle: true);

            Assert.That(response["id"], Is.EqualTo(1L));
            var result = Result(response);
            Assert.That(result.Keys, Is.EquivalentTo(new[] { "serverVersion", "revitVersion", "pid", "idle", "activeDocument" }));
            Assert.That(result["serverVersion"], Is.EqualTo("1.0"));
            Assert.That(result["revitVersion"], Is.EqualTo("2022"));
            Assert.That(result["pid"], Is.EqualTo((long)Process.GetCurrentProcess().Id));
            Assert.That(result["idle"], Is.EqualTo(true));
            Assert.That(result["activeDocument"], Is.Null);
        }

        [Test]
        public void Ping_ReportsActiveDocumentAndIdleFlag()
        {
            _executor.Docs.Add(new DocumentInfo("Другой", @"C:\b.rvt", false, false, false));
            _executor.Docs.Add(new DocumentInfo("Проект", @"C:\a.rvt", true, true, false));

            var result = Result(Call("{\"id\":2,\"cmd\":\"ping\"}", idle: false));

            Assert.That(result["idle"], Is.EqualTo(false));
            var active = (Dictionary<string, object?>)result["activeDocument"]!;
            Assert.That(active.Keys, Is.EquivalentTo(new[] { "title", "path", "isFamilyDocument" }));
            Assert.That(active["title"], Is.EqualTo("Проект"));
            Assert.That(active["path"], Is.EqualTo(@"C:\a.rvt"));
            Assert.That(active["isFamilyDocument"], Is.EqualTo(false));
        }

        [Test]
        public void Documents_ListsAllWithContractFields()
        {
            _executor.Docs.Add(new DocumentInfo("A", @"C:\a.rvt", true, true, false));
            _executor.Docs.Add(new DocumentInfo("Fam", @"C:\f.rfa", false, false, true));

            var list = (List<object?>)Result(Call("{\"id\":3,\"cmd\":\"documents\"}"))["documents"]!;

            Assert.That(list, Has.Count.EqualTo(2));
            var fam = (Dictionary<string, object?>)list[1]!;
            Assert.That(fam.Keys, Is.EquivalentTo(new[] { "title", "path", "isActive", "isModified", "isFamilyDocument" }));
            Assert.That(fam["isFamilyDocument"], Is.EqualTo(true));
            Assert.That(((Dictionary<string, object?>)list[0]!)["isModified"], Is.EqualTo(true));
        }

        [Test]
        public void UnknownCommand_IsUnknownCommand()
        {
            var response = Call("{\"id\":4,\"cmd\":\"PING\"}");
            Assert.That(ErrorCode(response), Is.EqualTo(ErrorCodes.UnknownCommand));
            Assert.That(response["id"], Is.EqualTo(4L));
        }

        [Test]
        public void OpenNew_CreatesNumberedFileInWorkspaceAndActivatesIt()
        {
            var first = Result(Call("{\"id\":5,\"cmd\":\"open\",\"args\":{\"path\":\"@new\"}}"));
            var second = Result(Call("{\"id\":6,\"cmd\":\"open\",\"args\":{\"path\":\"@new\"}}"));

            var serve = Path.Combine(_tempDir, "serve");
            Assert.That(first["path"], Is.EqualTo(Path.Combine(serve, "new-1.rvt")));
            Assert.That(first["title"], Is.EqualTo("new-1"));
            Assert.That(second["path"], Is.EqualTo(Path.Combine(serve, "new-2.rvt")));
            Assert.That(_executor.Calls[0], Is.EqualTo("create " + Path.Combine(serve, "new-1.rvt")));
            Assert.That(_executor.Calls[1], Is.EqualTo("open " + Path.Combine(serve, "new-1.rvt")));
        }

        [Test]
        public void OpenExisting_WithoutCopy_OpensOriginal()
        {
            var model = Path.Combine(_tempDir, "Модель.rvt");
            File.WriteAllText(model, "rvt");

            var result = Result(Call(Line(5, "open", new JsonPayload(w => w.BeginObject().Property("path", model).EndObject()))));

            Assert.That(result["path"], Is.EqualTo(model));
        }

        [Test]
        public void OpenExisting_WithCopy_OpensCopyAndKeepsOriginal()
        {
            var model = Path.Combine(_tempDir, "Модель.rvt");
            File.WriteAllText(model, "оригинал");
            File.SetAttributes(model, FileAttributes.ReadOnly);
            try
            {
                var args = new JsonPayload(w => w.BeginObject().Property("path", model).Property("copyToTemp", true).EndObject());
                var first = Result(Call(Line(5, "open", args)));
                var second = Result(Call(Line(6, "open", args)));

                var serve = Path.Combine(_tempDir, "serve");
                Assert.That(first["path"], Is.EqualTo(Path.Combine(serve, "Модель.rvt")));
                Assert.That(second["path"], Is.EqualTo(Path.Combine(serve, "Модель-2.rvt")));
                Assert.That(File.GetAttributes((string)first["path"]!) & FileAttributes.ReadOnly, Is.EqualTo((FileAttributes)0));
                Assert.That(File.ReadAllText(model), Is.EqualTo("оригинал"));
            }
            finally
            {
                File.SetAttributes(model, FileAttributes.Normal);
            }
        }

        [Test]
        public void OpenMissingFile_IsNotFound()
        {
            var missing = Path.Combine(_tempDir, "нет.rvt");
            var response = Call(Line(7, "open", new JsonPayload(w => w.BeginObject().Property("path", missing).EndObject())));

            Assert.That(ErrorCode(response), Is.EqualTo(ErrorCodes.NotFound));
            Assert.That(ErrorMessage(response), Does.Contain(missing));
            Assert.That(_executor.Calls, Is.Empty);
        }

        [TestCase("{\"id\":8,\"cmd\":\"open\"}")]
        [TestCase("{\"id\":8,\"cmd\":\"open\",\"args\":{\"path\":\"\"}}")]
        [TestCase("{\"id\":8,\"cmd\":\"open\",\"args\":{\"path\":\"models\\\\a.rvt\"}}")]
        [TestCase("{\"id\":8,\"cmd\":\"open\",\"args\":{\"path\":\"@active\"}}")]
        [TestCase("{\"id\":8,\"cmd\":\"open\",\"args\":{\"path\":5}}")]
        [TestCase("{\"id\":8,\"cmd\":\"open\",\"args\":{\"path\":\"@new\",\"copyToTemp\":\"yes\"}}")]
        [TestCase("{\"id\":8,\"cmd\":\"open\",\"args\":{\"path\":\"C:\\\\a|b.rvt\"}}")]
        [TestCase("{\"id\":8,\"cmd\":\"open\",\"args\":{\"path\":\"C:a.rvt\"}}")]
        [TestCase("{\"id\":8,\"cmd\":\"open\",\"args\":{\"path\":\"\\\\a.rvt\"}}")]
        public void OpenBadArgs_IsBadRequest(string line)
        {
            var response = Call(line);
            Assert.That(ErrorCode(response), Is.EqualTo(ErrorCodes.BadRequest));
            Assert.That(response["id"], Is.EqualTo(8L));
        }

        [Test]
        public void Close_NonActiveByTitle_ClosesIt()
        {
            _executor.Docs.Add(new DocumentInfo("Старая", @"C:\old.rvt", false, true, false));
            _executor.Docs.Add(new DocumentInfo("Новая", @"C:\new.rvt", true, false, false));

            var result = Result(Call("{\"id\":9,\"cmd\":\"close\",\"args\":{\"title\":\"старая.rvt\",\"save\":false}}"));

            Assert.That(result["closed"], Is.EqualTo(true));
            Assert.That(_executor.Calls, Is.EqualTo(new[] { "close Старая" }));
        }

        [Test]
        public void Close_ByPath_IgnoresCaseAndSeparators()
        {
            _executor.Docs.Add(new DocumentInfo("Old", @"C:\Models\old.rvt", false, false, false));
            _executor.Docs.Add(new DocumentInfo("New", @"C:\Models\new.rvt", true, false, false));

            Result(Call("{\"id\":9,\"cmd\":\"close\",\"args\":{\"path\":\"c:/models/OLD.rvt\"}}"));

            Assert.That(_executor.Calls, Is.EqualTo(new[] { "close Old" }));
        }

        private void AddDocsWithRevitServerModel()
        {
            _executor.Docs.Add(new DocumentInfo("RS", "RSN://srv/p/a.rvt", false, false, false));
            _executor.Docs.Add(new DocumentInfo("Old", @"C:\Models\old.rvt", false, false, false));
            _executor.Docs.Add(new DocumentInfo("New", @"C:\Models\new.rvt", true, false, false));
        }

        [Test]
        public void Close_ByPath_WithRevitServerDocumentOpen_ClosesMatchingFileDocument()
        {
            AddDocsWithRevitServerModel();

            Result(Call("{\"id\":9,\"cmd\":\"close\",\"args\":{\"path\":\"c:/models/OLD.rvt\"}}"));

            Assert.That(_executor.Calls, Is.EqualTo(new[] { "close Old" }));
        }

        [Test]
        public void Close_ByTitle_RevitServerDocument_ClosesIt()
        {
            AddDocsWithRevitServerModel();

            Result(Call("{\"id\":9,\"cmd\":\"close\",\"args\":{\"title\":\"RS\"}}"));

            Assert.That(_executor.Calls, Is.EqualTo(new[] { "close RS" }));
        }

        [Test]
        public void Close_ByUnknownPath_WithRevitServerDocumentOpen_IsNotFoundListingIt()
        {
            AddDocsWithRevitServerModel();

            var response = Call("{\"id\":9,\"cmd\":\"close\",\"args\":{\"path\":\"C:\\\\none.rvt\"}}");

            Assert.That(ErrorCode(response), Is.EqualTo(ErrorCodes.NotFound));
            Assert.That(ErrorMessage(response), Does.Contain("RS"));
            Assert.That(_executor.Calls, Is.Empty);
        }

        [Test]
        public void Close_Active_IsActiveDocumentAndDoesNotClose()
        {
            _executor.Docs.Add(new DocumentInfo("Новая", @"C:\new.rvt", true, false, false));

            var response = Call("{\"id\":10,\"cmd\":\"close\",\"args\":{\"title\":\"Новая\"}}");

            Assert.That(ErrorCode(response), Is.EqualTo(ErrorCodes.ActiveDocument));
            Assert.That(_executor.Calls, Is.Empty);
        }

        [Test]
        public void Close_Unknown_IsNotFoundWithOpenTitles()
        {
            _executor.Docs.Add(new DocumentInfo("Новая", @"C:\new.rvt", true, false, false));

            var response = Call("{\"id\":11,\"cmd\":\"close\",\"args\":{\"title\":\"Нет такой\"}}");

            Assert.That(ErrorCode(response), Is.EqualTo(ErrorCodes.NotFound));
            Assert.That(ErrorMessage(response), Does.Contain("Новая"));
        }

        [Test]
        public void Close_AmbiguousTitle_IsBadRequest()
        {
            _executor.Docs.Add(new DocumentInfo("a", @"C:\x\a.rvt", false, false, false));
            _executor.Docs.Add(new DocumentInfo("a", @"C:\y\a.rvt", false, false, false));
            _executor.Docs.Add(new DocumentInfo("b", @"C:\b.rvt", true, false, false));

            Assert.That(ErrorCode(Call("{\"id\":12,\"cmd\":\"close\",\"args\":{\"title\":\"a\"}}")), Is.EqualTo(ErrorCodes.BadRequest));
        }

        [TestCase("{\"id\":13,\"cmd\":\"close\"}")]
        [TestCase("{\"id\":13,\"cmd\":\"close\",\"args\":{\"title\":\"a\",\"path\":\"C:\\\\a.rvt\"}}")]
        [TestCase("{\"id\":13,\"cmd\":\"close\",\"args\":{\"title\":\"a\",\"save\":true}}")]
        [TestCase("{\"id\":13,\"cmd\":\"close\",\"args\":{\"path\":\"a.rvt\"}}")]
        [TestCase("{\"id\":13,\"cmd\":\"close\",\"args\":{\"path\":\"C:a.rvt\"}}")]
        [TestCase("{\"id\":13,\"cmd\":\"close\",\"args\":{\"path\":\"\\\\a.rvt\"}}")]
        public void Close_BadArgs_IsBadRequest(string line)
        {
            Assert.That(ErrorCode(Call(line)), Is.EqualTo(ErrorCodes.BadRequest));
        }

        [Test]
        public void Run_DefaultsToActiveDocumentAndPassesFilterAndParameters()
        {
            _executor.Docs.Add(new DocumentInfo("Проект", @"C:\a.rvt", true, false, false));

            var result = Result(Call("{\"id\":14,\"cmd\":\"run\",\"args\":{\"filter\":\"Ns.Probe.*\",\"parameters\":{\"family\":\"Ванна\",\"count\":\"2\"}}}"));

            Assert.That(_executor.LastFilter, Is.EqualTo("Ns.Probe.*"));
            Assert.That(_executor.LastParameters, Is.EqualTo(new Dictionary<string, string> { ["family"] = "Ванна", ["count"] = "2" }));
            Assert.That(_executor.LastRunDocument!.Title, Is.EqualTo("Проект"));
            var summary = (Dictionary<string, object?>)result["summary"]!;
            Assert.That(summary["passed"], Is.EqualTo(1L));
            var first = (Dictionary<string, object?>)((List<object?>)result["results"]!)[0]!;
            Assert.That(first["status"], Is.EqualTo("passed"));
            Assert.That(first["model"], Is.EqualTo("@active"));
        }

        [Test]
        public void Run_WithoutActiveDocument_PassesNullAndEmptyParameters()
        {
            Result(Call("{\"id\":15,\"cmd\":\"run\",\"args\":{\"filter\":\"X.*\"}}"));

            Assert.That(_executor.LastRunDocument, Is.Null);
            Assert.That(_executor.LastParameters, Is.Empty);
        }

        [Test]
        public void Run_ModelByTitle_UsesThatOpenDocument()
        {
            _executor.Docs.Add(new DocumentInfo("Фон", @"C:\bg.rvt", false, false, false));
            _executor.Docs.Add(new DocumentInfo("Проект", @"C:\a.rvt", true, false, false));

            Result(Call("{\"id\":16,\"cmd\":\"run\",\"args\":{\"model\":\"Фон\"}}"));

            Assert.That(_executor.LastRunDocument!.Title, Is.EqualTo("Фон"));
        }

        [Test]
        public void Run_ModelByPath_UsesThatOpenDocument()
        {
            _executor.Docs.Add(new DocumentInfo("Фон", @"C:\bg.rvt", false, false, false));
            _executor.Docs.Add(new DocumentInfo("Проект", @"C:\a.rvt", true, false, false));

            Result(Call("{\"id\":16,\"cmd\":\"run\",\"args\":{\"model\":\"c:/BG.rvt\"}}"));

            Assert.That(_executor.LastRunDocument!.Title, Is.EqualTo("Фон"));
        }

        [Test]
        public void Run_ModelNotOpen_IsNotFoundAndDoesNotRun()
        {
            var response = Call("{\"id\":17,\"cmd\":\"run\",\"args\":{\"model\":\"C:\\\\none.rvt\"}}");

            Assert.That(ErrorCode(response), Is.EqualTo(ErrorCodes.NotFound));
            Assert.That(_executor.Calls, Is.Empty);
        }

        [TestCase("{\"id\":18,\"cmd\":\"run\",\"args\":{\"parameters\":{\"n\":5}}}")]
        [TestCase("{\"id\":18,\"cmd\":\"run\",\"args\":{\"parameters\":[\"a\"]}}")]
        [TestCase("{\"id\":18,\"cmd\":\"run\",\"args\":{\"filter\":7}}")]
        [TestCase("{\"id\":18,\"cmd\":\"run\",\"args\":{\"model\":\"\"}}")]
        public void Run_BadArgs_IsBadRequest(string line)
        {
            Assert.That(ErrorCode(Call(line)), Is.EqualTo(ErrorCodes.BadRequest));
            Assert.That(_executor.Calls, Is.Empty);
        }

        [Test]
        public void ExecutorException_IsRevitErrorWithTypeAndMessage()
        {
            _executor.ThrowOnDocuments = new InvalidOperationException("Revit сломался");

            var response = Call("{\"id\":19,\"cmd\":\"documents\"}");

            Assert.That(ErrorCode(response), Is.EqualTo(ErrorCodes.RevitError));
            Assert.That(ErrorMessage(response), Is.EqualTo("System.InvalidOperationException: Revit сломался"));
            Assert.That(response["id"], Is.EqualTo(19L));
        }

        [Test]
        public void ExecutorHostCommandException_KeepsItsCode()
        {
            _executor.ThrowOnDocuments = new HostCommandException(ErrorCodes.NotFound, "нет");

            Assert.That(ErrorCode(Call("{\"id\":20,\"cmd\":\"documents\"}")), Is.EqualTo(ErrorCodes.NotFound));
        }

        private static string Line(long id, string cmd, IJsonWritable args) =>
            new JsonWriter().BeginObject().Property("id", id).Property("cmd", cmd).Name("args").Also(args).EndObject().ToString();
    }

    internal static class JsonWriterTestExtensions
    {
        public static JsonWriter Also(this JsonWriter writer, IJsonWritable value)
        {
            value.WriteJson(writer);
            return writer;
        }
    }
}
