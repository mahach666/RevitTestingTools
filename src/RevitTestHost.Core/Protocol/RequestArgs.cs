using System;
using System.Collections.Generic;
using System.IO;
using RevitTestHost.Core.Execution;

namespace RevitTestHost.Core.Protocol
{
    /// <summary>Чтение аргументов запроса с ошибкой <c>BAD_REQUEST</c> при неверном типе.</summary>
    internal static class ArgReader
    {
        public static string? OptionalString(IReadOnlyDictionary<string, object?> args, string name)
        {
            if (!args.TryGetValue(name, out var value) || value == null) return null;
            return value as string ?? throw BadRequest($"аргумент '{name}' должен быть строкой");
        }

        public static bool OptionalBool(IReadOnlyDictionary<string, object?> args, string name, bool defaultValue)
        {
            if (!args.TryGetValue(name, out var value) || value == null) return defaultValue;
            return value is bool b ? b : throw BadRequest($"аргумент '{name}' должен быть true или false");
        }

        public static IReadOnlyDictionary<string, object?>? OptionalObject(IReadOnlyDictionary<string, object?> args, string name)
        {
            if (!args.TryGetValue(name, out var value) || value == null) return null;
            return value as Dictionary<string, object?> ?? throw BadRequest($"аргумент '{name}' должен быть JSON-объектом");
        }

        /// <summary>
        /// Путь полностью определён: <c>X:\…</c>, <c>X:/…</c> или UNC <c>\\сервер\ресурс…</c>.
        /// Пути «диск без каталога» (<c>C:файл</c>) и «от корня текущего диска» (<c>\файл</c>, <c>/файл</c>) — не абсолютные:
        /// их смысл зависит от текущего каталога процесса Revit. Для полностью определённого пути с недопустимыми
        /// символами или слишком длинного бросает <c>BAD_REQUEST</c>.
        /// </summary>
        public static bool IsAbsolutePath(string path)
        {
            if (!IsFullyQualified(path)) return false;
            var invalid = IndexOfInvalidPathChar(path);
            if (invalid >= 0)
                throw BadRequest($"некорректный путь '{path}': недопустимый символ в позиции {invalid}");
            try
            {
                Path.GetFullPath(path); // проверка символов и длины
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw BadRequest($"некорректный путь '{path}': {ExceptionText.Describe(ex)}");
            }
        }

        /// <summary>Набор недопустимых символов .NET Framework (кавычка, меньше, больше, вертикальная черта, управляющие): одинаков на всех рантаймах.</summary>
        private static int IndexOfInvalidPathChar(string path)
        {
            for (var i = 0; i < path.Length; i++)
            {
                var c = path[i];
                if (c < ' ' || c == '"' || c == '<' || c == '>' || c == '|') return i;
            }
            return -1;
        }

        private static bool IsFullyQualified(string path)
        {
            var isDriveAbsolute = path.Length >= 3 && IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2]);
            var isUnc = path.Length > 2 && path[0] == '\\' && path[1] == '\\';
            return isDriveAbsolute || isUnc;
        }

        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        private static bool IsSeparator(char c) => c == '\\' || c == '/';

        public static HostCommandException BadRequest(string message) => new HostCommandException(ErrorCodes.BadRequest, message);
    }

    /// <summary>Аргументы <c>open</c>: <c>{"path":"C:\\...rvt" | "@new","copyToTemp":bool}</c>.</summary>
    public sealed class OpenArgs
    {
        private OpenArgs(string path, bool copyToTemp)
        {
            Path = path;
            CopyToTemp = copyToTemp;
        }

        public string Path { get; }
        public bool CopyToTemp { get; }
        public bool IsNewProject => Path == ModelKeys.NewProject;

        public static OpenArgs Parse(IReadOnlyDictionary<string, object?> args)
        {
            var path = ArgReader.OptionalString(args, "path");
            if (string.IsNullOrWhiteSpace(path))
                throw ArgReader.BadRequest("аргумент 'path' обязателен: абсолютный путь к файлу Revit или \"@new\"");
            var copyToTemp = ArgReader.OptionalBool(args, "copyToTemp", false);
            if (path != ModelKeys.NewProject && !ArgReader.IsAbsolutePath(path!))
                throw ArgReader.BadRequest($"путь '{path}' должен быть абсолютным (или \"@new\")");
            return new OpenArgs(path!, copyToTemp);
        }
    }

    /// <summary>Аргументы <c>close</c>: ровно одно из <c>title</c>/<c>path</c>, <c>save</c> только false.</summary>
    public sealed class CloseArgs
    {
        private CloseArgs(string? title, string? path)
        {
            Title = title;
            Path = path;
        }

        /// <summary>Заголовок документа; <c>null</c>, если задан <see cref="Path"/>.</summary>
        public string? Title { get; }

        /// <summary>Абсолютный путь документа; <c>null</c>, если задан <see cref="Title"/>.</summary>
        public string? Path { get; }

        public static CloseArgs Parse(IReadOnlyDictionary<string, object?> args)
        {
            var title = ArgReader.OptionalString(args, "title");
            var path = ArgReader.OptionalString(args, "path");
            var hasTitle = !string.IsNullOrWhiteSpace(title);
            var hasPath = !string.IsNullOrWhiteSpace(path);
            if (hasTitle == hasPath)
                throw ArgReader.BadRequest("укажите ровно один аргумент: 'title' или 'path'");
            if (ArgReader.OptionalBool(args, "save", false))
                throw ArgReader.BadRequest("save=true не поддерживается: сервер закрывает документы только без сохранения");
            if (hasPath && !ArgReader.IsAbsolutePath(path!))
                throw ArgReader.BadRequest($"путь '{path}' должен быть абсолютным");
            return new CloseArgs(hasTitle ? title : null, hasPath ? path : null);
        }
    }

    /// <summary>Аргументы <c>run</c>: <c>{"filter":"..","model":"@active","parameters":{"k":"v"}}</c>.</summary>
    public sealed class RunArgs
    {
        private RunArgs(string? filter, string model, IReadOnlyDictionary<string, string> parameters)
        {
            Filter = filter;
            Model = model;
            Parameters = parameters;
        }

        public string? Filter { get; }

        /// <summary>«@active» (по умолчанию) или заголовок/абсолютный путь открытого документа, который получат тесты с Model = "@active".</summary>
        public string Model { get; }

        public IReadOnlyDictionary<string, string> Parameters { get; }

        public static RunArgs Parse(IReadOnlyDictionary<string, object?> args)
        {
            var filter = ArgReader.OptionalString(args, "filter");
            var model = ArgReader.OptionalString(args, "model") ?? ModelKeys.ActiveDocument;
            if (string.IsNullOrWhiteSpace(model))
                throw ArgReader.BadRequest("аргумент 'model' не может быть пустым: \"@active\", заголовок или путь открытого документа");

            var raw = ArgReader.OptionalObject(args, "parameters");
            IReadOnlyDictionary<string, string> parameters;
            if (raw == null)
            {
                parameters = RunParameters.Empty;
            }
            else
            {
                var values = new List<KeyValuePair<string, string>>();
                foreach (var pair in raw)
                {
                    if (!(pair.Value is string s))
                        throw ArgReader.BadRequest($"параметр '{pair.Key}' должен быть строкой");
                    values.Add(new KeyValuePair<string, string>(pair.Key, s));
                }
                parameters = RunParameters.From(values);
            }

            return new RunArgs(filter, model, parameters);
        }
    }
}
