using System;

namespace RevitTestHost.Core.Json
{
    /// <summary>Значение, которое умеет записать себя в <see cref="JsonWriter"/> (ровно одно JSON-значение).</summary>
    public interface IJsonWritable
    {
        void WriteJson(JsonWriter writer);
    }

    /// <summary>Значение JSON, заданное делегатом записи.</summary>
    public sealed class JsonPayload : IJsonWritable
    {
        private readonly Action<JsonWriter> _write;

        public JsonPayload(Action<JsonWriter> write)
        {
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        public void WriteJson(JsonWriter writer) => _write(writer);

        /// <summary>Записывает значение в компактную строку JSON.</summary>
        public static string ToJson(IJsonWritable value, bool indented = false)
        {
            var writer = new JsonWriter(indented);
            value.WriteJson(writer);
            return writer.ToString();
        }
    }
}
