using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace RevitUITesting.Revit
{
    /// <summary>Процесс и его private bytes — для сообщения о нехватке памяти.</summary>
    internal sealed class MemoryConsumer
    {
        internal MemoryConsumer(string name, int pid, long privateBytes)
        {
            Name = name;
            Pid = pid;
            PrivateBytes = privateBytes;
        }

        internal string Name { get; }

        internal int Pid { get; }

        internal long PrivateBytes { get; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} (pid {1}) {2:0.0} ГБ", Name, Pid, PrivateBytes / (1024.0 * 1024 * 1024));
        }
    }

    /// <summary>
    /// Свободный commit перед запуском Revit (minFreeCommitGb). Revit с моделью занимает 2–3 ГБ commit; при нехватке он стартует десятки
    /// минут (случай 06.10.2026: утечка контейнеров Docker MCP съела ~15 ГБ). Источник чисел подменяется в тестах.
    /// </summary>
    internal sealed class CommitCheck
    {
        private readonly Func<ulong> _freeCommitBytes;
        private readonly Func<IReadOnlyList<MemoryConsumer>> _topConsumers;

        internal CommitCheck(Func<ulong> freeCommitBytes, Func<IReadOnlyList<MemoryConsumer>> topConsumers)
        {
            _freeCommitBytes = freeCommitBytes ?? throw new ArgumentNullException(nameof(freeCommitBytes));
            _topConsumers = topConsumers ?? throw new ArgumentNullException(nameof(topConsumers));
        }

        internal static CommitCheck Default => new CommitCheck(SystemFreeCommit, TopProcesses);

        internal double FreeCommitGb => _freeCommitBytes() / (1024.0 * 1024 * 1024);

        internal void Require(double minGb)
        {
            if (minGb <= 0)
            {
                UiLog.Action("проверка свободного commit отключена (minFreeCommitGb ≤ 0)");
                return;
            }
            double free = FreeCommitGb;
            string freeText = free.ToString("0.0", CultureInfo.InvariantCulture);
            string minText = minGb.ToString("0.#", CultureInfo.InvariantCulture);
            if (free >= minGb)
            {
                UiLog.Action($"свободный commit {freeText} ГБ ≥ {minText} ГБ");
                return;
            }
            IReadOnlyList<MemoryConsumer> top = _topConsumers();
            throw new InvalidOperationException(
                $"Revit не запущен: свободного commit {freeText} ГБ, а нужно не меньше {minText} ГБ (minFreeCommitGb). " +
                "При нехватке commit Revit стартует десятки минут или падает." + Environment.NewLine +
                "Больше всего памяти (private bytes) занимают:" + Environment.NewLine +
                string.Join(Environment.NewLine, top.Select(c => "  " + c)) + Environment.NewLine +
                "Если среди них vmmem/vmmemWSL — память держит виртуальная машина WSL (Docker); останавливать ли контейнеры, решает пользователь.");
        }

        private static ulong SystemFreeCommit()
        {
            MemoryStatusEx status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalMemoryStatusEx не вернул состояние памяти");
            }
            return status.AvailPageFile;
        }

        private static IReadOnlyList<MemoryConsumer> TopProcesses()
        {
            List<MemoryConsumer> consumers = new List<MemoryConsumer>();
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        consumers.Add(new MemoryConsumer(process.ProcessName, process.Id, process.PrivateMemorySize64));
                    }
                    catch (InvalidOperationException)
                    {
                        // процесс завершился во время перечня — в список потребителей он уже не входит
                        continue;
                    }
                }
            }
            return consumers.OrderByDescending(c => c.PrivateBytes).Take(8).ToList();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    }
}
