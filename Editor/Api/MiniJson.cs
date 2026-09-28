using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UISpriteMaker.Editor
{
    /// <summary>
    /// Minimal JSON reader/writer. Objects become <see cref="Dictionary{TKey,TValue}"/> (string, object),
    /// arrays become <see cref="List{T}"/> (object), numbers become double.
    /// JsonUtility can't tell a missing field from a default one, which the sprite spec relies on.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            var reader = new Reader(json ?? throw new ArgumentNullException(nameof(json)));
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw reader.Error("Unexpected trailing characters");
            return value;
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object value, int indent)
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
                case IDictionary<string, object> obj:
                    if (obj.Count == 0) { sb.Append("{}"); break; }
                    sb.Append("{\n");
                    int i = 0;
                    foreach (var kv in obj)
                    {
                        sb.Append(' ', (indent + 1) * 2);
                        WriteString(sb, kv.Key);
                        sb.Append(": ");
                        Write(sb, kv.Value, indent + 1);
                        if (++i < obj.Count) sb.Append(',');
                        sb.Append('\n');
                    }
                    sb.Append(' ', indent * 2).Append('}');
                    break;
                case IList<object> list:
                    bool inline = list.TrueForAllScalars();
                    sb.Append('[');
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (!inline) sb.Append('\n').Append(' ', (indent + 1) * 2);
                        Write(sb, list[j], indent + 1);
                        if (j < list.Count - 1) sb.Append(inline ? ", " : ",");
                    }
                    if (!inline && list.Count > 0) sb.Append('\n').Append(' ', indent * 2);
                    sb.Append(']');
                    break;
                case float f:
                    sb.Append(FormatNumber(f));
                    break;
                case double d:
                    sb.Append(FormatNumber(d));
                    break;
                case int n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentException($"Unsupported JSON value type {value.GetType()}");
            }
        }

        static bool TrueForAllScalars(this IList<object> list)
        {
            foreach (var v in list)
                if (v is IDictionary<string, object> || v is IList<object>) return false;
            return true;
        }

        static string FormatNumber(double d) =>
            Math.Round(d, 4).ToString("0.####", CultureInfo.InvariantCulture);

        static void WriteString(StringBuilder sb, string s)
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

        sealed class Reader
        {
            readonly string _s;
            int _i;

            public Reader(string s) => _s = s;

            public bool AtEnd => _i >= _s.Length;

            public FormatException Error(string message)
            {
                int line = 1, col = 1;
                for (int k = 0; k < _i && k < _s.Length; k++)
                {
                    if (_s[k] == '\n') { line++; col = 1; }
                    else col++;
                }
                return new FormatException($"Invalid JSON: {message} at line {line}, column {col}.");
            }

            public void SkipWhitespace()
            {
                while (!AtEnd)
                {
                    char c = _s[_i];
                    if (char.IsWhiteSpace(c)) { _i++; continue; }
                    // Allow // line comments so hand-written spec files can be annotated.
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                    {
                        while (!AtEnd && _s[_i] != '\n') _i++;
                        continue;
                    }
                    break;
                }
            }

            public object ReadValue()
            {
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of input");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || char.IsDigit(c)) return ReadNumber();
                        throw Error($"Unexpected character '{c}'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error($"Expected '{word}'");
                _i += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var obj = new Dictionary<string, object>();
                _i++; // {
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw Error("Expected property name");
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw Error("Expected ':'");
                    _i++;
                    obj[key] = ReadValue();
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return obj; }
                    throw Error("Expected ',' or '}'");
                }
            }

            List<object> ReadArray()
            {
                var list = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return list; }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated array");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list; }
                    throw Error("Expected ',' or ']'");
                }
            }

            string ReadString()
            {
                var sb = new StringBuilder();
                _i++; // "
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("Unterminated string");
                    char e = _s[_i++];
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
                            if (_i + 4 > _s.Length) throw Error("Bad unicode escape");
                            sb.Append((char)Convert.ToInt32(_s.Substring(_i, 4), 16));
                            _i += 4;
                            break;
                        default: throw Error($"Bad escape '\\{e}'");
                    }
                }
            }

            double ReadNumber()
            {
                int start = _i;
                while (!AtEnd && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw Error("Invalid number");
                return d;
            }
        }
    }
}
