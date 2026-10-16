using System;
using System.IO;

namespace RevitTestHost.Core.Server
{
    /// <summary>
    /// Временная папка пользователя — <c>%TEMP%</c> из его окружения, а не из окружения процесса.
    /// Revit при старте подменяет своему процессу <c>TEMP</c>/<c>TMP</c> на папку сеанса
    /// (<c>%TEMP%\&lt;guid сеанса&gt;\</c>), поэтому <see cref="Path.GetTempPath"/> внутри Revit
    /// не совпадает с <c>%TEMP%</c>, который видит клиент.
    /// </summary>
    public static class UserTemp
    {
        /// <summary>
        /// Пользовательское значение <c>TEMP</c> с раскрытыми переменными; если оно не задано —
        /// <c>%LOCALAPPDATA%\Temp</c>.
        /// </summary>
        public static string Directory =>
            Resolve(() => Environment.GetEnvironmentVariable("TEMP", EnvironmentVariableTarget.User));

        /// <param name="readUserTempVariable">Чтение пользовательского значения <c>TEMP</c> (подменяется в тестах).</param>
        internal static string Resolve(Func<string?> readUserTempVariable)
        {
            if (readUserTempVariable == null) throw new ArgumentNullException(nameof(readUserTempVariable));
            var value = readUserTempVariable();
            if (!string.IsNullOrWhiteSpace(value))
                return Environment.ExpandEnvironmentVariables(value!.Trim());
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
        }
    }
}
