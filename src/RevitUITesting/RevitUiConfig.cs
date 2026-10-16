using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RevitUITesting
{
    /// <summary>
    /// Дополнительный диалог для сброса UI (к встроенному каталогу диалогов Revit): окно с этим заголовком закрывается кнопкой <see cref="Answer"/>.
    /// Неизвестный ключ в элементе knownDialogs — ошибка чтения конфигурации, а не пропуск.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class KnownDialogSetting
    {
        /// <summary>Точный заголовок окна.</summary>
        public string Title { get; set; }

        /// <summary>Фрагмент текста окна (без учёта регистра); пусто — любой текст.</summary>
        public string TextContains { get; set; }

        /// <summary>Подпись кнопки, которой окно закрывается.</summary>
        public string Answer { get; set; }
    }

    /// <summary>
    /// Настройки UI-тестов Revit. Источник (по убыванию приоритета): переменные окружения REVIT_UI_*, файл revit-ui-tests.local.json
    /// (не хранится в git: учётки, пароли), файл revit-ui-tests.json рядом с тестовой сборкой, значения по умолчанию.
    /// Объекты двух файлов сливаются по ключам (вложенные объекты — рекурсивно), массивы и значения заменяются целиком.
    /// Настройки потребителя (адрес стенда, учётки) — в объекте "sections": { "&lt;имя&gt;": { … } }, читаются через <see cref="Section{T}"/>.
    /// </summary>
    public sealed class RevitUiConfig
    {
        public const string FileName = "revit-ui-tests.json";

        public const string LocalFileName = "revit-ui-tests.local.json";

        /// <summary>Префикс переменных окружения.</summary>
        public const string EnvironmentPrefix = "REVIT_UI_";

        /// <summary>Ключ объекта с разделами потребителя.</summary>
        public const string SectionsKey = "sections";

        private static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        private JsonObject _sections = new JsonObject();

        /// <summary>Версия Revit: путь по умолчанию C:\Program Files\Autodesk\Revit {версия}\Revit.exe.</summary>
        public string RevitVersion { get; set; } = "2022";

        /// <summary>Явный путь к Revit.exe, перекрывает <see cref="RevitVersion"/>.</summary>
        public string RevitExe { get; set; }

        /// <summary>Аргумент /language (ENU, RUS, …); null — язык установки.</summary>
        public string Language { get; set; } = "ENU";

        /// <summary>Подхватить уже запущенный Revit (только если в нём включён режим сервера).</summary>
        public bool AttachToRunning { get; set; }

        /// <summary>Не закрывать Revit после прогона (отладка).</summary>
        public bool KeepRevitOpen { get; set; }

        /// <summary>Осведомлённость процесса тестов о DPI (Per-Monitor V2) до создания UI Automation: без неё клики мышью на экране больше 100 % попадают мимо.</summary>
        public bool DpiAware { get; set; } = true;

        /// <summary>Свободный commit (ГБ), без которого Revit не запускается.</summary>
        public int MinFreeCommitGb { get; set; } = 5;

        /// <summary>GUID надстроек, которым перед запуском выставляется доверие (нет диалога «Надстройка без подписи»).</summary>
        public string[] TrustAddins { get; set; } = new string[0];

        /// <summary>Дополнительные диалоги для сброса UI (к встроенному каталогу).</summary>
        public List<KnownDialogSetting> KnownDialogs { get; set; } = new List<KnownDialogSetting>();

        public int DefaultTimeoutSeconds { get; set; } = 30;

        public int RevitStartTimeoutSeconds { get; set; } = 300;

        /// <summary>Сколько ждать ответа сервера RevitTestHost.</summary>
        public int HostTimeoutSeconds { get; set; } = 60;

        public int ModelOpenTimeoutSeconds { get; set; } = 300;

        public int RevitExitTimeoutSeconds { get; set; } = 60;

        /// <summary>Папка артефактов падения; пусто — %TEMP%\RevitUITesting\artifacts.</summary>
        public string ArtifactsDir { get; set; }

        /// <summary>Сколько последних папок артефактов хранить; 0 и меньше — не удалять.</summary>
        public int ArtifactsKeep { get; set; } = 50;

        /// <summary>Ключи верхнего уровня, которых библиотека не знает (опечатка или устаревший ключ).</summary>
        [JsonIgnore]
        public IReadOnlyList<string> UnknownKeys { get; private set; } = new string[0];

        /// <summary>Откуда взяты значения (файлы и имена переменных окружения) — для журнала.</summary>
        [JsonIgnore]
        public IReadOnlyList<string> Sources { get; private set; } = new string[0];

        /// <summary>Имена разделов потребителя из объекта "sections".</summary>
        [JsonIgnore]
        public IReadOnlyList<string> SectionNames => _sections.Select(p => p.Key).ToList();

        public string ResolvedRevitExe => string.IsNullOrWhiteSpace(RevitExe)
            ? $@"C:\Program Files\Autodesk\Revit {RevitVersion}\Revit.exe"
            : RevitExe;

        /// <summary>Имя процесса Revit (файл exe без расширения).</summary>
        public string ProcessName => Path.GetFileNameWithoutExtension(ResolvedRevitExe);

        public string ResolvedArtifactsDir => string.IsNullOrWhiteSpace(ArtifactsDir)
            ? Path.Combine(Path.GetTempPath(), "RevitUITesting", "artifacts")
            : ArtifactsDir;

        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(DefaultTimeoutSeconds);

        public TimeSpan RevitStartTimeout => TimeSpan.FromSeconds(RevitStartTimeoutSeconds);

        public TimeSpan HostTimeout => TimeSpan.FromSeconds(HostTimeoutSeconds);

        public TimeSpan ModelOpenTimeout => TimeSpan.FromSeconds(ModelOpenTimeoutSeconds);

        public TimeSpan RevitExitTimeout => TimeSpan.FromSeconds(RevitExitTimeoutSeconds);

        /// <summary>Прочитать настройки из baseDirectory (по умолчанию — папка тестовой сборки) и переменных окружения.</summary>
        public static RevitUiConfig Load(string baseDirectory = null)
        {
            string dir = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            List<string> sources = new List<string>();
            JsonObject merged = new JsonObject();
            foreach (string name in new[] { FileName, LocalFileName })
            {
                string file = Path.Combine(dir, name);
                if (!File.Exists(file))
                {
                    continue;
                }
                JsonNode node = JsonNode.Parse(File.ReadAllText(file), null, DocumentOptions);
                if (!(node is JsonObject obj))
                {
                    throw new InvalidDataException($"{file}: ожидается JSON-объект");
                }
                Overlay(merged, obj);
                sources.Add(file);
            }
            JsonObject sections = new JsonObject();
            string sectionsKey = merged.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, SectionsKey, StringComparison.OrdinalIgnoreCase));
            if (sectionsKey != null)
            {
                JsonNode value = merged[sectionsKey];
                merged.Remove(sectionsKey);
                if (value != null && !(value is JsonObject))
                {
                    throw new InvalidDataException($"«{SectionsKey}» в {FileName}: ожидается JSON-объект с разделами");
                }
                sections = (JsonObject)value ?? new JsonObject();
            }
            RevitUiConfig config;
            try
            {
                config = JsonSerializer.Deserialize<RevitUiConfig>(merged.ToJsonString(), SerializerOptions) ?? new RevitUiConfig();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{FileName}/{LocalFileName} в {dir}: {ex.Message}", ex);
            }
            config.TrustAddins = config.TrustAddins ?? new string[0];
            config.KnownDialogs = config.KnownDialogs ?? new List<KnownDialogSetting>();
            config._sections = sections;
            config.UnknownKeys = UnknownKeysOf(merged);
            sources.AddRange(config.ApplyEnvironment());
            config.Sources = sources;
            return config;
        }

        /// <summary>Есть ли раздел потребителя с таким именем (без учёта регистра).</summary>
        public bool HasSection(string name)
        {
            return SectionKey(name) != null;
        }

        /// <summary>
        /// Раздел потребителя "sections": { "&lt;name&gt;": { … } } как объект T (имена свойств без учёта регистра).
        /// Переменные окружения REVIT_UI_&lt;NAME&gt;__&lt;СВОЙСТВО&gt; перекрывают файлы (строковые, числовые и логические свойства; логические — 1/0/true/false).
        /// Раздела нет ни в файлах, ни в окружении, в разделе ключ, которого нет в T, или переменная для несуществующего свойства —
        /// <see cref="InvalidDataException"/>.
        /// </summary>
        public T Section<T>(string name) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Пустое имя раздела", nameof(name));
            }
            Dictionary<string, PropertyInfo> properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.GetSetMethod() != null)
                .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            JsonObject section = new JsonObject();
            string key = SectionKey(name);
            if (key != null)
            {
                if (!(_sections[key] is JsonObject fromFiles))
                {
                    throw new InvalidDataException($"Раздел «{SectionsKey}.{key}» в {FileName}: ожидается JSON-объект");
                }
                section = (JsonObject)fromFiles.DeepClone();
            }
            List<string> unknown = section.Select(p => p.Key).Where(k => !properties.ContainsKey(k)).ToList();
            if (unknown.Count > 0)
            {
                throw new InvalidDataException($"Раздел «{SectionsKey}.{name}»: неизвестные ключи {string.Join(", ", unknown)} (свойства {typeof(T).Name}: {string.Join(", ", properties.Keys)})");
            }
            int fromEnvironment = ApplySectionEnvironment(name, section, properties);
            if (key == null && fromEnvironment == 0)
            {
                throw new InvalidDataException($"Нет раздела «{SectionsKey}.{name}» ни в {FileName}/{LocalFileName}, ни в переменных {EnvironmentPrefix}{name.ToUpperInvariant()}__* (источники: {string.Join(", ", Sources)})");
            }
            try
            {
                return JsonSerializer.Deserialize<T>(section.ToJsonString(), SerializerOptions) ?? new T();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Раздел «{SectionsKey}.{name}»: значение не подходит к {typeof(T).Name}: {ex.Message}", ex);
            }
        }

        /// <summary>Проблемы конфигурации (их печатает базовый класс тестов).</summary>
        public IReadOnlyList<string> Validate()
        {
            List<string> problems = new List<string>();
            foreach (string key in UnknownKeys)
            {
                problems.Add($"неизвестный ключ «{key}» (опечатка?)");
            }
            if (string.IsNullOrWhiteSpace(RevitExe) && (RevitVersion == null || !Regex.IsMatch(RevitVersion, @"^\d{4}$")))
            {
                problems.Add($"revitVersion: ожидается год версии Revit (например 2022), получено «{RevitVersion}»");
            }
            if (Language != null && !Regex.IsMatch(Language, "^[A-Z]{3}$"))
            {
                problems.Add($"language: ожидается трёхбуквенный код языка Revit (ENU, RUS, …) или null, получено «{Language}»");
            }
            foreach (KeyValuePair<string, int> timeout in new Dictionary<string, int>
                     {
                         ["defaultTimeoutSeconds"] = DefaultTimeoutSeconds,
                         ["revitStartTimeoutSeconds"] = RevitStartTimeoutSeconds,
                         ["hostTimeoutSeconds"] = HostTimeoutSeconds,
                         ["modelOpenTimeoutSeconds"] = ModelOpenTimeoutSeconds,
                         ["revitExitTimeoutSeconds"] = RevitExitTimeoutSeconds,
                     })
            {
                if (timeout.Value <= 0)
                {
                    problems.Add($"{timeout.Key}: таймаут должен быть положительным, получено {timeout.Value}");
                }
            }
            if (MinFreeCommitGb < 0)
            {
                problems.Add($"minFreeCommitGb: не может быть отрицательным, получено {MinFreeCommitGb}");
            }
            foreach (string addin in TrustAddins)
            {
                if (!Guid.TryParse(addin, out _))
                {
                    problems.Add($"trustAddins: «{addin}» — не GUID надстройки");
                }
            }
            for (int i = 0; i < KnownDialogs.Count; i++)
            {
                KnownDialogSetting dialog = KnownDialogs[i];
                if (dialog == null || string.IsNullOrWhiteSpace(dialog.Title) || string.IsNullOrWhiteSpace(dialog.Answer))
                {
                    problems.Add($"knownDialogs[{i}]: нужны title и answer");
                }
            }
            return problems;
        }

        /// <summary>Конфигурация одной строкой без значений разделов (в них пароли) — для журнала.</summary>
        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"Revit {RevitVersion} ({ResolvedRevitExe}); язык {Language ?? "установки"}");
            sb.Append($"; attachToRunning={AttachToRunning}; keepRevitOpen={KeepRevitOpen}; dpiAware={DpiAware}; minFreeCommitGb={MinFreeCommitGb}");
            sb.Append($"; trustAddins: {TrustAddins.Length}; knownDialogs: {KnownDialogs.Count}");
            sb.Append($"; таймауты default/start/host/model/exit {DefaultTimeoutSeconds}/{RevitStartTimeoutSeconds}/{HostTimeoutSeconds}/{ModelOpenTimeoutSeconds}/{RevitExitTimeoutSeconds} с");
            sb.Append($"; артефакты {ResolvedArtifactsDir} (хранить {ArtifactsKeep})");
            if (_sections.Count > 0)
            {
                sb.Append("; разделы: ").Append(string.Join(", ", SectionNames));
            }
            if (Sources.Count > 0)
            {
                sb.Append("; источники: ").Append(string.Join(", ", Sources));
            }
            return sb.ToString();
        }

        private string SectionKey(string name)
        {
            return _sections.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Слить source в target: ключи без учёта регистра, вложенные объекты — рекурсивно, прочее заменяется.</summary>
        private static void Overlay(JsonObject target, JsonObject source)
        {
            foreach (KeyValuePair<string, JsonNode> pair in source.ToList())
            {
                string existing = target.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, pair.Key, StringComparison.OrdinalIgnoreCase));
                JsonNode value = pair.Value;
                source.Remove(pair.Key);
                if (existing != null && target[existing] is JsonObject targetObject && value is JsonObject sourceObject)
                {
                    Overlay(targetObject, sourceObject);
                    continue;
                }
                if (existing != null)
                {
                    target.Remove(existing);
                }
                target[pair.Key] = value;
            }
        }

        private static IReadOnlyList<string> UnknownKeysOf(JsonObject merged)
        {
            HashSet<string> known = new HashSet<string>(
                typeof(RevitUiConfig).GetProperties().Where(p => p.CanWrite && p.GetSetMethod() != null).Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase);
            return merged.Select(p => p.Key).Where(k => !known.Contains(k)).ToList();
        }

        private IEnumerable<string> ApplyEnvironment()
        {
            List<string> used = new List<string>();
            RevitVersion = Env("REVIT_UI_REVIT_VERSION", RevitVersion, used);
            RevitExe = Env("REVIT_UI_REVIT_EXE", RevitExe, used);
            Language = Env("REVIT_UI_LANGUAGE", Language, used);
            AttachToRunning = Flag("REVIT_UI_ATTACH", AttachToRunning, used);
            KeepRevitOpen = Flag("REVIT_UI_KEEP_REVIT", KeepRevitOpen, used);
            DpiAware = Flag("REVIT_UI_DPI_AWARE", DpiAware, used);
            MinFreeCommitGb = Number("REVIT_UI_MIN_FREE_COMMIT_GB", MinFreeCommitGb, 0, used);
            DefaultTimeoutSeconds = Number("REVIT_UI_DEFAULT_TIMEOUT", DefaultTimeoutSeconds, 1, used);
            ArtifactsDir = Env("REVIT_UI_ARTIFACTS", ArtifactsDir, used);
            return used.Count == 0 ? new string[0] : new[] { "окружение: " + string.Join(", ", used) };
        }

        /// <summary>REVIT_UI_&lt;NAME&gt;__&lt;СВОЙСТВО&gt; → свойство раздела; возвращает число применённых переменных.</summary>
        private static int ApplySectionEnvironment(string name, JsonObject section, Dictionary<string, PropertyInfo> properties)
        {
            string prefix = EnvironmentPrefix + name.ToUpperInvariant() + "__";
            int applied = 0;
            foreach (string variable in Environment.GetEnvironmentVariables().Keys.Cast<string>().OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!variable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string value = Environment.GetEnvironmentVariable(variable);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }
                string propertyName = variable.Substring(prefix.Length);
                if (!properties.TryGetValue(propertyName, out PropertyInfo property))
                {
                    throw new InvalidDataException($"{variable}: у раздела «{name}» нет свойства «{propertyName}»");
                }
                string existing = section.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, property.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    section.Remove(existing);
                }
                section[property.Name] = SectionValue(variable, value, property.PropertyType);
                applied++;
            }
            return applied;
        }

        private static JsonNode SectionValue(string variable, string value, Type type)
        {
            Type target = Nullable.GetUnderlyingType(type) ?? type;
            if (target == typeof(string))
            {
                return JsonValue.Create(value);
            }
            if (target == typeof(bool))
            {
                return JsonValue.Create(ParseFlag(variable, value));
            }
            if (target == typeof(int) || target == typeof(long) || target == typeof(double) || target == typeof(decimal))
            {
                // число читается сериализатором из строки (AllowReadingFromString); не число — ошибка разбора раздела
                return JsonValue.Create(value);
            }
            throw new InvalidDataException($"{variable}: переменной окружения задаются только строковые, числовые и логические свойства, а у свойства тип {type.Name}");
        }

        private static string Env(string name, string fallback, List<string> used)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }
            used.Add(name);
            return value;
        }

        private static bool Flag(string name, bool fallback, List<string> used)
        {
            string value = Env(name, null, used);
            return value == null ? fallback : ParseFlag(name, value);
        }

        private static bool ParseFlag(string name, string value)
        {
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            throw new FormatException($"{name}: ожидается 1, 0, true или false, получено «{value}»");
        }

        private static int Number(string name, int fallback, int min, List<string> used)
        {
            string value = Env(name, null, used);
            if (value == null)
            {
                return fallback;
            }
            if (!int.TryParse(value, out int number) || number < min)
            {
                throw new FormatException($"{name}: ожидается целое число не меньше {min}, получено «{value}»");
            }
            return number;
        }
    }
}
