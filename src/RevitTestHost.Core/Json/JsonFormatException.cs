using System;

namespace RevitTestHost.Core.Json
{
    /// <summary>Входная строка не является корректным JSON.</summary>
    public sealed class JsonFormatException : FormatException
    {
        public JsonFormatException(string message, int position) : base(message)
        {
            Position = position;
        }

        /// <summary>Позиция символа (от 0), на котором остановился разбор.</summary>
        public int Position { get; }
    }
}
