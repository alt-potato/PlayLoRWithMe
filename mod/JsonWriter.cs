using System.Text;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Minimal JSON object builder. Handles string escaping and nested objects.
    /// Usage:
    ///   new JsonWriter()
    ///       .Add("scene", "battle")
    ///       .Add("round", 3)
    ///       .Add("active", true)
    ///       .AddObject("unit", u => u.Add("hp", 10))
    ///       .Build()
    /// </summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder("{");
        private bool _hasEntry = false;

        private JsonWriter Comma()
        {
            if (_hasEntry)
                _sb.Append(',');
            _hasEntry = true;
            return this;
        }

        private static void AppendEscaped(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
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

        public JsonWriter Add(string key, string value)
        {
            Comma();
            AppendEscaped(_sb, key);
            _sb.Append(':');
            if (value == null)
                _sb.Append("null");
            else
                AppendEscaped(_sb, value);
            return this;
        }

        public JsonWriter Add(string key, int value)
        {
            Comma();
            AppendEscaped(_sb, key);
            _sb.Append(':');
            _sb.Append(value);
            return this;
        }

        public JsonWriter Add(string key, bool value)
        {
            Comma();
            AppendEscaped(_sb, key);
            _sb.Append(':');
            _sb.Append(value ? "true" : "false");
            return this;
        }

        public JsonWriter AddObject(string key, System.Action<JsonWriter> build)
        {
            Comma();
            AppendEscaped(_sb, key);
            _sb.Append(':');
            var nested = new JsonWriter();
            build(nested);
            _sb.Append(nested.Build());
            return this;
        }

        public string Build()
        {
            return _sb.ToString() + '}';
        }
    }
}
