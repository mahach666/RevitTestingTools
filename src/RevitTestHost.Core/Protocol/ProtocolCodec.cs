using System;
using System.Collections.Generic;
using RevitTestHost.Core.Json;

namespace RevitTestHost.Core.Protocol
{
    /// <summary>
    /// Преобразование строк канала: строка UTF-8 JSON без перевода строки ↔ <see cref="HostRequest"/>/<see cref="HostResponse"/>.
    /// </summary>
    public static class ProtocolCodec
    {
        /// <summary>Id ответа на запрос, из которого id извлечь не удалось (клиенты нумеруют запросы с 1).</summary>
        public const long UnknownRequestId = 0;

        private static readonly IReadOnlyDictionary<string, object?> NoArgs =
            new Dictionary<string, object?>(StringComparer.Ordinal);

        /// <summary>
        /// Разбирает строку запроса. При ошибке возвращает false и готовый ответ <c>BAD_REQUEST</c>:
        /// с id запроса, если он прочитан, иначе с <see cref="UnknownRequestId"/>. Имя команды не проверяется —
        /// это делает диспетчер (<c>UNKNOWN_COMMAND</c>).
        /// </summary>
        public static bool TryParseRequest(string line, out HostRequest? request, out HostResponse? error)
        {
            request = null;
            error = null;

            object? root;
            try
            {
                root = JsonReader.Parse(line ?? throw new ArgumentNullException(nameof(line)));
            }
            catch (JsonFormatException ex)
            {
                error = BadRequest(UnknownRequestId, ex.Message);
                return false;
            }

            if (!(root is Dictionary<string, object?> obj))
            {
                error = BadRequest(UnknownRequestId, "запрос должен быть JSON-объектом {\"id\":..,\"cmd\":..,\"args\":{..}}");
                return false;
            }

            if (!obj.TryGetValue("id", out var idValue) || !(idValue is long id))
            {
                error = BadRequest(UnknownRequestId, "поле 'id' обязательно и должно быть целым числом");
                return false;
            }

            if (!obj.TryGetValue("cmd", out var cmdValue) || !(cmdValue is string cmd) || cmd.Length == 0)
            {
                error = BadRequest(id, "поле 'cmd' обязательно и должно быть непустой строкой");
                return false;
            }

            IReadOnlyDictionary<string, object?> args;
            if (!obj.TryGetValue("args", out var argsValue) || argsValue == null)
            {
                args = NoArgs;
            }
            else if (argsValue is Dictionary<string, object?> argsObject)
            {
                args = argsObject;
            }
            else
            {
                error = BadRequest(id, "поле 'args' должно быть JSON-объектом");
                return false;
            }

            request = new HostRequest(id, cmd, args);
            return true;
        }

        /// <summary>Строка ответа без завершающего перевода строки (его добавляет транспорт).</summary>
        public static string FormatResponse(HostResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            var w = new JsonWriter(indented: false);
            w.BeginObject()
                .Property("id", response.Id)
                .Property("ok", response.Ok);
            if (response.Ok)
            {
                w.Name("result");
                response.Result!.WriteJson(w);
            }
            else
            {
                w.Name("error").BeginObject()
                    .Property("code", response.ErrorCode)
                    .Property("message", response.ErrorMessage)
                    .EndObject();
            }
            w.EndObject();
            return w.ToString();
        }

        private static HostResponse BadRequest(long id, string message) =>
            HostResponse.Failure(id, ErrorCodes.BadRequest, message);
    }
}
