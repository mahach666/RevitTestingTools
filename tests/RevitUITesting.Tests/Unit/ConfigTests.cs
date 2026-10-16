using System;
using System.IO;
using NUnit.Framework;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>Раздел потребителя для тестов <see cref="RevitUiConfig.Section{T}"/>.</summary>
    public sealed class StandSettings
    {
        public string StandUrl { get; set; }

        public string LibraryUnc { get; set; }

        public string AdminLogin { get; set; }

        public string AdminPassword { get; set; }

        public int Port { get; set; }

        public bool UseSsl { get; set; }

        public string[] Roles { get; set; }
    }

    [TestFixture]
    [NonParallelizable]
    public class ConfigTests
    {
        private static readonly string[] EnvNames =
        {
            "REVIT_UI_REVIT_VERSION", "REVIT_UI_REVIT_EXE", "REVIT_UI_LANGUAGE", "REVIT_UI_ATTACH", "REVIT_UI_KEEP_REVIT", "REVIT_UI_DPI_AWARE",
            "REVIT_UI_MIN_FREE_COMMIT_GB", "REVIT_UI_DEFAULT_TIMEOUT", "REVIT_UI_ARTIFACTS",
            "REVIT_UI_STAND__ADMINPASSWORD", "REVIT_UI_STAND__PORT", "REVIT_UI_STAND__USESSL", "REVIT_UI_STAND__ROLES", "REVIT_UI_STAND__ADMINPASWORD",
            "REVIT_UI_ONLYENV__STANDURL",
        };

        private string _dir;
        private string[] _env;

        [SetUp]
        public void Prepare()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RevitUITesting.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _env = Array.ConvertAll(EnvNames, Environment.GetEnvironmentVariable);
            foreach (string name in EnvNames)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        [TearDown]
        public void Restore()
        {
            for (int i = 0; i < EnvNames.Length; i++)
            {
                Environment.SetEnvironmentVariable(EnvNames[i], _env[i]);
            }
            Directory.Delete(_dir, true);
        }

        private void Main(string json)
        {
            File.WriteAllText(Path.Combine(_dir, RevitUiConfig.FileName), json);
        }

        private void Local(string json)
        {
            File.WriteAllText(Path.Combine(_dir, RevitUiConfig.LocalFileName), json);
        }

        [Test]
        public void Defaults_without_files()
        {
            RevitUiConfig config = RevitUiConfig.Load(_dir);
            Assert.That(config.RevitVersion, Is.EqualTo("2022"));
            Assert.That(config.ResolvedRevitExe, Is.EqualTo(@"C:\Program Files\Autodesk\Revit 2022\Revit.exe"));
            Assert.That(config.ProcessName, Is.EqualTo("Revit"));
            Assert.That(config.Language, Is.EqualTo("ENU"));
            Assert.That(config.AttachToRunning, Is.False);
            Assert.That(config.KeepRevitOpen, Is.False);
            Assert.That(config.DpiAware, Is.True);
            Assert.That(config.MinFreeCommitGb, Is.EqualTo(5));
            Assert.That(config.TrustAddins, Is.Empty);
            Assert.That(config.KnownDialogs, Is.Empty);
            Assert.That(new[] { config.DefaultTimeoutSeconds, config.RevitStartTimeoutSeconds, config.HostTimeoutSeconds, config.ModelOpenTimeoutSeconds, config.RevitExitTimeoutSeconds },
                Is.EqualTo(new[] { 30, 300, 60, 300, 60 }));
            Assert.That(config.ArtifactsKeep, Is.EqualTo(50));
            Assert.That(config.ResolvedArtifactsDir, Is.EqualTo(Path.Combine(Path.GetTempPath(), "RevitUITesting", "artifacts")));
            Assert.That(config.UnknownKeys, Is.Empty);
            Assert.That(config.SectionNames, Is.Empty);
            Assert.That(config.Validate(), Is.Empty);
        }

        [Test]
        public void Spec_example_with_comments_reads_without_problems()
        {
            Main(@"{
  ""revitVersion"": ""2022"",                  // путь по умолчанию
  ""revitExe"": null,
  ""language"": ""ENU"",
  ""attachToRunning"": false,
  ""keepRevitOpen"": false,
  ""dpiAware"": true,
  ""minFreeCommitGb"": 5,
  ""trustAddins"": [""770c23e2-3b3a-4ea3-89a2-d59899bed9a0""],
  ""knownDialogs"": [ { ""title"": ""Acme"", ""textContains"": ""обновление"", ""answer"": ""OK"" } ],
  ""defaultTimeoutSeconds"": 30, ""revitStartTimeoutSeconds"": 300, ""hostTimeoutSeconds"": 60, ""modelOpenTimeoutSeconds"": 300, ""revitExitTimeoutSeconds"": 60,
  ""artifactsDir"": null, ""artifactsKeep"": 50,
}");
            RevitUiConfig config = RevitUiConfig.Load(_dir);
            Assert.That(config.UnknownKeys, Is.Empty);
            Assert.That(config.Validate(), Is.Empty);
            Assert.That(config.TrustAddins, Is.EqualTo(new[] { "770c23e2-3b3a-4ea3-89a2-d59899bed9a0" }));
            Assert.That(config.KnownDialogs[0].Answer, Is.EqualTo("OK"));
            Assert.That(config.KnownDialogs[0].TextContains, Is.EqualTo("обновление"));
        }

        [Test]
        public void Explicit_exe_overrides_version()
        {
            Main(@"{ ""revitVersion"": ""2024"", ""revitExe"": ""D:\\Revit\\Revit.exe"" }");
            RevitUiConfig config = RevitUiConfig.Load(_dir);
            Assert.That(config.ResolvedRevitExe, Is.EqualTo(@"D:\Revit\Revit.exe"));
            Assert.That(config.ProcessName, Is.EqualTo("Revit"));
        }

        [Test]
        public void Local_file_overrides_main_and_unknown_keys_are_reported()
        {
            Main(@"{ ""language"": ""ENU"", ""revitVersoin"": ""2023"", ""artifactsKeep"": 10 }");
            Local(@"{ ""Language"": ""RUS"" }");
            RevitUiConfig config = RevitUiConfig.Load(_dir);
            Assert.That(config.Language, Is.EqualTo("RUS"), "ключи без учёта регистра, локальный файл важнее");
            Assert.That(config.ArtifactsKeep, Is.EqualTo(10));
            Assert.That(config.UnknownKeys, Is.EquivalentTo(new[] { "revitVersoin" }));
            Assert.That(config.Validate(), Has.Some.Contains("revitVersoin"));
            Assert.That(config.Sources, Has.Count.EqualTo(2));
        }

        [Test]
        public void Null_language_means_installation_language()
        {
            Main(@"{ ""language"": null }");
            RevitUiConfig config = RevitUiConfig.Load(_dir);
            Assert.That(config.Language, Is.Null);
            Assert.That(config.Validate(), Is.Empty);
            Assert.That(config.Describe(), Does.Contain("язык установки"));
        }

        [Test]
        public void Environment_wins()
        {
            Main(@"{ ""dpiAware"": true, ""attachToRunning"": false, ""defaultTimeoutSeconds"": 10 }");
            Environment.SetEnvironmentVariable("REVIT_UI_DPI_AWARE", "0");
            Environment.SetEnvironmentVariable("REVIT_UI_ATTACH", "true");
            Environment.SetEnvironmentVariable("REVIT_UI_DEFAULT_TIMEOUT", "45");
            Environment.SetEnvironmentVariable("REVIT_UI_REVIT_VERSION", "2024");
            RevitUiConfig config = RevitUiConfig.Load(_dir);
            Assert.That(config.DpiAware, Is.False);
            Assert.That(config.AttachToRunning, Is.True);
            Assert.That(config.DefaultTimeoutSeconds, Is.EqualTo(45));
            Assert.That(config.ResolvedRevitExe, Is.EqualTo(@"C:\Program Files\Autodesk\Revit 2024\Revit.exe"));
            Assert.That(config.Sources, Has.Some.Contains("REVIT_UI_ATTACH"));
        }

        [TestCase("REVIT_UI_DEFAULT_TIMEOUT", "minute")]
        [TestCase("REVIT_UI_DEFAULT_TIMEOUT", "0")]
        [TestCase("REVIT_UI_ATTACH", "yes")]
        [TestCase("REVIT_UI_MIN_FREE_COMMIT_GB", "-1")]
        public void Bad_environment_value_is_error_not_silence(string name, string value)
        {
            Environment.SetEnvironmentVariable(name, value);
            FormatException ex = Assert.Throws<FormatException>(() => RevitUiConfig.Load(_dir));
            Assert.That(ex.Message, Does.Contain(name).And.Contain(value));
        }

        [Test]
        public void Validate_lists_each_problem()
        {
            Main(@"{ ""revitVersion"": ""22"", ""language"": ""en"", ""hostTimeoutSeconds"": 0, ""minFreeCommitGb"": -1,
  ""trustAddins"": [""AcmeDev""], ""knownDialogs"": [ { ""title"": ""Acme"" } ] }");
            string problems = string.Join(Environment.NewLine, RevitUiConfig.Load(_dir).Validate());
            Assert.That(problems, Does.Contain("revitVersion").And.Contain("language").And.Contain("hostTimeoutSeconds")
                .And.Contain("minFreeCommitGb").And.Contain("«AcmeDev» — не GUID").And.Contain("knownDialogs[0]"));
        }

        [Test]
        public void Typo_inside_known_dialog_is_an_error()
        {
            Main(@"{ ""knownDialogs"": [ { ""title"": ""Acme"", ""answr"": ""OK"" } ] }");
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => RevitUiConfig.Load(_dir));
            Assert.That(ex.Message, Does.Contain("answr"));
        }

        [Test]
        public void Main_file_must_be_an_object()
        {
            Main("[1, 2]");
            Assert.Throws<InvalidDataException>(() => RevitUiConfig.Load(_dir));
        }

        [Test]
        public void Section_merges_files_deeply_and_environment_wins()
        {
            Main(@"{ ""sections"": { ""stand"": { ""standUrl"": ""http://stand.example/"", ""libraryUnc"": ""\\\\stand.example\\Share\\Library"", ""adminLogin"": ""admin"", ""port"": 80 } } }");
            Local(@"{ ""sections"": { ""Stand"": { ""adminPassword"": ""из-файла"" } } }");
            Environment.SetEnvironmentVariable("REVIT_UI_STAND__ADMINPASSWORD", "из-окружения");
            Environment.SetEnvironmentVariable("REVIT_UI_STAND__PORT", "8080");
            Environment.SetEnvironmentVariable("REVIT_UI_STAND__USESSL", "1");
            RevitUiConfig config = RevitUiConfig.Load(_dir);
            Assert.That(config.UnknownKeys, Is.Empty, "sections — известный ключ");
            Assert.That(config.HasSection("STAND"), Is.True);
            Assert.That(config.SectionNames, Is.EqualTo(new[] { "stand" }));
            StandSettings stand = config.Section<StandSettings>("stand");
            Assert.That(stand.StandUrl, Is.EqualTo("http://stand.example/"), "локальный файл не стёр раздел основного");
            Assert.That(stand.LibraryUnc, Is.EqualTo(@"\\stand.example\Share\Library"));
            Assert.That(stand.AdminLogin, Is.EqualTo("admin"));
            Assert.That(stand.AdminPassword, Is.EqualTo("из-окружения"));
            Assert.That(stand.Port, Is.EqualTo(8080));
            Assert.That(stand.UseSsl, Is.True);
            Assert.That(config.Describe(), Does.Contain("разделы: stand").And.Not.Contain("из-файла").And.Not.Contain("из-окружения"));
        }

        [Test]
        public void Section_only_from_environment()
        {
            Environment.SetEnvironmentVariable("REVIT_UI_ONLYENV__STANDURL", "http://stand/");
            RevitUiConfig config = RevitUiConfig.Load(_dir);
            Assert.That(config.HasSection("onlyEnv"), Is.False);
            Assert.That(config.Section<StandSettings>("onlyEnv").StandUrl, Is.EqualTo("http://stand/"));
        }

        [Test]
        public void Missing_section_is_an_error()
        {
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => RevitUiConfig.Load(_dir).Section<StandSettings>("stand"));
            Assert.That(ex.Message, Does.Contain("sections.stand").And.Contain("REVIT_UI_STAND__"));
        }

        [Test]
        public void Unknown_key_inside_section_is_an_error()
        {
            Main(@"{ ""sections"": { ""stand"": { ""standUrll"": ""http://stand/"" } } }");
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => RevitUiConfig.Load(_dir).Section<StandSettings>("stand"));
            Assert.That(ex.Message, Does.Contain("standUrll"));
        }

        [Test]
        public void Environment_variable_for_unknown_section_property_is_an_error()
        {
            Main(@"{ ""sections"": { ""stand"": { ""standUrl"": ""http://stand/"" } } }");
            Environment.SetEnvironmentVariable("REVIT_UI_STAND__ADMINPASWORD", "x");
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => RevitUiConfig.Load(_dir).Section<StandSettings>("stand"));
            Assert.That(ex.Message, Does.Contain("REVIT_UI_STAND__ADMINPASWORD"));
        }

        [Test]
        public void Bad_section_values_are_errors()
        {
            Main(@"{ ""sections"": { ""stand"": { ""standUrl"": ""http://stand/"" } } }");
            Environment.SetEnvironmentVariable("REVIT_UI_STAND__USESSL", "да");
            Assert.Throws<FormatException>(() => RevitUiConfig.Load(_dir).Section<StandSettings>("stand"));
            Environment.SetEnvironmentVariable("REVIT_UI_STAND__USESSL", null);
            Environment.SetEnvironmentVariable("REVIT_UI_STAND__PORT", "восемьдесят");
            Assert.Throws<InvalidDataException>(() => RevitUiConfig.Load(_dir).Section<StandSettings>("stand"));
            Environment.SetEnvironmentVariable("REVIT_UI_STAND__PORT", null);
            Environment.SetEnvironmentVariable("REVIT_UI_STAND__ROLES", "designer");
            Assert.Throws<InvalidDataException>(() => RevitUiConfig.Load(_dir).Section<StandSettings>("stand"), "массив переменной окружения не задаётся");
        }
    }
}
