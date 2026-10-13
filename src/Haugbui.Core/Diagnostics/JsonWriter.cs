using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Haugbui.Core.Diagnostics
{
    /// <summary>Minimal streaming JSON writer for debug dumps; no dependency on game or third-party serializers.</summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Stack<bool> _needsComma = new Stack<bool>();
        private bool _afterName;

        public JsonWriter BeginObject() => Open('{');
        public JsonWriter EndObject() => Close('}');
        public JsonWriter BeginArray() => Open('[');
        public JsonWriter EndArray() => Close(']');

        public JsonWriter Name(string name)
        {
            Separator();
            WriteString(name);
            _sb.Append(':');
            _afterName = true;
            return this;
        }

        public JsonWriter Value(string value)
        {
            Separator();
            if (value == null) _sb.Append("null");
            else WriteString(value);
            return this;
        }

        public JsonWriter Value(bool value)
        {
            Separator();
            _sb.Append(value ? "true" : "false");
            return this;
        }

        public JsonWriter Value(long value)
        {
            Separator();
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Value(double value)
        {
            Separator();
            if (double.IsNaN(value) || double.IsInfinity(value)) _sb.Append("null");
            else _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Property(string name, string value) => Name(name).Value(value);
        public JsonWriter Property(string name, bool value) => Name(name).Value(value);
        public JsonWriter Property(string name, long value) => Name(name).Value(value);
        public JsonWriter Property(string name, double value) => Name(name).Value(value);

        public override string ToString()
        {
            if (_needsComma.Count != 0) throw new InvalidOperationException("Unclosed object or array");
            return _sb.ToString();
        }

        private JsonWriter Open(char c)
        {
            Separator();
            _sb.Append(c);
            _needsComma.Push(false);
            return this;
        }

        private JsonWriter Close(char c)
        {
            if (_needsComma.Count == 0) throw new InvalidOperationException("Nothing to close");
            _needsComma.Pop();
            _sb.Append(c);
            return this;
        }

        private void Separator()
        {
            if (_afterName)
            {
                _afterName = false;
                return;
            }
            if (_needsComma.Count == 0) return;
            if (_needsComma.Peek()) _sb.Append(',');
            _needsComma.Pop();
            _needsComma.Push(true);
        }

        private void WriteString(string value)
        {
            _sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
