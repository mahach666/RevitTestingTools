using NUnit.Framework;
using RevitTestHost.Core.Json;
using RevitTestHost.Core.Protocol;

namespace RevitTestHost.Tests.Protocol
{
    [TestFixture]
    public class ProtocolCodecTests
    {
        [Test]
        public void ParsesRequestWithArgs()
        {
            var ok = ProtocolCodec.TryParseRequest(
                "{\"id\":7,\"cmd\":\"open\",\"args\":{\"path\":\"@new\",\"copyToTemp\":true}}", out var request, out var error);

            Assert.That(ok, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(request!.Id, Is.EqualTo(7L));
            Assert.That(request.Command, Is.EqualTo("open"));
            Assert.That(request.Args["path"], Is.EqualTo("@new"));
            Assert.That(request.Args["copyToTemp"], Is.EqualTo(true));
        }

        [TestCase("{\"id\":1,\"cmd\":\"ping\"}")]
        [TestCase("{\"id\":1,\"cmd\":\"ping\",\"args\":null}")]
        [TestCase("{\"id\":1,\"cmd\":\"ping\",\"args\":{}}")]
        public void MissingOrNullArgs_AreEmpty(string line)
        {
            Assert.That(ProtocolCodec.TryParseRequest(line, out var request, out _), Is.True);
            Assert.That(request!.Args, Is.Empty);
        }

        [Test]
        public void UnknownTopLevelFields_AreIgnored()
        {
            Assert.That(ProtocolCodec.TryParseRequest("{\"id\":1,\"cmd\":\"ping\",\"trace\":\"x\"}", out _, out _), Is.True);
        }

        [TestCase("not json")]
        [TestCase("{\"id\":1,\"cmd\":")]
        [TestCase("[1,2]")]
        [TestCase("\"ping\"")]
        [TestCase("{\"cmd\":\"ping\"}")]
        [TestCase("{\"id\":\"7\",\"cmd\":\"ping\"}")]
        [TestCase("{\"id\":1.5,\"cmd\":\"ping\"}")]
        [TestCase("{\"id\":null,\"cmd\":\"ping\"}")]
        public void Malformed_WithoutReadableId_IsBadRequestWithIdZero(string line)
        {
            Assert.That(ProtocolCodec.TryParseRequest(line, out var request, out var error), Is.False);
            Assert.That(request, Is.Null);
            Assert.That(error!.Ok, Is.False);
            Assert.That(error.Id, Is.EqualTo(ProtocolCodec.UnknownRequestId));
            Assert.That(error.ErrorCode, Is.EqualTo(ErrorCodes.BadRequest));
            Assert.That(error.ErrorMessage, Is.Not.Empty);
        }

        [TestCase("{\"id\":9,\"cmd\":\"\"}")]
        [TestCase("{\"id\":9}")]
        [TestCase("{\"id\":9,\"cmd\":5}")]
        [TestCase("{\"id\":9,\"cmd\":\"ping\",\"args\":[1]}")]
        [TestCase("{\"id\":9,\"cmd\":\"ping\",\"args\":\"x\"}")]
        public void Malformed_WithReadableId_IsBadRequestWithThatId(string line)
        {
            Assert.That(ProtocolCodec.TryParseRequest(line, out _, out var error), Is.False);
            Assert.That(error!.Id, Is.EqualTo(9L));
            Assert.That(error.ErrorCode, Is.EqualTo(ErrorCodes.BadRequest));
        }

        [Test]
        public void FormatsSuccessExactly()
        {
            var response = HostResponse.Success(7, new JsonPayload(w => w.BeginObject().Property("closed", true).EndObject()));

            Assert.That(ProtocolCodec.FormatResponse(response), Is.EqualTo("{\"id\":7,\"ok\":true,\"result\":{\"closed\":true}}"));
        }

        [Test]
        public void FormatsFailureExactly()
        {
            var response = HostResponse.Failure(3, ErrorCodes.ActiveDocument, "активный документ\nзакрывается через UI");

            Assert.That(ProtocolCodec.FormatResponse(response), Is.EqualTo(
                "{\"id\":3,\"ok\":false,\"error\":{\"code\":\"ACTIVE_DOCUMENT\",\"message\":\"активный документ\\nзакрывается через UI\"}}"));
        }
    }
}
