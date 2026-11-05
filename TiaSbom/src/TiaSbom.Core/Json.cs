using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TiaSbom.Core
{
    /// <summary>
    /// JSON object that keeps member order and skips null values.
    /// Usage: new JsonObject { { "type", "device" }, { "name", name } }
    /// </summary>
    public sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<KeyValuePair<string, object>> _members = new List<KeyValuePair<string, object>>();

        public void Add(string name, object value)
        {
            if (value != null) _members.Add(new KeyValuePair<string, object>(name, value));
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _members.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Minimal JSON writer and parser with no external dependencies.
    /// The Add-In runs inside the TIA Portal process without binding redirects, so System.Text.Json
    /// (and CycloneDX.Core, which depends on it) fail with "type initializer ... threw an exception".
    /// Supports JsonObject, IEnumerable (arrays), string, bool, int, long.
    /// </summary>
    public static class Json
    {
        /// <summary>Returns Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool or null.</summary>
        public static object Parse(string text) => new JsonReader(text).ReadDocument();

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value, int level)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case JsonObject obj: WriteObject(sb, obj, level); break;
                case IEnumerable items: WriteArray(sb, items, level); break;
                default: throw new NotSupportedException("Cannot serialize " + value.GetType());
            }
        }

        private static void WriteObject(StringBuilder sb, JsonObject obj, int level)
        {
            sb.Append('{');
            bool first = true;
            foreach (var member in obj)
            {
                sb.Append(first ? "" : ",");
                NewLine(sb, level + 1);
                WriteString(sb, member.Key);
                sb.Append(": ");
                Write(sb, member.Value, level + 1);
                first = false;
            }
            if (!first) NewLine(sb, level);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable items, int level)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in items)
            {
                sb.Append(first ? "" : ",");
                NewLine(sb, level + 1);
                Write(sb, item, level + 1);
                first = false;
            }
            if (!first) NewLine(sb, level);
            sb.Append(']');
        }

        private static void NewLine(StringBuilder sb, int level) => sb.Append('\n').Append(' ', level * 2);

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
