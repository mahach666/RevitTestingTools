using System;
using NUnit.Framework;
using RevitTestHost.Core.Json;

namespace RevitTestHost.Tests.Json
{
    [TestFixture]
    public class JsonWriterTests
    {
        [Test]
        public void Compact_WritesObjectWithoutWhitespace()
        {
            var json = new JsonWriter()
                .BeginObject()
                .Property("id", 7)
                .Property("ok", true)
                .Name("result").Null()
                .Name("items").BeginArray().String("a").Number(-1).Bool(false).EndArray()
                .EndObject()
                .ToString();

            Assert.That(json, Is.EqualTo("{\"id\":7,\"ok\":true,\"result\":null,\"items\":[\"a\",-1,false]}"));
        }

        [Test]
        public void Compact_EscapesControlCharsSoOutputIsSingleLine()
        {
            var json = new JsonWriter().String("a\"b\\c\nd\re\tf\u0001g").ToString();

            Assert.That(json, Is.EqualTo("\"a\\\"b\\\\c\\nd\\re\\tf\\u0001g\""));
            Assert.That(json, Does.Not.Contain("\n"));
        }

        [Test]
        public void Compact_KeepsCyrillicAsIs()
        {
            Assert.That(new JsonWriter().String("Модель.rvt").ToString(), Is.EqualTo("\"Модель.rvt\""));
        }

        [Test]
        public void Indented_UsesTwoSpacesAndColonSpace()
        {
            var json = new JsonWriter(indented: true)
                .BeginObject()
                .Name("summary").BeginObject().Property("total", 1).Property("host", "x").EndObject()
                .Name("results").BeginArray().EndArray()
                .Name("empty").BeginObject().EndObject()
                .EndObject()
                .ToString();

            Assert.That(json, Is.EqualTo(
                "{\n" +
                "  \"summary\": {\n" +
                "    \"total\": 1,\n" +
                "    \"host\": \"x\"\n" +
                "  },\n" +
                "  \"results\": [],\n" +
                "  \"empty\": {}\n" +
                "}"));
        }

        [Test]
        public void NullString_WritesNullLiteral()
        {
            Assert.That(new JsonWriter().String(null).ToString(), Is.EqualTo("null"));
        }

        [Test]
        public void ValueWithoutNameInObject_Throws()
        {
            var writer = new JsonWriter().BeginObject();
            Assert.Throws<InvalidOperationException>(() => writer.Number(1));
        }

        [Test]
        public void UnclosedObject_ToStringThrows()
        {
            var writer = new JsonWriter().BeginObject();
            Assert.Throws<InvalidOperationException>(() => writer.ToString());
        }

        [Test]
        public void SecondRootValue_Throws()
        {
            var writer = new JsonWriter().Number(1);
            Assert.Throws<InvalidOperationException>(() => writer.Number(2));
        }

        [Test]
        public void NameWithoutValue_EndObjectThrows()
        {
            var writer = new JsonWriter().BeginObject().Name("a");
            Assert.Throws<InvalidOperationException>(() => writer.EndObject());
        }
    }
}
