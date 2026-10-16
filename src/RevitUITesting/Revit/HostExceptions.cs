using System;
using RevitUITesting.Uia;

namespace RevitUITesting.Revit
{
    /// <summary>Коды ошибок сервера RevitTestHost (контракт 1).</summary>
    public static class HostErrorCodes
    {
        public const string BadRequest = "BAD_REQUEST";

        public const string UnknownCommand = "UNKNOWN_COMMAND";

        public const string NotFound = "NOT_FOUND";

        /// <summary>Активный документ Revit API не закрывает — его закрывает UI-слой (RevitApp.CloseActiveDocument).</summary>
        public const string ActiveDocument = "ACTIVE_DOCUMENT";

        /// <summary>Исключение Revit API или отказ ExternalEvent.Raise; текст — в сообщении.</summary>
        public const string RevitError = "REVIT_ERROR";
    }

    /// <summary>Сервер RevitTestHost ответил ошибкой (ok = false).</summary>
    public sealed class HostErrorException : InvalidOperationException
    {
        public HostErrorException(string command, string code, string message)
            : base($"RevitTestHost отказал в «{command}»: {code}: {message}")
        {
            Command = command;
            Code = code;
            HostMessage = message;
        }

        public string Command { get; }

        /// <summary>Код из <see cref="HostErrorCodes"/>.</summary>
        public string Code { get; }

        /// <summary>Текст ошибки сервера.</summary>
        public string HostMessage { get; }
    }

    /// <summary>
    /// Сервер RevitTestHost не ответил за таймаут: Revit занят — показывает модальное окно (ExternalEvent тогда не исполняется) или
    /// выполняет долгую операцию. Это не «элемента нет»: в тексте — открытые окна Revit, найденные через UI Automation.
    /// </summary>
    public sealed class RevitBusyException : AppBusyException
    {
        public RevitBusyException(string command, TimeSpan timeout, TimeSpan elapsed, string openWindows)
            : base("ответ RevitTestHost на «" + command + "»", timeout, elapsed, null,
                "Revit занят: пока открыто модальное окно или идёт долгая операция, ExternalEvent не исполняется и сервер не отвечает. " +
                (string.IsNullOrEmpty(openWindows) ? "Окна Revit не перечислены." : openWindows))
        {
            Command = command;
            OpenWindows = openWindows ?? "";
        }

        public string Command { get; }

        /// <summary>Окна Revit в момент отказа (одним проходом UI Automation).</summary>
        public string OpenWindows { get; }
    }

    /// <summary>Процесс Revit завершился (упал или закрыт). Сеанс помечается мёртвым: следующие классы тестов запустят Revit заново.</summary>
    public sealed class RevitExitedException : InvalidOperationException
    {
        public RevitExitedException(int processId, int? exitCode, string journalTail)
            : base($"Revit (pid {processId}) завершился{(exitCode == null ? " (код выхода неизвестен)" : " с кодом " + exitCode)}. " +
                   "Следующие классы тестов запустят Revit заново." +
                   (string.IsNullOrEmpty(journalTail) ? "" : Environment.NewLine + "Хвост журнала Revit:" + Environment.NewLine + journalTail))
        {
            ProcessId = processId;
            ExitCode = exitCode;
            JournalTail = journalTail ?? "";
        }

        public int ProcessId { get; }

        public int? ExitCode { get; }

        public string JournalTail { get; }
    }
}
