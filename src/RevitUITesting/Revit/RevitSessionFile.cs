using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RevitUITesting.Revit
{
    /// <summary>
    /// Файл сеанса %TEMP%\RevitUITesting\session.json: Revit, запущенный RevitUITesting, и имя его канала. По нему следующий прогон
    /// подхватывает Revit (attachToRunning): pid и время старта процесса должны совпасть — чужой Revit с тем же pid не подхватится.
    /// </summary>
    internal sealed class RevitSessionFile
    {
        internal RevitSessionFile(int pid, string pipe, string revitVersion, DateTime startedUtc)
        {
            Pid = pid;
            Pipe = pipe;
            RevitVersion = revitVersion;
            StartedUtc = DateTime.SpecifyKind(startedUtc.ToUniversalTime(), DateTimeKind.Utc);
        }

        internal int Pid { get; }

        internal string Pipe { get; }

        internal string RevitVersion { get; }

        internal DateTime StartedUtc { get; }

        internal static string DefaultPath => Path.Combine(Path.GetTempPath(), "RevitUITesting", "session.json");

        internal static RevitSessionFile Read(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8)))
                {
                    JsonElement root = document.RootElement;
                    int pid = Require(root, "pid", path).GetInt32();
                    string pipe = Require(root, "pipe", path).GetString();
                    string version = Require(root, "revitVersion", path).GetString();
                    DateTime started = DateTime.Parse(Require(root, "startedUtc", path).GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    return new RevitSessionFile(pid, pipe, version, started);
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is InvalidOperationException)
            {
                throw new InvalidDataException($"Файл сеанса {path} повреждён: {ex.Message}. Если Revit, запущенный тестами, уже закрыт, удалите файл.", ex);
            }
        }

        internal void Write(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("pid", Pid);
                    writer.WriteString("pipe", Pipe);
                    writer.WriteString("revitVersion", RevitVersion);
                    writer.WriteString("startedUtc", StartedUtc.ToString("o", CultureInfo.InvariantCulture));
                    writer.WriteEndObject();
                }
                File.WriteAllBytes(path, stream.ToArray());
            }
        }

        internal bool Matches(int pid, DateTime processStartUtc)
        {
            return pid == Pid && Math.Abs((processStartUtc.ToUniversalTime() - StartedUtc).TotalSeconds) < 2;
        }

        internal static void Delete(string path, int pid)
        {
            RevitSessionFile session = Read(path);
            if (session != null && session.Pid == pid)
            {
                File.Delete(path);
            }
        }

        private static JsonElement Require(JsonElement root, string name, string path)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                throw new InvalidDataException($"Файл сеанса {path}: нет поля «{name}».");
            }
            return value;
        }
    }
}
