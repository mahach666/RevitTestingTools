using System;

namespace RevitTestHost.Core.Protocol
{
    /// <summary>Единый формат текста ошибки из исключения для ответов канала и журнала: «Полное.Имя.Типа: сообщение».</summary>
    public static class ExceptionText
    {
        public static string Describe(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            return $"{exception.GetType().FullName}: {exception.Message}";
        }
    }
}
