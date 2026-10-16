using System.Globalization;
using System.IO;
using System.Text;
using RevitTestHost.Core.Reporting;
using RevitTestHost.Core.Server;

namespace RevitTestHost.Addin.Reporting
{
    /// <summary>
    /// Пишет JSON-отчёт в файл. Формат задаёт <see cref="JsonReportWriter"/> (RevitTestHost.Core) —
    /// тот же объект возвращает команда <c>run</c> режима сервера. Сторонних сериализаторов нет:
    /// надстройка не должна тянуть зависимости в процесс Revit (конфликты версий с тестируемыми плагинами).
    /// Папка — <c>RevitTestHost</c> во временной папке пользователя (<see cref="UserTemp"/>), а не в папке сеанса Revit.
    /// </summary>
    public static class JsonReporter
    {
        public static string Write(TestRunReport report)
        {
            var dir = Path.Combine(UserTemp.Directory, "RevitTestHost");
            Directory.CreateDirectory(dir);
            var timestamp = report.StartedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(dir, $"results_{timestamp}.json");

            File.WriteAllText(path, JsonReportWriter.ToJson(report, indented: true), new UTF8Encoding(false));
            return path;
        }
    }
}
