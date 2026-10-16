namespace RevitTestHost.Core.Server
{
    public enum HostMode
    {
        /// <summary>Обычный запуск Revit: только кнопка в ленте.</summary>
        None,

        /// <summary>Headless-прогон и выход (<c>REVITTESTHOST_CLI=1</c>).</summary>
        Cli,

        /// <summary>Сервер named pipe (<c>REVITTESTHOST_SERVE=&lt;имя канала&gt;</c>).</summary>
        Serve
    }

    /// <summary>Выбранный режим и ошибка конфигурации, которую надстройка пишет в журнал.</summary>
    public sealed class HostModeDecision
    {
        public HostModeDecision(HostMode mode, string? pipeName, string? error)
        {
            Mode = mode;
            PipeName = pipeName;
            Error = error;
        }

        public HostMode Mode { get; }

        /// <summary>Имя канала для <see cref="HostMode.Serve"/>, иначе null.</summary>
        public string? PipeName { get; }

        /// <summary>Текст ошибки конфигурации или null.</summary>
        public string? Error { get; }
    }

    /// <summary>
    /// Выбор режима по переменным окружения процесса Revit. CLI-режим не меняется; обе переменные сразу —
    /// ошибка в журнал и только CLI-режим; недопустимое имя канала — ошибка в журнал, сервер не поднимается.
    /// </summary>
    public static class HostModeSelector
    {
        public const string CliVariable = "REVITTESTHOST_CLI";
        public const string ServeVariable = "REVITTESTHOST_SERVE";

        public static HostModeDecision Select(string? cliValue, string? serveValue)
        {
            var cli = cliValue == "1";
            var serveRequested = !string.IsNullOrWhiteSpace(serveValue);

            if (cli && serveRequested)
                return new HostModeDecision(HostMode.Cli, null,
                    $"Заданы обе переменные {CliVariable}=1 и {ServeVariable}={serveValue}: режимы не совмещаются, работает только CLI-режим.");
            if (cli)
                return new HostModeDecision(HostMode.Cli, null, null);
            if (!serveRequested)
                return new HostModeDecision(HostMode.None, null, null);

            var pipeName = serveValue!.Trim();
            if (!ServeWorkspace.IsValidPipeName(pipeName))
                return new HostModeDecision(HostMode.None, null,
                    $"Недопустимое имя канала в {ServeVariable}: '{serveValue}'. Допустимы латинские буквы, цифры, '-' и '_' (до 128 символов), " +
                    "например RevitTestHost-<guid без дефисов>. Режим сервера не включён.");
            return new HostModeDecision(HostMode.Serve, pipeName, null);
        }
    }
}
