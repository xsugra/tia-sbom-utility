using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TiaSbom.Core
{
    /// <summary>
    /// Minimal JSON parser. JavaScriptSerializer cannot be used: System.Web.Extensions rejects partial-trust callers,
    /// so it fails in the V17 Add-In sandbox.
    /// Objects become Dictionary&lt;string, object&gt; (keys ignore case), arrays List&lt;object&gt;, numbers double.
    /// </summary>
    internal sealed class JsonReader
    {
        private readonly string _text;
        private int _position;

        public JsonReader(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            _text = text;
        }

        public object ReadDocument()
        {
            object value = ReadValue();
            SkipWhitespace();
            if (_position != _text.Length) throw Error("unexpected text after the value");
            return value;
        }

        private object ReadValue()
        {
            SkipWhitespace();
            if (_position >= _text.Length) throw Error("unexpected end");

            switch (_text[_position])
            {
                case '{': return ReadObject();
                case '[': return ReadArray();
                case '"': return ReadString();
                case 't': return ReadLiteral("true", true);
                case 'f': return ReadLiteral("false", false);
                case 'n': return ReadLiteral("null", null);
                default: return ReadNumber();
            }
        }

        private Dictionary<string, object> ReadObject()
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _position++;
            if (TryConsume('}')) return result;

            while (true)
            {
                SkipWhitespace();
                if (_position >= _text.Length || _text[_position] != '"') throw Error("expected a property name");
                string name = ReadString();
                Expect(':');
                result[name] = ReadValue();
                if (TryConsume('}')) return result;
                Expect(',');
            }
        }

        private List<object> ReadArray()
        {
            var result = new List<object>();
            _position++;
            if (TryConsume(']')) return result;

            while (true)
            {
                result.Add(ReadValue());
                if (TryConsume(']')) return result;
                Expect(',');
            }
        }

        private string ReadString()
        {
            var result = new StringBuilder();
            _position++;
            while (true)
            {
                if (_position >= _text.Length) throw Error("unterminated string");
                char c = _text[_position++];
                if (c == '"') return result.ToString();
                result.Append(c == '\\' ? ReadEscape() : c);
            }
        }

        private char ReadEscape()
        {
            if (_position >= _text.Length) throw Error("unterminated escape");
            char c = _text[_position++];
            switch (c)
            {
                case '"':
                case '\\':
                case '/': return c;
                case 'b': return '\b';
                case 'f': return '\f';
                case 'n': return '\n';
                case 'r': return '\r';
                case 't': return '\t';
                case 'u':
                    if (_position + 4 > _text.Length) throw Error("unterminated escape");
                    string hex = _text.Substring(_position, 4);
                    _position += 4;
                    int code;
                    if (!int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                        throw Error("invalid escape");
                    return (char)code;
                default: throw Error("invalid escape");
            }
        }

        private object ReadLiteral(string literal, object value)
        {
            if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0) throw Error("invalid value");
            _position += literal.Length;
            return value;
        }

        private object ReadNumber()
        {
            int start = _position;
            while (_position < _text.Length && "+-.eE0123456789".IndexOf(_text[_position]) >= 0) _position++;

            double value;
            if (!double.TryParse(_text.Substring(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw Error("invalid value");
            return value;
        }

        private void Expect(char expected)
        {
            if (!TryConsume(expected)) throw Error("expected '" + expected + "'");
        }

        private bool TryConsume(char expected)
        {
            SkipWhitespace();
            if (_position >= _text.Length || _text[_position] != expected) return false;
            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
        }

        private FormatException Error(string problem)
        {
            return new FormatException("Invalid JSON at position " + _position + ": " + problem + ".");
        }
    }
}
