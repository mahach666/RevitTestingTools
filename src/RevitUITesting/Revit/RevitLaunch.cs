using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace RevitUITesting.Revit
{
    /// <summary>Запущенный Revit этой сессии Windows: pid и время старта (защита от переиспользованного pid).</summary>
    internal sealed class RunningRevit
    {
        internal RunningRevit(int pid, DateTime startUtc)
        {
            Pid = pid;
            StartUtc = startUtc;
        }

        internal int Pid { get; }

        internal DateTime StartUtc { get; }
    }

    /// <summary>Подхватить ли запущенный Revit: только тот, что запустил RevitUITesting (session.json) в режиме сервера.</summary>
    internal static class AttachDecision
    {
        internal static int? Choose(IReadOnlyList<RunningRevit> running, RevitSessionFile session, string sessionPath, string revitVersion, bool attachToRunning)
        {
            if (running.Count == 0)
            {
                return null;
            }
            string pids = string.Join(", ", running.Select(r => r.Pid));
            if (!attachToRunning)
            {
                throw new InvalidOperationException($"Revit уже запущен (pid {pids}), а attachToRunning = false. Закройте Revit вручную или включите attachToRunning " +
                                                    "(подхват работает только для Revit, запущенного RevitUITesting в режиме сервера). Тесты чужой Revit не закрывают.");
            }
            if (session == null)
            {
                throw new InvalidOperationException($"Revit уже запущен (pid {pids}), но файла сеанса {sessionPath} нет: этот Revit запущен не RevitUITesting " +
                                                    "(режима сервера RevitTestHost в нём нет) — подхват невозможен. Закройте Revit вручную.");
            }
            RunningRevit match = running.FirstOrDefault(r => session.Matches(r.Pid, r.StartUtc));
            if (match == null)
            {
                throw new InvalidOperationException($"Файл сеанса {sessionPath} описывает Revit pid {session.Pid}, запущенный {session.StartedUtc:o}, а запущены pid {pids}: " +
                                                    "запущенный Revit не запущен RevitUITesting — подхват невозможен. Закройте Revit вручную.");
            }
            if (!string.Equals(session.RevitVersion, revitVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Подхватываемый Revit pid {match.Pid} — версии {session.RevitVersion}, а в конфигурации revitVersion = {revitVersion}.");
            }
            return match.Pid;
        }
    }

    internal static class RevitLaunch
    {
        /// <summary>
        /// Revit.exe [/language XXX] с REVITTESTHOST_SERVE=&lt;канал&gt;. Унаследованные переменные CLI-режима RevitTestHost удаляются:
        /// при REVITTESTHOST_CLI=1 надстройка выбрала бы CLI-режим, прогнала тесты и завершила Revit.
        /// </summary>
        internal static ProcessStartInfo StartInfo(string exe, string language, string pipeName)
        {
            if (string.IsNullOrEmpty(pipeName))
            {
                throw new ArgumentException("Нужно имя канала", nameof(pipeName));
            }
            string arguments = "";
            if (!string.IsNullOrWhiteSpace(language))
            {
                if (language.Length != 3 || !language.All(char.IsLetter))
                {
                    throw new ArgumentException($"language: ожидается трёхбуквенный код языка Revit (ENU, RUS, DEU…), получено «{language}».", nameof(language));
                }
                arguments = "/language " + language.ToUpperInvariant();
            }
            ProcessStartInfo info = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe),
            };
            info.Environment.Remove(RevitApp.CliVariable);
            info.Environment.Remove("REVITTESTHOST_FILTER");
            info.Environment.Remove("REVITTESTHOST_OUTPUT");
            info.Environment[RevitApp.ServeVariable] = pipeName;
            return info;
        }

        /// <summary>Командная строка CreateProcess: путь к exe в кавычках и аргументы ProcessStartInfo.</summary>
        internal static string CommandLine(ProcessStartInfo info)
        {
            if (string.IsNullOrEmpty(info.FileName) || info.FileName.IndexOf('"') >= 0)
            {
                throw new ArgumentException($"Путь к exe пуст или содержит кавычку: «{info.FileName}».", nameof(info));
            }
            string quoted = "\"" + info.FileName + "\"";
            return string.IsNullOrEmpty(info.Arguments) ? quoted : quoted + " " + info.Arguments;
        }

        /// <summary>
        /// Блок окружения для CreateProcess с CREATE_UNICODE_ENVIRONMENT: «имя=значение\0» по возрастанию имён без учёта регистра
        /// (порядок, которого требует Windows), в конце ещё один «\0»; пустое окружение — «\0\0». Значение null пишется пустым (как в Process.Start).
        /// Имя пустое или с «=» или «\0», значение с «\0», имена, отличающиеся только регистром, — ArgumentException.
        /// </summary>
        internal static string EnvironmentBlock(IDictionary<string, string> environment)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }
            List<string> names = environment.Keys.ToList();
            names.Sort(StringComparer.OrdinalIgnoreCase);
            StringBuilder block = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                string value = environment[name] ?? "";
                if (name.Length == 0 || name.IndexOf('=') >= 0 || name.IndexOf('\0') >= 0)
                {
                    throw new ArgumentException($"Имя переменной окружения «{name}» пустое или содержит «=» или NUL — блок окружения из него не построить.", nameof(environment));
                }
                if (value.IndexOf('\0') >= 0)
                {
                    throw new ArgumentException($"Значение переменной окружения {name} содержит NUL — блок окружения из него не построить.", nameof(environment));
                }
                if (i > 0 && string.Equals(names[i - 1], name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Переменные окружения {names[i - 1]} и {name} отличаются только регистром — в Windows это одна переменная.", nameof(environment));
                }
                block.Append(name).Append('=').Append(value).Append('\0');
            }
            if (names.Count == 0)
            {
                block.Append('\0');
            }
            return block.Append('\0').ToString();
        }

        /// <summary>
        /// Запустить процесс по ProcessStartInfo (FileName, Arguments, WorkingDirectory, Environment, CreateNoWindow) через CreateProcessW
        /// с bInheritHandles = FALSE. Process.Start в .NET Framework всегда передаёт TRUE, и Revit получал копии наследуемых дескрипторов
        /// процесса тестов — в том числе каналов stdout/stderr вызывающего: «dotnet test» с перенаправленным выводом не завершался, пока жив
        /// Revit с keepRevitOpen (задача 10). Стандартные потоки потомку не передаются (STARTF_USESTDHANDLES не ставится).
        /// Сбой CreateProcessW или CloseHandle — Win32Exception с командной строкой.
        /// </summary>
        internal static Process Start(ProcessStartInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }
            string commandLine = CommandLine(info);
            string block = EnvironmentBlock(info.Environment);
            uint flags = CreateUnicodeEnvironment | (info.CreateNoWindow ? CreateNoWindow : 0);
            string directory = string.IsNullOrEmpty(info.WorkingDirectory) ? null : info.WorkingDirectory;
            StartupInfo startup = new StartupInfo { cb = Marshal.SizeOf(typeof(StartupInfo)) };
            IntPtr environment = Marshal.StringToHGlobalUni(block);
            try
            {
                // CreateProcessW может изменить буфер командной строки — нужен изменяемый StringBuilder
                if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, flags, environment, directory, ref startup, out ProcessInformation created))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, $"Не удалось запустить процесс: {commandLine} (папка {directory ?? "текущая"}): {new Win32Exception(error).Message}");
                }
                return Track(created, commandLine);
            }
            finally
            {
                Marshal.FreeHGlobal(environment);
            }
        }

        /// <summary>
        /// Process по pid созданного процесса. Он берётся (и открывает собственный дескриптор через Process.Handle — для ExitCode и защиты pid
        /// от повторного использования) до того, как закрыты дескрипторы CreateProcessW, иначе завершившийся процесс мог бы потерять свой pid.
        /// </summary>
        private static Process Track(ProcessInformation created, string commandLine)
        {
            Process tracked = null;
            try
            {
                Process process;
                try
                {
                    process = Process.GetProcessById(created.dwProcessId);
                }
                catch (ArgumentException ex)
                {
                    // GetProcessById не находит процесс, завершившийся сразу после запуска: отказ с командной строкой вместо «pid не запущен»
                    throw new InvalidOperationException($"Процесс pid {created.dwProcessId} завершился сразу после запуска: {commandLine}", ex);
                }
                if (process.Handle == IntPtr.Zero)
                {
                    process.Dispose();
                    throw new InvalidOperationException($"Не открыт дескриптор запущенного процесса pid {created.dwProcessId}: {commandLine}");
                }
                tracked = process;
            }
            finally
            {
                bool threadClosed = CloseHandle(created.hThread);
                int threadError = Marshal.GetLastWin32Error();
                bool processClosed = CloseHandle(created.hProcess);
                int processError = Marshal.GetLastWin32Error();
                if (!threadClosed || !processClosed)
                {
                    string failure = $"CloseHandle после запуска pid {created.dwProcessId} ({commandLine}) не прошёл: поток — {(threadClosed ? "закрыт" : "код " + threadError)}, процесс — {(processClosed ? "закрыт" : "код " + processError)}";
                    if (tracked != null)
                    {
                        tracked.Dispose();
                        throw new Win32Exception(threadClosed ? processError : threadError, failure);
                    }
                    // исходное исключение (GetProcessById или Handle) уже летит и объясняет отказ — сбой закрытия только в журнал, чтобы его не подменить
                    UiLog.Warn(failure);
                }
            }
            return tracked;
        }

        private const uint CreateUnicodeEnvironment = 0x00000400;

        private const uint CreateNoWindow = 0x08000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory,
            ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }

    /// <summary>
    /// Повтор WM_CLOSE главному окну при закрытии Revit: с открытым документом Revit может пропустить первый WM_CLOSE,
    /// поэтому сообщение повторяется каждые <see cref="Interval"/>, пока главное окно есть и доступно.
    /// </summary>
    internal static class CloseResend
    {
        internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        /// <summary>Пора ли слать WM_CLOSE: ещё не посылали — сразу; иначе — когда с прошлой отправки прошло не меньше interval.</summary>
        internal static bool Due(TimeSpan? lastSent, TimeSpan now, TimeSpan interval)
        {
            return lastSent == null || now - lastSent.Value >= interval;
        }
    }

    /// <summary>
    /// Одна попытка ping при ожидании готовности Revit: не дольше <see cref="MaxAttempt"/>, чтобы между попытками отвечались TaskDialog,
    /// появившиеся уже после подъёма канала (пока Revit показывает модальное окно, сервер не отвечает). Запоздалый ответ клиент отбрасывает по id.
    /// </summary>
    internal static class ReadyPing
    {
        internal static readonly TimeSpan MaxAttempt = TimeSpan.FromSeconds(5);

        /// <summary>Наименьшее из оставшегося времени ожидания, таймаута канала (hostTimeoutSeconds) и <see cref="MaxAttempt"/>.</summary>
        internal static TimeSpan AttemptTimeout(TimeSpan left, TimeSpan hostTimeout)
        {
            TimeSpan timeout = left < hostTimeout ? left : hostTimeout;
            return timeout < MaxAttempt ? timeout : MaxAttempt;
        }
    }
}
