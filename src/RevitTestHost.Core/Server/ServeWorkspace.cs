using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace RevitTestHost.Core.Server
{
    /// <summary>
    /// Временная папка режима сервера: <c>%TEMP%\RevitTestHost\serve\&lt;имя канала&gt;\</c>, где <c>%TEMP%</c> —
    /// временная папка пользователя (<see cref="UserTemp"/>), а не папка сеанса Revit.
    /// Здесь лежат журнал сервера, проекты <c>@new</c> (<c>new-&lt;n&gt;.rvt</c>) и копии моделей <c>copyToTemp</c>.
    /// Файлы не удаляются: Revit может держать их открытыми до конца сеанса.
    /// </summary>
    public sealed class ServeWorkspace
    {
        private static readonly Regex PipeNamePattern = new Regex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant);

        public ServeWorkspace(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("Не задана папка режима сервера.", nameof(rootDirectory));
            RootDirectory = Path.GetFullPath(rootDirectory);
        }

        public string RootDirectory { get; }

        public string LogPath => Path.Combine(RootDirectory, "server.log");

        /// <summary>Журнал ошибок выбора режима и запуска сервера: <c>%TEMP%\RevitTestHost\serve\startup.log</c> (temp пользователя).</summary>
        public static string StartupLogPath => Path.Combine(ServeRoot, "startup.log");

        /// <summary>Папка для канала в temp пользователя.</summary>
        public static ServeWorkspace ForPipe(string pipeName)
        {
            if (!IsValidPipeName(pipeName))
                throw new ArgumentException($"Недопустимое имя канала '{pipeName}': допустимы латинские буквы, цифры, '-' и '_' (до 128 символов).", nameof(pipeName));
            return new ServeWorkspace(Path.Combine(ServeRoot, pipeName));
        }

        private static string ServeRoot => Path.Combine(UserTemp.Directory, "RevitTestHost", "serve");

        /// <summary>Имя канала безопасно и как имя pipe, и как имя папки.</summary>
        public static bool IsValidPipeName(string? pipeName) => pipeName != null && PipeNamePattern.IsMatch(pipeName);

        public void EnsureCreated() => Directory.CreateDirectory(RootDirectory);

        /// <summary>Первый несуществующий <c>new-&lt;n&gt;.rvt</c>, n от 1.</summary>
        public string NextNewProjectPath()
        {
            EnsureCreated();
            for (var n = 1; ; n++)
            {
                var path = Path.Combine(RootDirectory, "new-" + n.ToString(CultureInfo.InvariantCulture) + ".rvt");
                if (!File.Exists(path)) return path;
            }
        }

        /// <summary>
        /// Копирует модель в папку: имя исходного файла, при совпадении — <c>&lt;имя&gt;-&lt;n&gt;&lt;расширение&gt;</c> (n от 2).
        /// Атрибут «только чтение» у копии снимается. Возвращает путь копии.
        /// </summary>
        public string CopyModel(string sourcePath)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException($"Модель не найдена: {sourcePath}", sourcePath);
            EnsureCreated();

            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var ext = Path.GetExtension(sourcePath);
            var target = Path.Combine(RootDirectory, name + ext);
            for (var n = 2; File.Exists(target); n++)
                target = Path.Combine(RootDirectory, name + "-" + n.ToString(CultureInfo.InvariantCulture) + ext);

            File.Copy(sourcePath, target, overwrite: false);
            var attributes = File.GetAttributes(target);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
            return target;
        }
    }
}
