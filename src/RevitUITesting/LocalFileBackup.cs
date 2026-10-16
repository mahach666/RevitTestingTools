using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitUITesting
{
    /// <summary>
    /// Снимок локальных файлов (настройки плагинов у пользователя) и восстановление с побайтной проверкой.
    /// «Файла не было» тоже запоминается: при восстановлении такой файл удаляется.
    /// </summary>
    public sealed class LocalFileBackup
    {
        private readonly Dictionary<string, byte[]> _content;

        private LocalFileBackup(Dictionary<string, byte[]> content)
        {
            _content = content;
        }

        public IReadOnlyList<string> Paths => _content.Keys.ToList();

        public static LocalFileBackup Take(params string[] paths)
        {
            Dictionary<string, byte[]> content = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                string full = Path.GetFullPath(path);
                content[full] = File.Exists(full) ? File.ReadAllBytes(full) : null;
            }
            return new LocalFileBackup(content);
        }

        /// <summary>
        /// Вернуть файлы к снимку и сверить побайтно. Сбой одного файла (занят другим процессом, нет прав) не прерывает остальные:
        /// возвращаются все файлы, затем одно <see cref="IOException"/> со списком всех проблем.
        /// </summary>
        public void Restore()
        {
            List<string> problems = new List<string>();
            Exception first = null;
            foreach (KeyValuePair<string, byte[]> pair in _content)
            {
                try
                {
                    string problem = RestoreOne(pair.Key, pair.Value);
                    if (problem != null)
                    {
                        problems.Add(problem);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    problems.Add(pair.Key + ": не удалось восстановить — " + ex.Message);
                    first = first ?? ex;
                }
            }
            if (problems.Count > 0)
            {
                throw new IOException("Локальные файлы не восстановлены: " + string.Join("; ", problems), first);
            }
        }

        /// <summary>Вернуть один файл (или удалить, если его не было); null — готово, иначе описание расхождения.</summary>
        private static string RestoreOne(string path, byte[] content)
        {
            if (content == null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return File.Exists(path) ? path + ": не удалён" : null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
            return File.ReadAllBytes(path).SequenceEqual(content) ? null : path + ": содержимое после восстановления отличается";
        }
    }
}
