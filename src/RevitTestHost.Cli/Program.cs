using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace RevitTestHost.Cli;

/// <summary>
/// Запускает Revit в headless-режиме RevitTestHost, ждёт завершения прогона,
/// парсит JSON-отчёт и возвращает exit code = числу упавших тестов.
///
/// Использование:
///   RevitTestHost.Cli --revit-version 2022 [--filter "*XYZ*;Ns.Class.*"] [--output path.json] [--timeout-minutes 45]
///   RevitTestHost.Cli --revit "C:\Program Files\Autodesk\Revit 2022\Revit.exe" ...
///
/// Exit codes: 0 — все прошли; N — число failed+errored; 101 — инфраструктурная ошибка
/// (Revit не найден, таймаут, нет отчёта); 102 — фатальная ошибка внутри прогона.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string? revitPath = null;
        string revitVersion = "2022";
        string? filter = null;
        string? output = null;
        int timeoutMinutes = 45;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--revit": revitPath = Next(args, ref i); break;
                case "--revit-version": revitVersion = Next(args, ref i); break;
                case "--filter": filter = Next(args, ref i); break;
                case "--output": output = Next(args, ref i); break;
                case "--timeout-minutes": timeoutMinutes = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                case "--help" or "-h" or "/?":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"Неизвестный аргумент: {args[i]}");
                    PrintUsage();
                    return 101;
            }
        }

        revitPath ??= $@"C:\Program Files\Autodesk\Revit {revitVersion}\Revit.exe";
        if (!File.Exists(revitPath))
        {
            Console.Error.WriteLine($"Revit не найден: {revitPath}");
            return 101;
        }

        // Revit запускается в другой рабочей папке: относительный путь отчёта делаем абсолютным здесь
        output = Path.GetFullPath(output ?? Path.Combine(Path.GetTempPath(), "RevitTestHost",
            $"cli_results_{DateTime.Now:yyyyMMdd_HHmmss}.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output)) File.Delete(output);

        Console.WriteLine($"Revit:   {revitPath}");
        Console.WriteLine($"Фильтр:  {filter ?? "(все тесты)"}");
        Console.WriteLine($"Отчёт:   {output}");
        Console.WriteLine($"Таймаут: {timeoutMinutes} мин");
        Console.WriteLine();

        var psi = new ProcessStartInfo
        {
            FileName = revitPath,
            Arguments = "/language ENU",
            UseShellExecute = false,
        };
        psi.Environment["REVITTESTHOST_CLI"] = "1";
        psi.Environment["REVITTESTHOST_OUTPUT"] = output;
        if (!string.IsNullOrWhiteSpace(filter))
            psi.Environment["REVITTESTHOST_FILTER"] = filter;

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(psi);
        if (process == null)
        {
            Console.Error.WriteLine("Не удалось запустить Revit.");
            return 101;
        }

        Console.WriteLine($"Revit запущен (PID {process.Id}), ждём завершения прогона...");
        if (!process.WaitForExit((int)TimeSpan.FromMinutes(timeoutMinutes).TotalMilliseconds))
        {
            Console.Error.WriteLine($"Таймаут {timeoutMinutes} мин — убиваем Revit.");
            try { process.Kill(entireProcessTree: true); } catch { }
            return 101;
        }
        Console.WriteLine($"Revit завершился за {sw.Elapsed:mm\\:ss}.");

        if (!File.Exists(output))
        {
            Console.Error.WriteLine(
                "Отчёт не появился. Возможные причины: аддин TestHost не установлен для этой версии Revit, " +
                "Revit показал модальный диалог (лицензия/вход), краш при старте. " +
                $@"Проверьте журнал: %LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit {revitVersion}\Journals\");
            return 101;
        }

        return PrintReport(output);
    }

    private static int PrintReport(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var fatal))
        {
            Console.Error.WriteLine("Фатальная ошибка прогона внутри Revit:");
            Console.Error.WriteLine(fatal.GetString());
            return 102;
        }

        var summary = root.GetProperty("summary");
        int passed = summary.GetProperty("passed").GetInt32();
        int failed = summary.GetProperty("failed").GetInt32();
        int errored = summary.GetProperty("errored").GetInt32();
        int skipped = summary.GetProperty("skipped").GetInt32();
        int total = summary.GetProperty("total").GetInt32();
        long durationMs = summary.GetProperty("durationMs").GetInt64();

        // Пропущенные при обнаружении сборки и типы (необязательное поле отчёта)
        if (root.TryGetProperty("warnings", out var warnings) && warnings.GetArrayLength() > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Предупреждения обнаружения тестов:");
            foreach (var w in warnings.EnumerateArray())
                Console.WriteLine($"  {w.GetString()}");
        }

        Console.WriteLine();
        Console.WriteLine($"Итого: {total} | passed {passed} | failed {failed} | errored {errored} | skipped {skipped} | {durationMs} ms");

        if (failed + errored > 0 && root.TryGetProperty("results", out var results))
        {
            Console.WriteLine();
            Console.WriteLine("Упавшие:");
            foreach (var r in results.EnumerateArray())
            {
                var status = r.GetProperty("status").GetString();
                if (status is not ("failed" or "error")) continue;
                var cls = r.GetProperty("class").GetString();
                var method = r.GetProperty("method").GetString();
                var message = r.TryGetProperty("message", out var m) ? m.GetString() : null;
                Console.WriteLine($"  [{status}] {cls}.{method}");
                if (!string.IsNullOrEmpty(message))
                    Console.WriteLine($"           {message}");
            }
        }

        return failed + errored;
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Аргумент {args[i]} требует значение");
        return args[++i];
    }

    private static void PrintUsage()
    {
        Console.WriteLine("RevitTestHost.Cli — запуск Revit-тестов без ручного открытия Revit.");
        Console.WriteLine();
        Console.WriteLine("  --revit-version 2022      версия установленного Revit (по умолчанию 2022)");
        Console.WriteLine("  --revit <path>            явный путь к Revit.exe (перекрывает --revit-version)");
        Console.WriteLine("  --filter \"a;b\"            шаблоны тестов через ';' с '*', матч по Namespace.Class.Method");
        Console.WriteLine("  --output <path.json>      путь JSON-отчёта (по умолчанию %TEMP%\\RevitTestHost\\cli_results_*.json)");
        Console.WriteLine("  --timeout-minutes 45      таймаут ожидания Revit");
    }
}
