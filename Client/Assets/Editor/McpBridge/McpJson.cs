using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PingTheKiller.McpBridge
{
    /// <summary>
    /// Minimal JSON reader/writer for the bridge's control channel (editor-only dev tool, not the game
    /// protocol). Objects parse to Dictionary&lt;string, object&gt;, arrays to List&lt;object&gt;, numbers to
    /// double, plus string / bool / null.
    /// </summary>
    internal static class McpJson
    {
        public static object Parse(string text)
        {
            var parser = new Parser(text);
            parser.SkipWhitespace();
            object value = parser.ReadValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw new FormatException("Trailing characters after JSON value.");
            return value;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case Enum e:
                    WriteString(sb, e.ToString());
                    break;
                case float f:
                    WriteNumber(sb, f);
                    break;
                case double d:
                    WriteNumber(sb, d);
                    break;
                case int or long or short or byte or uint or ulong or ushort or sbyte or decimal:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case IDictionary dict:
                    sb.Append('{');
                    bool firstPair = true;
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (!firstPair) sb.Append(',');
                        firstPair = false;
                        WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                        sb.Append(':');
                        WriteValue(sb, entry.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (object item in list)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    WriteString(sb, value.ToString());
                    break;
            }
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _pos;

            public Parser(string text) => _text = text;

            public bool AtEnd => _pos >= _text.Length;

            public void SkipWhitespace()
            {
                while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
            }

            public object ReadValue()
            {
                if (AtEnd) throw new FormatException("Unexpected end of JSON.");
                char c = _text[_pos];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            private Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>();
                _pos++;
                SkipWhitespace();
                if (Peek() == '}') { _pos++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw new FormatException($"Expected property name at {_pos}.");
                    string key = ReadString();
                    SkipWhitespace();
                    if (Next() != ':') throw new FormatException($"Expected ':' at {_pos - 1}.");
                    SkipWhitespace();
                    result[key] = ReadValue();
                    SkipWhitespace();
                    char c = Next();
                    if (c == '}') return result;
                    if (c != ',') throw new FormatException($"Expected ',' or '}}' at {_pos - 1}.");
                }
            }

            private List<object> ReadArray()
            {
                var result = new List<object>();
                _pos++;
                SkipWhitespace();
                if (Peek() == ']') { _pos++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    result.Add(ReadValue());
                    SkipWhitespace();
                    char c = Next();
                    if (c == ']') return result;
                    if (c != ',') throw new FormatException($"Expected ',' or ']' at {_pos - 1}.");
                }
            }

            private string ReadString()
            {
                _pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    char c = Next();
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char esc = Next();
                    switch (esc)
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
                            if (_pos + 4 > _text.Length) throw new FormatException("Truncated \\u escape.");
                            sb.Append((char)int.Parse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _pos += 4;
                            break;
                        default: throw new FormatException($"Invalid escape '\\{esc}'.");
                    }
                }
            }

            private double ReadNumber()
            {
                int start = _pos;
                while (_pos < _text.Length && "+-0123456789.eE".IndexOf(_text[_pos]) >= 0) _pos++;
                if (start == _pos) throw new FormatException($"Unexpected character '{_text[start]}' at {start}.");
                return double.Parse(_text.Substring(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0)
                    throw new FormatException($"Expected '{word}' at {_pos}.");
                _pos += word.Length;
            }

            private char Peek() => AtEnd ? '\0' : _text[_pos];

            private char Next()
            {
                if (AtEnd) throw new FormatException("Unexpected end of JSON.");
                return _text[_pos++];
            }
        }
    }

    /// <summary>Typed accessors for a parsed <c>args</c> object.</summary>
    internal sealed class McpArgs
    {
        private readonly Dictionary<string, object> _values;

        public McpArgs(Dictionary<string, object> values) => _values = values ?? new Dictionary<string, object>();

        public string String(string key, string fallback = "") =>
            _values.TryGetValue(key, out object v) && v is string s ? s : fallback;

        public bool Bool(string key, bool fallback = false) =>
            _values.TryGetValue(key, out object v) && v is bool b ? b : fallback;

        public int Int(string key, int fallback = 0) =>
            _values.TryGetValue(key, out object v) && v is double d ? (int)d : fallback;

        public string[] Strings(string key)
        {
            if (!_values.TryGetValue(key, out object v) || !(v is List<object> list)) return Array.Empty<string>();
            var result = new List<string>(list.Count);
            foreach (object item in list)
                if (item is string s && s.Length > 0) result.Add(s);
            return result.ToArray();
        }
    }
}
