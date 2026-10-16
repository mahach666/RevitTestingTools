using System.Text;

namespace RevitUITesting.Revit
{
    /// <summary>Подписи кнопок и вкладок: перенос строки «\n» в PushButtonData, табуляции и неразрывные пробелы сравниваются как один пробел.</summary>
    internal static class CaptionText
    {
        internal static string Normalize(string caption)
        {
            if (string.IsNullOrEmpty(caption))
            {
                return "";
            }
            StringBuilder sb = new StringBuilder(caption.Length);
            bool space = false;
            foreach (char c in caption)
            {
                bool blank = c == ' ' || c == '\n' || c == '\r' || c == '\t' || c == ' ';
                if (blank)
                {
                    space = sb.Length > 0;
                    continue;
                }
                if (space)
                {
                    sb.Append(' ');
                    space = false;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Первая строка подписи (у command link TaskDialog Name = «заголовок\nпояснение»).</summary>
        internal static string FirstLine(string caption)
        {
            if (string.IsNullOrEmpty(caption))
            {
                return "";
            }
            int end = caption.IndexOfAny(new[] { '\r', '\n' });
            return end < 0 ? caption : caption.Substring(0, end);
        }
    }
}
