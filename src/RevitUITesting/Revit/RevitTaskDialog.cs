using System;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using RevitUITesting.Uia;

namespace RevitUITesting.Revit
{
    /// <summary>
    /// TaskDialog Revit (или другое окно с кнопками): заголовок, текст, кнопки и command links по подписи, ответ с проверкой закрытия по HWND.
    /// Кнопка «Close» заголовка окна в перечень не входит.
    /// </summary>
    public sealed class RevitTaskDialog
    {
        public RevitTaskDialog(AutomationElement window)
        {
            Window = window ?? throw new ArgumentNullException(nameof(window));
            Handle = UiQuery.Read(() => window.Properties.NativeWindowHandle.ValueOrDefault, Wait.DefaultTimeout, "HWND диалога Revit");
            Title = window.SafeName();
        }

        public AutomationElement Window { get; }

        public IntPtr Handle { get; }

        public string Title { get; }

        public bool IsOpen => Native.IsShown(Handle);

        /// <summary>Тексты окна без заголовка и подписей кнопок.</summary>
        public string Text => ComposeText(Title, Window.Texts(), Buttons());

        /// <summary>Подписи видимых кнопок (command link — целиком, с пояснением).</summary>
        public IReadOnlyList<string> Buttons()
        {
            return ButtonElements().Select(b => b.SafeName()).ToList();
        }

        /// <summary>Нажать кнопку по подписи (без учёта регистра; у command link — по первой строке) и дождаться закрытия окна.</summary>
        public void Answer(string caption)
        {
            if (string.IsNullOrWhiteSpace(caption))
            {
                throw new ArgumentException("Нужна подпись кнопки.", nameof(caption));
            }
            List<AutomationElement> buttons = ButtonElements();
            List<AutomationElement> matches = buttons.Where(b => ButtonMatches(b.SafeName(), caption)).ToList();
            if (matches.Count == 0)
            {
                throw new InvalidOperationException($"В окне Revit «{Title}» нет кнопки «{caption}»; кнопки: {string.Join(", ", buttons.Select(b => "«" + b.SafeName() + "»"))}. Текст: {Text}");
            }
            if (matches.Count > 1)
            {
                throw new AmbiguousElementException($"кнопка «{caption}» в окне Revit «{Title}»", matches.Select(m => m.Describe()).ToList());
            }
            UiLog.Action($"окно Revit «{Title}»: ответ «{caption}»; текст: {Text}");
            UiAct.Press(matches[0], $"кнопка «{caption}» в окне Revit «{Title}»");
            Wait.Strict(() => !IsOpen, Wait.DefaultTimeout, $"закрытие окна Revit «{Title}» после «{caption}»", null);
        }

        internal static bool ButtonMatches(string buttonName, string caption)
        {
            string wanted = CaptionText.Normalize(caption);
            return string.Equals(CaptionText.Normalize(buttonName), wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(CaptionText.Normalize(CaptionText.FirstLine(buttonName)), wanted, StringComparison.OrdinalIgnoreCase);
        }

        internal static string ComposeText(string title, IEnumerable<string> texts, IEnumerable<string> captions)
        {
            HashSet<string> skip = new HashSet<string>(captions ?? Enumerable.Empty<string>(), StringComparer.Ordinal) { title ?? "" };
            return string.Join(Environment.NewLine, (texts ?? Enumerable.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t) && !skip.Contains(t)).Distinct());
        }

        private List<AutomationElement> ButtonElements()
        {
            return UiQuery.FindAllComplete(Window, By.Type(ControlType.Button), Wait.DefaultTimeout, 8)
                .Where(b => !InTitleBar(b) && !b.SafeIsOffscreen())
                .ToList();
        }

        private static bool InTitleBar(AutomationElement button)
        {
            return UiQuery.Read(() => button.Parent?.Properties.ControlType.ValueOrDefault == ControlType.TitleBar, Wait.DefaultTimeout, "родитель кнопки окна Revit");
        }
    }
}
