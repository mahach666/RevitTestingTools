using System;
using System.Collections.Generic;
using RevitTestHost.Core.Json;

namespace RevitTestHost.Core.Protocol
{
    /// <summary>Разобранный запрос канала: <c>{"id":..,"cmd":"..","args":{..}}</c>.</summary>
    public sealed class HostRequest
    {
        public HostRequest(long id, string command, IReadOnlyDictionary<string, object?> args)
        {
            Id = id;
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Args = args ?? throw new ArgumentNullException(nameof(args));
        }

        public long Id { get; }
        public string Command { get; }

        /// <summary>Аргументы в виде дерева <see cref="JsonReader"/>; отсутствующие или null <c>args</c> — пустой словарь.</summary>
        public IReadOnlyDictionary<string, object?> Args { get; }
    }

    /// <summary>Ответ канала: успех с <see cref="Result"/> или ошибка с кодом и текстом.</summary>
    public sealed class HostResponse
    {
        private HostResponse(long id, bool ok, IJsonWritable? result, string? errorCode, string? errorMessage)
        {
            Id = id;
            Ok = ok;
            Result = result;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public long Id { get; }
        public bool Ok { get; }
        public IJsonWritable? Result { get; }
        public string? ErrorCode { get; }
        public string? ErrorMessage { get; }

        public static HostResponse Success(long id, IJsonWritable result) =>
            new HostResponse(id, true, result ?? throw new ArgumentNullException(nameof(result)), null, null);

        public static HostResponse Failure(long id, string code, string message) =>
            new HostResponse(id, false, null,
                code ?? throw new ArgumentNullException(nameof(code)),
                message ?? throw new ArgumentNullException(nameof(message)));
    }

    /// <summary>
    /// Ошибка выполнения команды с кодом протокола (<see cref="ErrorCodes"/>). Диспетчер превращает её
    /// в ответ <c>ok:false</c> с этим кодом; прочие исключения становятся <c>REVIT_ERROR</c>.
    /// </summary>
    public sealed class HostCommandException : Exception
    {
        public HostCommandException(string code, string message) : base(message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
        }

        public string Code { get; }
    }
}
