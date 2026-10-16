using System.Collections.Generic;
using NUnit.Framework;
using RevitTestHost.Core.Json;

namespace RevitTestHost.Tests.Json
{
    [TestFixture]
    public class JsonReaderTests
    {
        [Test]
        public void ParsesNestedObject()
        {
            var value = JsonReader.Parse(" {\"id\": 7, \"cmd\": \"run\", \"args\": {\"parameters\": {\"k\": \"v\"}, \"list\": [1, 2.5, true, null]}} ");

            var root = (Dictionary<string, object?>)value!;
            Assert.That(root["id"], Is.EqualTo(7L));
            Assert.That(root["cmd"], Is.EqualTo("run"));
            var args = (Dictionary<string, object?>)root["args"]!;
            Assert.That(((Dictionary<string, object?>)args["parameters"]!)["k"], Is.EqualTo("v"));
            Assert.That(args["list"], Is.EqualTo(new List<object?> { 1L, 2.5d, true, null }));
        }

        [Test]
        public void ParsesEscapesAndUnicode()
        {
            var value = JsonReader.Parse("\"a\\\"b\\\\c\\/d\\n\\u041c\\u043e\"");
            Assert.That(value, Is.EqualTo("a\"b\\c/d\nМо"));
        }

        [Test]
        public void ParsesLongBeyondInt32()
        {
            Assert.That(JsonReader.Parse("9007199254740993"), Is.EqualTo(9007199254740993L));
        }

        [Test]
        public void IntegerOverflowingLong_BecomesDouble()
        {
            Assert.That(JsonReader.Parse("99999999999999999999"), Is.TypeOf<double>());
        }

        [Test]
        public void RoundTripsWriterOutput()
        {
            var json = new JsonWriter().BeginObject().Property("path", "C:\\Модели\\a \"b\".rvt").EndObject().ToString();
            var root = (Dictionary<string, object?>)JsonReader.Parse(json)!;
            Assert.That(root["path"], Is.EqualTo("C:\\Модели\\a \"b\".rvt"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("{")]
        [TestCase("{\"a\":1,}")]
        [TestCase("{\"a\" 1}")]
        [TestCase("{a:1}")]
        [TestCase("[1,]")]
        [TestCase("[1 2]")]
        [TestCase("\"abc")]
        [TestCase("\"a\\x\"")]
        [TestCase("\"a\\u12\"")]
        [TestCase("\"tab\tinside\"")]
        [TestCase("01")]
        [TestCase("-")]
        [TestCase("1.")]
        [TestCase("1e")]
        [TestCase("tru")]
        [TestCase("nul")]
        [TestCase("{} {}")]
        [TestCase("{\"a\":1,\"a\":2}")]
        [TestCase("1e999")]
        [TestCase("-1e999")]
        public void Malformed_ThrowsJsonFormatException(string text)
        {
            var ex = Assert.Throws<JsonFormatException>(() => JsonReader.Parse(text));
            Assert.That(ex!.Message, Does.StartWith("Некорректный JSON"));
        }

        [Test]
        public void TooDeep_ThrowsInsteadOfStackOverflow()
        {
            var text = new string('[', 10000) + new string(']', 10000);
            Assert.Throws<JsonFormatException>(() => JsonReader.Parse(text));
        }

        [Test]
        public void DepthAtLimit_Parses()
        {
            var text = new string('[', JsonReader.MaxDepth) + new string(']', JsonReader.MaxDepth);
            Assert.That(JsonReader.Parse(text), Is.InstanceOf<List<object?>>());
        }
    }
}
