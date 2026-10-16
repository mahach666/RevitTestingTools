using System;
using System.Globalization;
using RevitTestHost.Core.Json;

namespace RevitTestHost.Core.Reporting
{
    /// <summary>
    /// Единственное место, где задан формат JSON-отчёта прогона (<c>summary</c> + <c>results</c>, при наличии — <c>warnings</c>).
    /// Им пользуются и файл отчёта CLI/ленты (<c>JsonReporter</c> надстройки), и результат команды <c>run</c> канала.
    /// </summary>
    public static class JsonReportWriter
    {
        /// <summary>Пишет отчёт как одно JSON-значение (объект) в <paramref name="writer"/>.</summary>
        public static void Write(JsonWriter writer, TestRunReport report)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (report == null) throw new ArgumentNullException(nameof(report));

            writer.BeginObject();

            writer.Name("summary").BeginObject()
                .Property("total", report.Total)
                .Property("passed", report.Passed)
                .Property("failed", report.Failed)
                .Property("errored", report.Errored)
                .Property("skipped", report.Skipped)
                .Property("durationMs", report.DurationMs)
                .Property("revitVersion", report.RevitVersion)
                .Property("host", report.Host)
                .Property("startedAt", report.StartedAt.ToString("o", CultureInfo.InvariantCulture))
                .EndObject();

            writer.Name("results").BeginArray();
            foreach (var r in report.Results)
            {
                writer.BeginObject()
                    .Property("class", r.Class)
                    .Property("method", r.Method);
                if (r.Model != null) writer.Property("model", r.Model);
                writer.Property("status", StatusText(r.Status));
                if (r.Message != null) writer.Property("message", r.Message);
                if (r.StackTrace != null) writer.Property("stackTrace", r.StackTrace);
                writer.Property("durationMs", r.DurationMs)
                    .EndObject();
            }
            writer.EndArray();

            // Необязательное поле: без предупреждений отчёт прежнего формата
            if (report.Warnings.Count > 0)
            {
                writer.Name("warnings").BeginArray();
                foreach (var warning in report.Warnings)
                    writer.String(warning);
                writer.EndArray();
            }

            writer.EndObject();
        }

        /// <summary>Отчёт целиком строкой JSON.</summary>
        public static string ToJson(TestRunReport report, bool indented)
        {
            var writer = new JsonWriter(indented);
            Write(writer, report);
            return writer.ToString();
        }

        /// <summary>Отчёт как значение результата команды канала.</summary>
        public static IJsonWritable AsJson(TestRunReport report) => new JsonPayload(w => Write(w, report));

        /// <summary>Значение поля <c>status</c>: passed / failed / skipped / error.</summary>
        public static string StatusText(TestStatus status) => status.ToString().ToLowerInvariant();
    }
}
