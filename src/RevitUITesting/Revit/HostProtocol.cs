using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RevitUITesting.Revit
{
    /// <summary>Разобранный ответ сервера: result хранится копией (Clone), документ JSON освобождён.</summary>
    internal sealed class HostResponse
    {
        internal HostResponse(long id, bool ok, JsonElement result, string errorCode, string errorMessage)
        {
            Id = id;
            Ok = ok;
            Result = result;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        internal long Id { get; }

        internal bool Ok { get; }

        internal JsonElement Result { get; }

        internal string ErrorCode { get; }

        internal string ErrorMessage { get; }
    }

    /// <summary>
    /// Протокол канала RevitTestHost (контракт 1): одна строка UTF-8 = один JSON-объект. Запрос {"id","cmd","args"}, ответ
    /// {"id","ok","result"} или {"id","ok":false,"error":{"code","message"}}. Кириллица пишется как есть (UTF-8), не \u-escape.
    /// Разбор строгий: нет обязательного поля — <see cref="InvalidDataException"/> с именем поля.
    /// </summary>
    internal static class HostProtocol
    {
        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        internal static string Request(long id, string command, IReadOnlyDictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(command))
            {
                throw new ArgumentException("Нужна команда", nameof(command));
            }
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, WriterOptions))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("id", id);
                    writer.WriteString("cmd", command);
                    writer.WritePropertyName("args");
                    WriteValue(writer, args ?? new Dictionary<string, object>());
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static void WriteValue(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    return;
                case string text:
                    writer.WriteStringValue(text);
                    return;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    return;
                case int number:
                    writer.WriteNumberValue(number);
                    return;
                case long number:
                    writer.WriteNumberValue(number);
                    return;
                case IReadOnlyDictionary<string, object> map:
                    writer.WriteStartObject();
                    foreach (KeyValuePair<string, object> pair in map)
                    {
                        writer.WritePropertyName(pair.Key);
                        WriteValue(writer, pair.Value);
                    }
                    writer.WriteEndObject();
                    return;
                case IEnumerable<KeyValuePair<string, string>> strings:
                    writer.WriteStartObject();
                    foreach (KeyValuePair<string, string> pair in strings)
                    {
                        writer.WriteString(pair.Key, pair.Value);
                    }
                    writer.WriteEndObject();
                    return;
                default:
                    throw new ArgumentException($"Тип аргумента {value.GetType().Name} не поддерживается протоколом RevitTestHost (строка, bool, int, long, словарь).");
            }
        }

        internal static HostResponse ParseResponse(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                throw new InvalidDataException("Пустая строка ответа RevitTestHost.");
            }
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Ответ RevitTestHost — не JSON: " + Cut(line), ex);
            }
            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Ответ RevitTestHost должен быть JSON-объектом: " + Cut(line));
                }
                const string what = "ответ RevitTestHost";
                long id = Long(root, "id", what);
                bool ok = Bool(root, "ok", what);
                if (ok)
                {
                    return new HostResponse(id, true, Prop(root, "result", what).Clone(), null, null);
                }
                JsonElement error = Prop(root, "error", what);
                return new HostResponse(id, false, default, Str(error, "code", what + " (error)"), OptStr(error, "message") ?? "");
            }
        }

        internal static HostPing ReadPing(JsonElement result)
        {
            const string what = "ответ на «ping»";
            string serverVersion = Str(result, "serverVersion", what);
            string revitVersion = Str(result, "revitVersion", what);
            int pid = (int)Long(result, "pid", what);
            bool idle = Bool(result, "idle", what);
            HostDocument active = null;
            if (result.TryGetProperty("activeDocument", out JsonElement doc) && doc.ValueKind == JsonValueKind.Object)
            {
                active = new HostDocument(Str(doc, "title", what + " (activeDocument)"), OptStr(doc, "path"), true, false, Bool(doc, "isFamilyDocument", what + " (activeDocument)"));
            }
            return new HostPing(serverVersion, revitVersion, pid, idle, active);
        }

        internal static IReadOnlyList<HostDocument> ReadDocuments(JsonElement result)
        {
            const string what = "ответ на «documents»";
            JsonElement array = Prop(result, "documents", what);
            if (array.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(what + ": «documents» должен быть массивом.");
            }
            List<HostDocument> documents = new List<HostDocument>();
            foreach (JsonElement doc in array.EnumerateArray())
            {
                documents.Add(new HostDocument(Str(doc, "title", what), OptStr(doc, "path"), Bool(doc, "isActive", what), Bool(doc, "isModified", what), Bool(doc, "isFamilyDocument", what)));
            }
            return documents;
        }

        internal static HostDocument ReadOpened(JsonElement result)
        {
            const string what = "ответ на «open»";
            return new HostDocument(Str(result, "title", what), OptStr(result, "path"), true, false, false);
        }

        internal static bool ReadClosed(JsonElement result)
        {
            return Bool(result, "closed", "ответ на «close»");
        }

        internal static HostRunReport ReadRun(JsonElement result)
        {
            const string what = "ответ на «run»";
            JsonElement summary = Prop(result, "summary", what);
            JsonElement array = Prop(result, "results", what);
            if (array.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(what + ": «results» должен быть массивом.");
            }
            // Строка «(discovery)» — обычная строка результата: отдельного разбора не требует.
            List<HostTestResult> results = new List<HostTestResult>();
            foreach (JsonElement r in array.EnumerateArray())
            {
                results.Add(new HostTestResult(Str(r, "class", what), Str(r, "method", what), Str(r, "status", what), OptStr(r, "message"), OptStr(r, "stackTrace"), OptStr(r, "model"), Long(r, "durationMs", what)));
            }
            const string inSummary = what + " (summary)";
            return new HostRunReport((int)Long(summary, "total", inSummary), (int)Long(summary, "passed", inSummary), (int)Long(summary, "failed", inSummary),
                (int)Long(summary, "errored", inSummary), (int)Long(summary, "skipped", inSummary), Long(summary, "durationMs", inSummary), OptStr(summary, "revitVersion"),
                results, ReadWarnings(result, what));
        }

        /// <summary>Необязательный массив строк «warnings»: поля нет — пустой список; поле есть, но не массив строк — ошибка формата.</summary>
        private static IReadOnlyList<string> ReadWarnings(JsonElement result, string what)
        {
            if (!result.TryGetProperty("warnings", out JsonElement array) || array.ValueKind == JsonValueKind.Undefined)
            {
                return new List<string>();
            }
            if (array.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"{what}: поле «warnings» должно быть массивом, получено {array.ValueKind}.");
            }
            List<string> warnings = new List<string>();
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException($"{what}: элементы «warnings» должны быть строками, получено {item.ValueKind}.");
                }
                warnings.Add(item.GetString());
            }
            return warnings;
        }

        private static JsonElement Prop(JsonElement obj, string name, string what)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidDataException($"{what}: нет поля «{name}».");
            }
            return value;
        }

        private static string Str(JsonElement obj, string name, string what)
        {
            JsonElement value = Prop(obj, name, what);
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"{what}: поле «{name}» должно быть строкой, получено {value.ValueKind}.");
            }
            return value.GetString();
        }

        private static string OptStr(JsonElement obj, string name)
        {
            return obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static bool Bool(JsonElement obj, string name, string what)
        {
            JsonElement value = Prop(obj, name, what);
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            {
                throw new InvalidDataException($"{what}: поле «{name}» должно быть true/false, получено {value.ValueKind}.");
            }
            return value.GetBoolean();
        }

        private static long Long(JsonElement obj, string name, string what)
        {
            JsonElement value = Prop(obj, name, what);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long number))
            {
                throw new InvalidDataException($"{what}: поле «{name}» должно быть целым числом, получено {value.ValueKind}.");
            }
            return number;
        }

        private static string Cut(string line)
        {
            return line.Length > 300 ? line.Substring(0, 300) + "…" : line;
        }
    }
}
