using System.Collections.Generic;
using System.Linq;

namespace RevitUITesting.Revit
{
    /// <summary>Ответ сервера RevitTestHost на «ping» (контракт 1): ответ означает, что поток Revit свободен и исполняет внешние события.</summary>
    public sealed class HostPing
    {
        internal HostPing(string serverVersion, string revitVersion, int pid, bool idle, HostDocument activeDocument)
        {
            ServerVersion = serverVersion;
            RevitVersion = revitVersion;
            Pid = pid;
            Idle = idle;
            ActiveDocument = activeDocument;
        }

        public string ServerVersion { get; }

        public string RevitVersion { get; }

        /// <summary>Id процесса Revit, в котором работает сервер.</summary>
        public int Pid { get; }

        /// <summary>Событие Idling срабатывало после последнего запроса, и других запросов в очереди нет.</summary>
        public bool Idle { get; }

        /// <summary>Активный документ UI; null — документов нет.</summary>
        public HostDocument ActiveDocument { get; }

        public override string ToString()
        {
            return $"Revit {RevitVersion}, pid {Pid}, сервер {ServerVersion}, idle={Idle}, активный документ: {(ActiveDocument == null ? "нет" : ActiveDocument.ToString())}";
        }
    }

    /// <summary>Документ, открытый в Revit.</summary>
    public sealed class HostDocument
    {
        internal HostDocument(string title, string path, bool isActive, bool isModified, bool isFamilyDocument)
        {
            Title = title;
            Path = path ?? "";
            IsActive = isActive;
            IsModified = isModified;
            IsFamilyDocument = isFamilyDocument;
        }

        public string Title { get; }

        /// <summary>Полный путь; "" — документ не сохранён.</summary>
        public string Path { get; }

        public bool IsActive { get; }

        public bool IsModified { get; }

        public bool IsFamilyDocument { get; }

        public override string ToString()
        {
            return $"«{Title}» ({(Path.Length == 0 ? "не сохранён" : Path)}{(IsActive ? ", активный" : "")}{(IsModified ? ", изменён" : "")}{(IsFamilyDocument ? ", семейство" : "")})";
        }
    }

    /// <summary>Результат одного Revit-теста (формат JsonReporter RevitTestHost).</summary>
    public sealed class HostTestResult
    {
        internal HostTestResult(string className, string method, string status, string message, string stackTrace, string model, long durationMs)
        {
            Class = className;
            Method = method;
            Status = status;
            Message = message;
            StackTrace = stackTrace;
            Model = model;
            DurationMs = durationMs;
        }

        public string Class { get; }

        /// <summary>Имя метода; "(discovery)" — ошибка обнаружения тестов в типе, а не тест (строка остаётся обычным результатом).</summary>
        public string Method { get; }

        /// <summary>passed, failed, error или skipped.</summary>
        public string Status { get; }

        public string Message { get; }

        public string StackTrace { get; }

        public string Model { get; }

        public long DurationMs { get; }

        public string FullName => Class + "." + Method;

        /// <summary>Тест упал или завершился ошибкой.</summary>
        public bool IsFailure => Status == "failed" || Status == "error";

        public override string ToString()
        {
            return $"[{Status}] {FullName}" + (string.IsNullOrEmpty(Message) ? "" : ": " + Message);
        }
    }

    /// <summary>Отчёт «run»: сводка и результаты (формат JsonReporter RevitTestHost).</summary>
    public sealed class HostRunReport
    {
        internal HostRunReport(int total, int passed, int failed, int errored, int skipped, long durationMs, string revitVersion, IReadOnlyList<HostTestResult> results, IReadOnlyList<string> warnings)
        {
            Total = total;
            Passed = passed;
            Failed = failed;
            Errored = errored;
            Skipped = skipped;
            DurationMs = durationMs;
            RevitVersion = revitVersion;
            Results = results;
            Warnings = warnings;
        }

        public int Total { get; }

        public int Passed { get; }

        public int Failed { get; }

        public int Errored { get; }

        public int Skipped { get; }

        public long DurationMs { get; }

        public string RevitVersion { get; }

        public IReadOnlyList<HostTestResult> Results { get; }

        /// <summary>Предупреждения сервера (сборки и типы, пропущенные при обнаружении тестов); пусто, если поля «warnings» в ответе нет.</summary>
        public IReadOnlyList<string> Warnings { get; }

        public IReadOnlyList<HostTestResult> Failures => Results.Where(r => r.IsFailure).ToList();

        public override string ToString()
        {
            return $"всего {Total}, прошло {Passed}, упало {Failed}, ошибок {Errored}, пропущено {Skipped}, {DurationMs} мс";
        }
    }
}
