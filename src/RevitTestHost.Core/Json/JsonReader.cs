using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RevitTestHost.Core.Json
{
    /// <summary>
    /// Строгий разбор JSON (RFC 8259) в дерево: объект — <see cref="Dictionary{TKey,TValue}"/> со строковыми ключами
    /// (повтор ключа — ошибка), массив — <see cref="List{T}"/>, строка — <see cref="string"/>, целое — <see cref="long"/>,
    /// прочие числа — <see cref="double"/>, true/false — <see cref="bool"/>, null — null.
    /// Любое отклонение от грамматики — <see cref="JsonFormatException"/> с позицией.
    /// </summary>
    public static class JsonReader
    {
        /// <summary>Предел вложенности: защита от переполнения стека на враждебном вводе.</summary>
        public const int MaxDepth = 64;

        public static object? Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var parser = new Parser(text);
            parser.SkipWhitespace();
            var value = parser.ParseValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd)
                throw parser.Error("после значения JSON есть лишние символы");
            return value;
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _pos;

            public Parser(string text) => _text = text;

            public bool AtEnd => _pos >= _text.Length;

            public JsonFormatException Error(string message) =>
                new JsonFormatException($"Некорректный JSON (позиция {_pos}): {message}.", _pos);

            public void SkipWhitespace()
            {
                while (_pos < _text.Length)
                {
                    var c = _text[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _pos++;
                    else break;
                }
            }

            public object? ParseValue(int depth)
            {
                if (AtEnd) throw Error("ожидалось значение, а строка закончилась");
                var c = _text[_pos];
                switch (c)
                {
                    case '{': return ParseObject(depth + 1);
                    case '[': return ParseArray(depth + 1);
                    case '"': return ParseString();
                    case 't': ExpectLiteral("true"); return true;
                    case 'f': ExpectLiteral("false"); return false;
                    case 'n': ExpectLiteral("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error($"неожиданный символ '{c}'");
                }
            }

            private Dictionary<string, object?> ParseObject(int depth)
            {
                if (depth > MaxDepth) throw Error($"вложенность больше {MaxDepth}");
                _pos++; // {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == '}')
                {
                    _pos++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_pos] != '"') throw Error("ожидалось имя свойства в кавычках");
                    var keyPos = _pos;
                    var key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || _text[_pos] != ':') throw Error("ожидалось ':' после имени свойства");
                    _pos++;
                    SkipWhitespace();
                    var value = ParseValue(depth);
                    if (result.ContainsKey(key))
                        throw new JsonFormatException($"Некорректный JSON (позиция {keyPos}): свойство '{key}' повторяется.", keyPos);
                    result.Add(key, value);
                    SkipWhitespace();
                    if (AtEnd) throw Error("объект не закрыт");
                    var c = _text[_pos++];
                    if (c == '}') return result;
                    if (c != ',') { _pos--; throw Error("ожидалось ',' или '}'"); }
                }
            }

            private List<object?> ParseArray(int depth)
            {
                if (depth > MaxDepth) throw Error($"вложенность больше {MaxDepth}");
                _pos++; // [
                var result = new List<object?>();
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == ']')
                {
                    _pos++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    result.Add(ParseValue(depth));
                    SkipWhitespace();
                    if (AtEnd) throw Error("массив не закрыт");
                    var c = _text[_pos++];
                    if (c == ']') return result;
                    if (c != ',') { _pos--; throw Error("ожидалось ',' или ']'"); }
                }
            }

            private string ParseString()
            {
                _pos++; // открывающая кавычка
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("строка не закрыта");
                    var c = _text[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) { _pos--; throw Error("управляющий символ внутри строки должен быть экранирован"); }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (AtEnd) throw Error("строка оборвалась после '\\'");
                    var e = _text[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length) throw Error("неполная последовательность \\u");
                            var hex = _text.Substring(_pos, 4);
                            if (!int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                                throw Error($"некорректная последовательность \\u{hex}");
                            sb.Append((char)code);
                            _pos += 4;
                            break;
                        default:
                            _pos--;
                            throw Error($"недопустимая escape-последовательность '\\{e}'");
                    }
                }
            }

            private object ParseNumber()
            {
                var start = _pos;
                if (_text[_pos] == '-') _pos++;
                if (AtEnd) throw Error("после '-' нет цифр");
                if (_text[_pos] == '0') _pos++;
                else if (_text[_pos] >= '1' && _text[_pos] <= '9') SkipDigits();
                else throw Error("ожидалась цифра");

                var isInteger = true;
                if (!AtEnd && _text[_pos] == '.')
                {
                    isInteger = false;
                    _pos++;
                    if (AtEnd || !IsDigit(_text[_pos])) throw Error("после '.' нужны цифры");
                    SkipDigits();
                }
                if (!AtEnd && (_text[_pos] == 'e' || _text[_pos] == 'E'))
                {
                    isInteger = false;
                    _pos++;
                    if (!AtEnd && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                    if (AtEnd || !IsDigit(_text[_pos])) throw Error("в экспоненте нужны цифры");
                    SkipDigits();
                }

                var token = _text.Substring(start, _pos - start);
                if (isInteger && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
                    return l;

                try
                {
                    var number = double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
                    // .NET Core и новее возвращают бесконечность вместо OverflowException
                    if (double.IsInfinity(number)) throw Error("число вне допустимого диапазона");
                    return number;
                }
                catch (OverflowException)
                {
                    // На .NET Framework 4.8 число вроде 1e999 грамматически верно, но не помещается в double.
                    throw Error("число вне допустимого диапазона");
                }
            }

            private void SkipDigits()
            {
                while (!AtEnd && IsDigit(_text[_pos])) _pos++;
            }

            private static bool IsDigit(char c) => c >= '0' && c <= '9';

            private void ExpectLiteral(string literal)
            {
                if (string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
                    throw Error($"ожидалось '{literal}'");
                _pos += literal.Length;
            }
        }
    }
}
