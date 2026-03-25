using System;
using System.Text;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Minimal JSON object builder with proper string escaping.
    /// </summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder("{");
        private bool _hasEntry;

        private JsonWriter Comma()
        {
            if (_hasEntry)
                _sb.Append(',');
            _hasEntry = true;
            return this;
        }

        internal static void AppendEscaped(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private JsonWriter Key(string key)
        {
            Comma();
            AppendEscaped(_sb, key);
            _sb.Append(':');
            return this;
        }

        public JsonWriter Add(string key, string value)
        {
            Key(key);
            if (value == null)
                _sb.Append("null");
            else
                AppendEscaped(_sb, value);
            return this;
        }

        public JsonWriter Add(string key, int value)
        {
            Key(key);
            _sb.Append(value);
            return this;
        }

        public JsonWriter Add(string key, bool value)
        {
            Key(key);
            _sb.Append(value ? "true" : "false");
            return this;
        }

        /// <summary>Writes an unquoted JSON number, using invariant culture to avoid locale decimal separators.</summary>
        public JsonWriter Add(string key, float value)
        {
            Key(key);
            _sb.Append(value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter AddObject(string key, Action<JsonWriter> build)
        {
            Key(key);
            var nested = new JsonWriter();
            build(nested);
            _sb.Append(nested.Build());
            return this;
        }

        public JsonWriter AddArray(string key, Action<JsonArrayWriter> build)
        {
            Key(key);
            var arr = new JsonArrayWriter();
            build(arr);
            _sb.Append(arr.Build());
            return this;
        }

        public string Build() => _sb.ToString() + '}';
    }

    /// <summary>
    /// Minimal JSON array builder.
    /// </summary>
    public sealed class JsonArrayWriter
    {
        private readonly StringBuilder _sb = new StringBuilder("[");
        private bool _hasEntry;

        private JsonArrayWriter Comma()
        {
            if (_hasEntry)
                _sb.Append(',');
            _hasEntry = true;
            return this;
        }

        public JsonArrayWriter AddString(string value)
        {
            Comma();
            if (value == null)
                _sb.Append("null");
            else
                JsonWriter.AppendEscaped(_sb, value);
            return this;
        }

        public JsonArrayWriter AddInt(int value)
        {
            Comma();
            _sb.Append(value);
            return this;
        }

        public JsonArrayWriter AddObject(Action<JsonWriter> build)
        {
            Comma();
            var w = new JsonWriter();
            build(w);
            _sb.Append(w.Build());
            return this;
        }

        public string Build() => _sb.ToString() + ']';
    }
}
