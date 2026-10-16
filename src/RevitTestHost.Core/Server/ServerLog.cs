using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Diagnostics;
using RevitTestHost.Core.Protocol;

namespace RevitTestHost.Core.Server
{
    /// <summary>Журнал сервера канала.</summary>
    public interface IServerLog
    {
        void Info(string message);
        void Error(string message, Exception? exception = null);
    }

    /// <summary>
    /// Журнал в файл UTF-8 (без BOM), строка на запись: время, уровень, текст. Потокобезопасен.
    /// Журнал пишут фоновые потоки внутри Revit, где необработанное исключение завершает процесс,
    /// поэтому сбой записи в файл не бросается дальше, а уходит в <see cref="Trace"/> вместе с самой записью.
    /// </summary>
    public sealed class FileServerLog : IServerLog
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly object _lock = new object();

        public FileServerLog(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Не задан путь журнала.", nameof(path));
            Path = path;
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        public string Path { get; }

        public void Info(string message) => Append("INFO", message);

        public void Error(string message, Exception? exception = null) =>
            Append("ERROR", exception == null ? message : message + Environment.NewLine + exception);

        private void Append(string level, string message)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                       " [" + level + "] " + message + Environment.NewLine;
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(Path, line, Utf8NoBom);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Trace.TraceError($"RevitTestHost: не удалось записать журнал {Path}: {ExceptionText.Describe(ex)}. Запись: {line}");
                }
            }
        }
    }
}
