using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using RevitUITesting.Uia;

namespace RevitUITesting.Inspector
{
    /// <summary>
    /// Инструмент автора тестов: дамп дерева UI Automation окон процесса, скриншоты, действия над элементами.
    ///   RevitUITesting.Inspector dump  [processName=Revit] [outDir] [maxDepth=12]
    ///   RevitUITesting.Inspector shot  [processName=Revit] [outDir]
    ///   RevitUITesting.Inspector start "exePath" [outDir] [waitSeconds=60]   — запустить процесс и через waitSeconds сделать дамп
    ///   RevitUITesting.Inspector click processName "id=AutomationId|name=Имя|text=Часть имени" [outDir] [waitMs=3000]
    ///   RevitUITesting.Inspector row   processName "текст строки" [outDir]   — выделить строку списка/дерева
    ///   RevitUITesting.Inspector type  processName "id=AutomationId" "текст" [outDir]
    ///   RevitUITesting.Inspector keys  processName "{ENTER}" [outDir]      — нажать клавиши в активном окне; только если оно принадлежит processName, иначе отмена (код 1)
    /// outDir по умолчанию — %TEMP%\RevitUITesting. Процесс инспектора объявляет себя осведомлённым о DPI (Per-Monitor V2)
    /// до обращения к UI Automation. Коды выхода: 0 — успех, 1 — ошибка, 2 — неверные аргументы.
    /// </summary>
    internal static class Program
    {
        private const string DefaultProcess = "Revit";

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            UiLog.Sink = Console.WriteLine;
            string cmd = args.Length > 0 ? args[0] : "dump";
            if (!new[] { "dump", "shot", "start", "click", "row", "type", "keys" }.Contains(cmd))
            {
                Console.Error.WriteLine("Неизвестная команда «" + cmd + "». Команды: dump, shot, start, click, row, type, keys (см. README).");
                return 2;
            }
            // до первого обращения к UI Automation: иначе на экране больше 100 % координаты UIA и мыши расходятся (клики мимо)
            Console.WriteLine(DpiStatus.EnableForProcess());
            try
            {
                switch (cmd)
                {
                    case "start":
                    {
                        string exe = Required(args, 1, "путь к exe");
                        string outDir = Arg(args, 2, DefaultOut());
                        int waitSeconds = IntArg(args, 3, 60, "секунды ожидания");
                        Process.Start(exe);
                        Thread.Sleep(TimeSpan.FromSeconds(waitSeconds));
                        return Dump(Path.GetFileNameWithoutExtension(exe), outDir, 12, true);
                    }
                    case "shot":
                        return Dump(Arg(args, 1, DefaultProcess), Arg(args, 2, DefaultOut()), 0, true);
                    case "click":
                    {
                        string proc = Required(args, 1, "имя процесса");
                        string selector = Required(args, 2, "селектор");
                        string outDir = Arg(args, 3, DefaultOut());
                        int waitMs = IntArg(args, 4, 3000, "пауза после нажатия, мс");
                        using (UIA3Automation automation = new UIA3Automation())
                        {
                            AutomationElement element = Find(automation, proc, selector);
                            Console.WriteLine("нажатие: " + element.Describe());
                            UiActions.Press(element, selector);
                        }
                        Thread.Sleep(waitMs);
                        return Dump(proc, outDir, 12, true);
                    }
                    case "row":
                    {
                        string proc = Required(args, 1, "имя процесса");
                        string text = Required(args, 2, "текст строки");
                        string outDir = Arg(args, 3, DefaultOut());
                        using (UIA3Automation automation = new UIA3Automation())
                        {
                            AutomationElement row = Enumerable.Reverse(ProcessWindows(automation, proc)).Select(w => w.TryRowContaining(text)).FirstOrDefault(r => r != null)
                                ?? throw new InvalidOperationException("Строка не найдена: " + text);
                            Console.WriteLine("строка: " + row.Describe());
                            UiActions.Select(row, "строка «" + text + "»");
                        }
                        Thread.Sleep(1500);
                        return Dump(proc, outDir, 12, true);
                    }
                    case "type":
                    {
                        string proc = Required(args, 1, "имя процесса");
                        string selector = Required(args, 2, "селектор");
                        string text = Required(args, 3, "текст");
                        string outDir = Arg(args, 4, DefaultOut());
                        using (UIA3Automation automation = new UIA3Automation())
                        {
                            AutomationElement element = Find(automation, proc, selector);
                            if (element.Patterns.Value.IsSupported)
                            {
                                UiActions.SetValue(element, text, selector);
                            }
                            else
                            {
                                UiActions.TypeText(element, text, selector);
                            }
                        }
                        Thread.Sleep(1500);
                        return Dump(proc, outDir, 12, true);
                    }
                    case "keys":
                    {
                        string proc = Required(args, 1, "имя процесса");
                        string keys = Required(args, 2, "клавиши");
                        string outDir = Arg(args, 3, DefaultOut());
                        EnsureForegroundProcess(proc);
                        System.Windows.Forms.SendKeys.SendWait(keys);
                        Thread.Sleep(2000);
                        return Dump(proc, outDir, 12, true);
                    }
                    default:
                        return Dump(Arg(args, 1, DefaultProcess), Arg(args, 2, DefaultOut()), IntArg(args, 3, 12, "глубина дерева"), true);
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static string Arg(string[] args, int index, string fallback)
        {
            return args.Length > index ? args[index] : fallback;
        }

        private static int IntArg(string[] args, int index, int fallback, string what)
        {
            if (args.Length <= index)
            {
                return fallback;
            }
            if (!int.TryParse(args[index], out int value) || value < 0)
            {
                throw new ArgumentException($"Аргумент {index} ({what}) должен быть неотрицательным целым числом, получено «{args[index]}».");
            }
            return value;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>Клавиши уходят в активное окно, поэтому отправка допустима только если оно принадлежит процессу processName.</summary>
        private static void EnsureForegroundProcess(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
            {
                throw new InvalidOperationException("Процесс не найден: " + processName);
            }
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundPid);
            if (!processes.Any(p => p.Id == foregroundPid))
            {
                throw new InvalidOperationException($"Отмена: активно окно другого процесса (pid {foregroundPid}), а не «{processName}». Клавиши не отправлены.");
            }
        }

        private static string Required(string[] args, int index, string what)
        {
            if (args.Length <= index || string.IsNullOrWhiteSpace(args[index]))
            {
                throw new ArgumentException($"Не задан аргумент {index}: {what}.");
            }
            return args[index];
        }

        private static string DefaultOut()
        {
            return Path.Combine(Path.GetTempPath(), "RevitUITesting");
        }

        private static AutomationElement[] ProcessWindows(UIA3Automation automation, string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
            {
                throw new InvalidOperationException("Процесс не найден: " + processName);
            }
            AutomationElement desktop = automation.GetDesktop();
            return processes
                .SelectMany(p => desktop.FindAllChildren(cf => cf.ByProcessId(p.Id)))
                .ToArray();
        }

        private static AutomationElement Find(UIA3Automation automation, string processName, string selector)
        {
            int eq = selector.IndexOf('=');
            if (eq <= 0)
            {
                throw new ArgumentException("Селектор: id=..., name=... или text=...");
            }
            string kind = selector.Substring(0, eq).Trim();
            string value = selector.Substring(eq + 1);
            By by;
            switch (kind)
            {
                case "id":
                    by = By.Id(value);
                    break;
                case "name":
                    by = By.Name(value);
                    break;
                case "text":
                    by = By.Where(f => f.Name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0, "имя содержит «" + value + "»");
                    break;
                default:
                    throw new ArgumentException("Селектор: id=..., name=... или text=...");
            }
            foreach (AutomationElement window in Enumerable.Reverse(ProcessWindows(automation, processName)))
            {
                AutomationElement found = window.TryFind(by);
                if (found != null)
                {
                    return found;
                }
            }
            throw new InvalidOperationException("Элемент не найден: " + selector);
        }

        private static int Dump(string processName, string outDir, int maxDepth, bool screenshot)
        {
            Directory.CreateDirectory(outDir);
            using (UIA3Automation automation = new UIA3Automation())
            {
                AutomationElement[] windows = ProcessWindows(automation, processName);
                Console.WriteLine($"Процесс {processName}, окон верхнего уровня: {windows.Length}");
                int index = 0;
                foreach (AutomationElement window in windows)
                {
                    index++;
                    string stamp = DateTime.Now.ToString("HHmmss");
                    string baseName = $"{stamp}_{index}_{Sanitize(window.SafeName())}";
                    if (maxDepth > 0)
                    {
                        // подробный дамп: HelpText, ItemStatus, ItemType, паттерны; SelectionPattern.Selection не читается
                        string treePath = Path.Combine(outDir, baseName + ".txt");
                        File.WriteAllText(treePath, window.DumpTree(maxDepth, true), Encoding.UTF8);
                        Console.WriteLine("дерево: " + treePath);
                    }
                    if (screenshot)
                    {
                        string shotPath = Path.Combine(outDir, baseName + ".png");
                        try
                        {
                            window.CaptureToFile(shotPath);
                            Console.WriteLine("скриншот: " + shotPath);
                        }
                        catch (Exception ex)
                        {
                            // скриншот — необязательная часть дампа: окно могло закрыться или быть свёрнутым; сообщаем и продолжаем
                            Console.Error.WriteLine("скриншот не снят (" + window.Describe() + "): " + ex.GetType().Name + ": " + ex.Message);
                        }
                    }
                }
            }
            return 0;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return "window";
            }
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }
    }
}
