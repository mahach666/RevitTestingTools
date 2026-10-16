using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RevitUITesting
{
    /// <summary>Строка журнала плагина.</summary>
    public sealed class LogLine
    {
        internal LogLine(string log, string text, string level)
        {
            Log = log;
            Text = text;
            Level = level;
        }

        /// <summary>Имя журнала (файл без .log).</summary>
        public string Log { get; }

        public string Text { get; }

        /// <summary>Уровень из строки «дата|[поток]|LEVEL|…» (log4net, PatternLayout %-5level); "" — строка в другом формате (продолжение сообщения).</summary>
        public string Level { get; }

        public override string ToString()
        {
            return Log + ": " + Text;
        }
    }

    /// <summary>
    /// Строки журналов, дописанные после <see cref="Mark"/>. Файлы читаются с FileShare.ReadWrite|Delete (плагин продолжает писать),
    /// незавершённая последняя строка не отдаётся; файл стал короче метки (ротация) — читается с начала с предупреждением.
    /// </summary>
    public sealed class LogWatch
    {
        private static readonly Regex LevelPattern = new Regex(@"^[^|]*\|\[[^\]]*\]\|(?<level>[A-Z]+)\s*\|", RegexOptions.CultureInvariant);
        private static readonly Regex VersionPattern = new Regex(@"Plugin version:\s*(?<version>\S+)", RegexOptions.CultureInvariant);
        private readonly string _directory;
        private readonly string[] _names;
        private readonly string _extension;
        private readonly Regex _levelPattern;
        private readonly Dictionary<string, long> _marks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        public LogWatch(string logsDirectory, IEnumerable<string> logNames)
            : this(logsDirectory, logNames, ".log", LevelPattern)
        {
        }

        /// <summary>Журналы &lt;папка&gt;\&lt;имя&gt;&lt;extension&gt;; уровень строки — группа «level» шаблона levelPattern (например, журналы *.txt со строками вида «… [INFO] …»).</summary>
        public LogWatch(string logsDirectory, IEnumerable<string> logNames, string extension, Regex levelPattern)
        {
            _directory = logsDirectory ?? throw new ArgumentNullException(nameof(logsDirectory));
            _names = (logNames ?? new string[0]).ToArray();
            if (string.IsNullOrEmpty(extension) || !extension.StartsWith(".", StringComparison.Ordinal))
            {
                throw new ArgumentException("Расширение журнала начинается с точки: «.log», «.txt».", nameof(extension));
            }
            _extension = extension;
            _levelPattern = levelPattern ?? throw new ArgumentNullException(nameof(levelPattern));
            if (!_levelPattern.GetGroupNames().Contains("level"))
            {
                throw new ArgumentException("В шаблоне уровня нужна именованная группа (?<level>…).", nameof(levelPattern));
            }
        }

        /// <summary>Запомнить текущий конец каждого журнала.</summary>
        public void Mark()
        {
            foreach (string name in _names)
            {
                string path = PathOf(name);
                _marks[name] = File.Exists(path) ? new FileInfo(path).Length : 0;
            }
        }

        /// <summary>Строки, дописанные после <see cref="Mark"/>.</summary>
        public IReadOnlyList<LogLine> NewLines()
        {
            List<LogLine> lines = new List<LogLine>();
            foreach (string name in _names)
            {
                string path = PathOf(name);
                if (!File.Exists(path))
                {
                    continue;
                }
                long mark = _marks.TryGetValue(name, out long value) ? value : 0;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length < mark)
                    {
                        UiLog.Warn($"Журнал {name} стал короче метки (ротация?) — читается с начала.");
                        // метка сбрасывается, иначе предупреждение повторялось бы при каждом опросе до конца ожидания
                        mark = 0;
                        _marks[name] = 0;
                    }
                    stream.Seek(mark, SeekOrigin.Begin);
                    byte[] buffer = new byte[stream.Length - mark];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = stream.Read(buffer, read, buffer.Length - read);
                        if (n == 0)
                        {
                            break;
                        }
                        read += n;
                    }
                    string text = Encoding.UTF8.GetString(buffer, 0, read);
                    int lastNewLine = text.LastIndexOf('\n');
                    if (lastNewLine < 0)
                    {
                        continue;
                    }
                    foreach (string line in text.Substring(0, lastNewLine).Split('\n'))
                    {
                        string trimmed = line.TrimEnd('\r');
                        if (trimmed.Length == 0)
                        {
                            continue;
                        }
                        Match match = _levelPattern.Match(trimmed);
                        lines.Add(new LogLine(name, trimmed, match.Success ? match.Groups["level"].Value : ""));
                    }
                }
            }
            return lines;
        }

        /// <summary>
        /// Дождаться строки журналов (после <see cref="Mark"/>), подходящей под условие; нет за timeout — <see cref="Uia.UiWaitTimeoutException"/>
        /// с последними прочитанными строками.
        /// </summary>
        public LogLine WaitLine(Func<LogLine, bool> match, TimeSpan timeout, string description)
        {
            if (match == null)
            {
                throw new ArgumentNullException(nameof(match));
            }
            IReadOnlyList<LogLine> last = new LogLine[0];
            LogLine found = Uia.Wait.ForStrict(() =>
            {
                last = NewLines();
                return last.FirstOrDefault(match);
            }, timeout, "строка журнала: " + description,
                () => last.Count == 0 ? "новых строк нет" : "последние строки: " + string.Join(" | ", last.Skip(Math.Max(0, last.Count - 5)).Select(l => l.Text)));
            return found;
        }

        /// <summary>Последняя версия плагина из строки «Plugin version: X» (если плагин пишет её при загрузке); null — строки нет.</summary>
        public string LastPluginVersion(string logName)
        {
            string path = PathOf(logName);
            if (!File.Exists(path))
            {
                return null;
            }
            string text;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                text = reader.ReadToEnd();
            }
            Match last = VersionPattern.Matches(text).Cast<Match>().LastOrDefault();
            return last?.Groups["version"].Value;
        }

        private string PathOf(string name)
        {
            return Path.Combine(_directory, name + _extension);
        }
    }
}
