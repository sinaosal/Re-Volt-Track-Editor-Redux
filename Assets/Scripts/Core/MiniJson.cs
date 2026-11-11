/*
    Re-Volt Track Editor - Unity Edition
    A version of the track editor re-built from the ground up in Unity
    Copyright (C) 2022 Dummiesman

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReVolt.Track
{
    /// <summary>
    /// Minimal JSON reader/writer for editor data files. Values map to
    /// Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool and null.
    /// Dependency free and reflection free, so it behaves the same on every platform and in tests.
    /// </summary>
    public static class MiniJson
    {
        public class ParseException : Exception
        {
            public ParseException(string message) : base(message) { }
        }

        // Reading
        public static object Parse(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            var parser = new Parser(json);
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
                throw new ParseException($"Unexpected data after the end of the document at position {parser.Position}.");
            return value;
        }

        private sealed class Parser
        {
            private readonly string text;
            private int pos;

            public Parser(string text) { this.text = text; }
            public bool AtEnd => pos >= text.Length;
            public int Position => pos;

            public void SkipWhitespace()
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                    pos++;
            }

            private char Peek()
            {
                if (pos >= text.Length)
                    throw new ParseException("Unexpected end of the document.");
                return text[pos];
            }

            private void Expect(char c)
            {
                SkipWhitespace();
                if (Peek() != c)
                    throw new ParseException($"Expected '{c}' at position {pos}.");
                pos++;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                char c = Peek();
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': ExpectWord("true"); return true;
                    case 'f': ExpectWord("false"); return false;
                    case 'n': ExpectWord("null"); return null;
                    default:
                        if (c == '-' || char.IsDigit(c))
                            return ParseNumber();
                        throw new ParseException($"Unexpected character '{c}' at position {pos}.");
                }
            }

            private void ExpectWord(string word)
            {
                if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0)
                    throw new ParseException($"Expected '{word}' at position {pos}.");
                pos += word.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>();
                pos++; // {
                SkipWhitespace();
                if (Peek() == '}')
                {
                    pos++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                        throw new ParseException($"Expected a property name at position {pos}.");
                    string key = ParseString();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();
                    char c = Peek();
                    pos++;
                    if (c == '}')
                        return result;
                    if (c != ',')
                        throw new ParseException($"Expected ',' or '}}' at position {pos - 1}.");
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                pos++; // [
                SkipWhitespace();
                if (Peek() == ']')
                {
                    pos++;
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    char c = Peek();
                    pos++;
                    if (c == ']')
                        return result;
                    if (c != ',')
                        throw new ParseException($"Expected ',' or ']' at position {pos - 1}.");
                }
            }

            private string ParseString()
            {
                pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    char c = Peek();
                    pos++;
                    if (c == '"')
                        return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    char e = Peek();
                    pos++;
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
                            if (pos + 4 > text.Length)
                                throw new ParseException("Truncated unicode escape.");
                            sb.Append((char)Convert.ToInt32(text.Substring(pos, 4), 16));
                            pos += 4;
                            break;
                        default:
                            throw new ParseException($"Invalid escape '\\{e}' at position {pos - 1}.");
                    }
                }
            }

            private double ParseNumber()
            {
                int start = pos;
                while (pos < text.Length && "+-0123456789.eE".IndexOf(text[pos]) >= 0)
                    pos++;
                string number = text.Substring(start, pos - start);
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw new ParseException($"Invalid number '{number}' at position {start}.");
                return value;
            }
        }

        // Writing
        public static string Serialize(object value, bool indented = true)
        {
            var sb = new StringBuilder();
            Write(sb, value, indented, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value, bool indented, int depth)
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
                case IDictionary<string, object> dict:
                    sb.Append('{');
                    bool firstProperty = true;
                    foreach (var pair in dict)
                    {
                        if (!firstProperty)
                            sb.Append(',');
                        firstProperty = false;
                        NewLine(sb, indented, depth + 1);
                        WriteString(sb, pair.Key);
                        sb.Append(indented ? ": " : ":");
                        Write(sb, pair.Value, indented, depth + 1);
                    }
                    if (!firstProperty)
                        NewLine(sb, indented, depth);
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem)
                            sb.Append(',');
                        firstItem = false;
                        NewLine(sb, indented, depth + 1);
                        Write(sb, item, indented, depth + 1);
                    }
                    if (!firstItem)
                        NewLine(sb, indented, depth);
                    sb.Append(']');
                    break;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case int _:
                case long _:
                case short _:
                case byte _:
                case uint _:
                case ushort _:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentException($"Can't serialize {value.GetType().Name} to JSON.");
            }
        }

        private static void NewLine(StringBuilder sb, bool indented, int depth)
        {
            if (!indented)
                return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
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
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // Typed access helpers
        public static Dictionary<string, object> AsObject(object value) => value as Dictionary<string, object>;
        public static List<object> AsArray(object value) => value as List<object>;

        public static double GetNumber(Dictionary<string, object> obj, string key, double fallback = 0)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v is double d ? d : fallback;
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int fallback = 0)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v is double d ? (int)Math.Round(d) : fallback;
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback = null)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v is string s ? s : fallback;
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback = false)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v is bool b ? b : fallback;
        }
    }
}
