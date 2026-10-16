using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace RevitUITesting.Revit
{
    /// <summary>
    /// Доверие надстройкам до запуска Revit (trustAddins): REG_DWORD 1 в HKCU\Software\Autodesk\Revit\Autodesk Revit &lt;версия&gt;\CodeSigning
    /// с именем = GUID надстройки строчными буквами (AddInId, иначе ClientId из .addin). Значение обычно подавляет диалог
    /// «Security - Unsigned Add-In», но не всегда (замечено после пересборки DLL), поэтому это первая линия защиты, а не гарантия:
    /// RevitApp дополнительно отвечает на диалог «Always Load» при запуске (RevitApp.WaitReady → RevitWindows.AnswerTaskDialogs).
    /// Диалог нельзя закрыть безопасно: WM_CLOSE равносилен «Не загружать». Значение не откатывается — это то же, что «Всегда загружать».
    /// </summary>
    internal static class AddinTrust
    {
        internal static string KeyPath(string revitVersion)
        {
            return $@"Software\Autodesk\Revit\Autodesk Revit {revitVersion}\CodeSigning";
        }

        internal static IReadOnlyList<string> Apply(string revitVersion, IEnumerable<string> addinIds)
        {
            return ApplyAt(KeyPath(revitVersion), addinIds);
        }

        internal static IReadOnlyList<string> ApplyAt(string keyPath, IEnumerable<string> addinIds)
        {
            List<string> ids = Normalize(addinIds);
            List<string> actions = new List<string>();
            if (ids.Count == 0)
            {
                return actions;
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException($@"Не удалось открыть HKCU\{keyPath} для записи доверия надстройкам.");
                }
                foreach (string id in ids)
                {
                    object current = key.GetValue(id);
                    if (current is int value && value == 1)
                    {
                        actions.Add($@"надстройка {id} уже доверена (HKCU\{keyPath})");
                        continue;
                    }
                    key.SetValue(id, 1, RegistryValueKind.DWord);
                    actions.Add($@"доверие надстройке {id}: HKCU\{keyPath}\{id} = 1 (было: {(current == null ? "нет" : current.ToString())}); значение не откатывается");
                }
            }
            return actions;
        }

        internal static List<string> Normalize(IEnumerable<string> addinIds)
        {
            List<string> result = new List<string>();
            foreach (string raw in addinIds ?? Enumerable.Empty<string>())
            {
                if (!Guid.TryParse((raw ?? "").Trim(), out Guid guid))
                {
                    throw new ArgumentException($"trustAddins: «{raw}» — не GUID надстройки (AddInId или ClientId из файла .addin).", nameof(addinIds));
                }
                string id = guid.ToString("D").ToLowerInvariant();
                if (!result.Contains(id))
                {
                    result.Add(id);
                }
            }
            return result;
        }
    }
}
