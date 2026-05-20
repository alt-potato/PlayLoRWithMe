using System;
using System.Text;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Minimal JSON object builder with proper string escaping.
    /// </summary>
    /// <remarks>
    /// Nested objects/arrays append directly into the root writer's
    /// <see cref="StringBuilder"/> rather than each allocating their own builder and
    /// round-tripping through <c>ToString()</c>. A full game-state snapshot nests
    /// floors -> units -> cards -> dice -> buffs, so sharing one builder avoids
    /// hundreds of intermediate string allocations per broadcast.
    /// </remarks>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _sb;
        private bool _hasEntry;
        private bool _closed;

        /// <summary>Creates a root writer backed by a fresh builder.</summary>
        public JsonWriter()
        {
            _sb = new StringBuilder();
            _sb.Append('{');
        }

        /// <summary>Creates a nested writer that appends into a shared builder.</summary>
        internal JsonWriter(StringBuilder shared)
        {
            _sb = shared;
            _sb.Append('{');
        }

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
            var nested = new JsonWriter(_sb);
            build(nested);
            nested.Close();
            return this;
        }

        public JsonWriter AddArray(string key, Action<JsonArrayWriter> build)
        {
            Key(key);
            var arr = new JsonArrayWriter(_sb);
            build(arr);
            arr.Close();
            return this;
        }

        /// <summary>Appends the closing brace into the shared builder exactly once.</summary>
        internal void Close()
        {
            if (_closed)
                return;
            _closed = true;
            _sb.Append('}');
        }

        /// <summary>Closes the object and returns the complete JSON string.</summary>
        public string Build()
        {
            Close();
            return _sb.ToString();
        }
    }

    /// <summary>
    /// Minimal JSON array builder. Always backed by a parent writer's shared
    /// <see cref="StringBuilder"/>; created only via <see cref="JsonWriter.AddArray"/>.
    /// </summary>
    public sealed class JsonArrayWriter
    {
        private readonly StringBuilder _sb;
        private bool _hasEntry;
        private bool _closed;

        internal JsonArrayWriter(StringBuilder shared)
        {
            _sb = shared;
            _sb.Append('[');
        }

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
            var w = new JsonWriter(_sb);
            build(w);
            w.Close();
            return this;
        }

        public JsonArrayWriter AddNull()
        {
            Comma();
            _sb.Append("null");
            return this;
        }

        /// <summary>Appends the closing bracket into the shared builder exactly once.</summary>
        internal void Close()
        {
            if (_closed)
                return;
            _closed = true;
            _sb.Append(']');
        }
    }
}
