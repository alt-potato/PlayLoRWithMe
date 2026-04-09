using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Minimal flat JSON object reader.  Parses string and integer scalar values
    /// from a single-level JSON object — sufficient for the action payloads used
    /// by this mod.
    ///
    /// Example input: {"type":"playCard","unitId":3,"cardIndex":0,"diceSlot":0}
    /// </summary>
    public sealed class JsonReader
    {
        // Matches "key":"string-value" (with escaped quotes) or "key":integer
        private static readonly Regex _pattern = new Regex(
            "\"(\\w+)\"\\s*:\\s*(?:\"((?:[^\"\\\\]|\\\\.)*)\"\\s*|(-?\\d+))",
            RegexOptions.Compiled
        );

        private readonly Dictionary<string, string> _fields;

        public JsonReader(string json)
        {
            _fields = new Dictionary<string, string>();
            foreach (Match m in _pattern.Matches(json ?? ""))
            {
                string key = m.Groups[1].Value;
                string val = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                _fields[key] = val;
            }
        }

        /// <summary>Returns the string value for the given key, or null if absent.</summary>
        public string GetString(string key)
        {
            if (!_fields.TryGetValue(key, out string val))
                return null;
            return Unescape(val);
        }

        /// <summary>Resolves standard JSON backslash escape sequences.</summary>
        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0)
                return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char next = s[++i];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'n':  sb.Append('\n'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'r':  sb.Append('\r'); break;
                        default:   sb.Append('\\').Append(next); break;
                    }
                }
                else
                    sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>Returns true and sets <paramref name="val"/> if the key exists and is a valid integer.</summary>
        public bool TryGetInt(string key, out int val)
        {
            val = 0;
            return _fields.TryGetValue(key, out string s) && int.TryParse(s, out val);
        }
    }
}
