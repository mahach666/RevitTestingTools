using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace RevitUITesting
{
    /// <summary>
    /// Журнал шагов теста: шаги автора (<see cref="Step"/>), действия компонентов библиотеки («нажата кнопка …»)
    /// и предупреждения. Базовый класс NUnit направляет его в TestContext.Progress и сохраняет в артефакты при падении.
    /// </summary>
    public static class UiLog
    {
        private static readonly object Sync = new object();
        private static readonly List<string> Lines = new List<string>();
        private static readonly LinkedList<string> Tail = new LinkedList<string>();
        private const int TailSize = 200;

        /// <summary>Куда писать строки журнала (по умолчанию — Trace). Базовый класс ставит TestContext.Progress.</summary>
        public static Action<string> Sink { get; set; } = text => Trace.WriteLine(text);

        /// <summary>Шаг сценария, как его понимает автор теста: «Открыть панель Поиск семейств».</summary>
        public static void Step(string text)
        {
            Write("> " + text);
        }

        /// <summary>Предупреждение: всегда пишется и попадает в сводку теста.</summary>
        public static void Warn(string text)
        {
            Write("! " + text);
        }

        /// <summary>Последние строки журнала (для сообщений об ошибках).</summary>
        public static IReadOnlyList<string> Recent(int count = 20)
        {
            lock (Sync)
            {
                return Tail.Skip(Math.Max(0, Tail.Count - count)).ToList();
            }
        }

        /// <summary>Журнал текущего теста целиком (после <see cref="BeginTest"/>).</summary>
        public static string Dump()
        {
            lock (Sync)
            {
                StringBuilder sb = new StringBuilder();
                foreach (string line in Lines)
                {
                    sb.AppendLine(line);
                }
                return sb.ToString();
            }
        }

        /// <summary>Действие page object'а (библиотеки или плагина): «лента MyPlugin: Поиск семейств».</summary>
        public static void Action(string text)
        {
            Write("  " + text);
        }

        /// <summary>Начать журнал нового теста.</summary>
        internal static void BeginTest(string fullName)
        {
            lock (Sync)
            {
                Lines.Clear();
            }
            Write("=== " + fullName);
        }

        private static void Write(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + text;
            lock (Sync)
            {
                Lines.Add(line);
                Tail.AddLast(line);
                if (Tail.Count > TailSize)
                {
                    Tail.RemoveFirst();
                }
            }
            Action<string> sink = Sink;
            sink?.Invoke(line);
        }
    }
}
