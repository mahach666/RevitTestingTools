using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RevitTestHost.Core.Json
{
    /// <summary>
    /// Потоковая запись JSON без сторонних библиотек: надстройка не тянет зависимости в процесс Revit.
    /// Компактный режим — для строк протокола канала (переводов строк в выводе нет),
    /// режим с отступами — для файла отчёта.
    /// </summary>
    public sealed class JsonWriter
    {
        private sealed class Frame
        {
            public bool IsObject;
            public int Count;
            public bool AfterName;
        }

        private readonly StringBuilder _sb = new StringBuilder();
        private readonly List<Frame> _stack = new List<Frame>();
        private readonly bool _indented;
        private bool _rootWritten;

        public JsonWriter(bool indented = false)
        {
            _indented = indented;
        }

        public JsonWriter BeginObject()
        {
            BeforeValue();
            _sb.Append('{');
            _stack.Add(new Frame { IsObject = true });
            return this;
        }

        public JsonWriter EndObject() => End(isObject: true, '}');

        public JsonWriter BeginArray()
        {
            BeforeValue();
            _sb.Append('[');
            _stack.Add(new Frame { IsObject = false });
            return this;
        }

        public JsonWriter EndArray() => End(isObject: false, ']');

        public JsonWriter Name(string name)
        {
            if (_stack.Count == 0 || !_stack[_stack.Count - 1].IsObject)
                throw new InvalidOperationException("Имя свойства JSON можно писать только внутри объекта.");
            var frame = _stack[_stack.Count - 1];
            if (frame.AfterName)
                throw new InvalidOperationException($"Перед свойством '{name}' не записано значение предыдущего свойства.");
            Separate(frame);
            AppendQuoted(name);
            _sb.Append(_indented ? ": " : ":");
            frame.AfterName = true;
            return this;
        }

        public JsonWriter String(string? value)
        {
            BeforeValue();
            if (value == null) _sb.Append("null");
            else AppendQuoted(value);
            return this;
        }

        public JsonWriter Number(long value)
        {
            BeforeValue();
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Bool(bool value)
        {
            BeforeValue();
            _sb.Append(value ? "true" : "false");
            return this;
        }

        public JsonWriter Null()
        {
            BeforeValue();
            _sb.Append("null");
            return this;
        }

        public JsonWriter Property(string name, string? value) => Name(name).String(value);

        public JsonWriter Property(string name, long value) => Name(name).Number(value);

        public JsonWriter Property(string name, bool value) => Name(name).Bool(value);

        /// <summary>Готовый JSON. Бросает, если остались незакрытые объекты или массивы.</summary>
        public override string ToString()
        {
            if (_stack.Count != 0)
                throw new InvalidOperationException($"JSON не завершён: не закрыто вложенных объектов/массивов: {_stack.Count}.");
            return _sb.ToString();
        }

        private JsonWriter End(bool isObject, char closing)
        {
            if (_stack.Count == 0 || _stack[_stack.Count - 1].IsObject != isObject)
                throw new InvalidOperationException($"Нечего закрывать символом '{closing}'.");
            var frame = _stack[_stack.Count - 1];
            if (frame.AfterName)
                throw new InvalidOperationException("У последнего свойства объекта нет значения.");
            _stack.RemoveAt(_stack.Count - 1);
            if (_indented && frame.Count > 0) NewLine(_stack.Count);
            _sb.Append(closing);
            return this;
        }

        private void BeforeValue()
        {
            if (_stack.Count == 0)
            {
                if (_rootWritten)
                    throw new InvalidOperationException("Корневое значение JSON уже записано.");
                _rootWritten = true;
                return;
            }

            var frame = _stack[_stack.Count - 1];
            if (frame.IsObject)
            {
                if (!frame.AfterName)
                    throw new InvalidOperationException("Внутри объекта перед значением нужно записать имя свойства.");
                frame.AfterName = false;
                return;
            }

            Separate(frame);
        }

        private void Separate(Frame frame)
        {
            if (frame.Count > 0) _sb.Append(',');
            if (_indented) NewLine(_stack.Count);
            frame.Count++;
        }

        private void NewLine(int depth)
        {
            _sb.Append('\n');
            _sb.Append(' ', depth * 2);
        }

        private void AppendQuoted(string value)
        {
            _sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
