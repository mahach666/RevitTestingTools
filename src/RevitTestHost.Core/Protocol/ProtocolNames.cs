namespace RevitTestHost.Core.Protocol
{
    /// <summary>Коды ошибок протокола канала (контракт 1).</summary>
    public static class ErrorCodes
    {
        public const string BadRequest = "BAD_REQUEST";
        public const string UnknownCommand = "UNKNOWN_COMMAND";
        public const string NotFound = "NOT_FOUND";
        public const string ActiveDocument = "ACTIVE_DOCUMENT";
        public const string RevitError = "REVIT_ERROR";
    }

    /// <summary>Команды протокола канала (контракт 1). Имена сравниваются с учётом регистра.</summary>
    public static class Commands
    {
        public const string Ping = "ping";
        public const string Documents = "documents";
        public const string Open = "open";
        public const string Close = "close";
        public const string Run = "run";
    }

    /// <summary>Версия протокола, которую сервер сообщает в <c>ping.serverVersion</c>.</summary>
    public static class ServerInfo
    {
        public const string ProtocolVersion = "1.0";
    }
}
