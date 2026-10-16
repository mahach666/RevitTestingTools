using System;
using System.IO;
using System.Linq;
using System.Text;

namespace RevitUITesting.Revit
{
    /// <summary>Журналы Revit (%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit &lt;версия&gt;\Journals\journal.NNNN.txt, кодировка ANSI) — хвост для артефактов и ошибок.</summary>
    internal static class RevitJournal
    {
        internal static string DirectoryFor(string revitVersion)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autodesk", "Revit", "Autodesk Revit " + revitVersion, "Journals");
        }

        internal static string Latest(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }
            return new DirectoryInfo(directory).GetFiles("journal.*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }

        /// <summary>Последние lines строк самого свежего журнала (файл открывается с FileShare.ReadWrite: Revit продолжает писать).</summary>
        internal static string Tail(string directory, int lines)
        {
            if (!Directory.Exists(directory))
            {
                return "Папки журналов Revit нет: " + directory;
            }
            string latest = Latest(directory);
            if (latest == null)
            {
                return "Журналов Revit нет в " + directory;
            }
            string text;
            using (FileStream stream = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.Default, true))
            {
                text = reader.ReadToEnd();
            }
            string[] all = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            return latest + Environment.NewLine + string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Length - lines)));
        }
    }
}
