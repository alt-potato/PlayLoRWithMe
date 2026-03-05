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
        // Matches "key":"string-value" or "key":integer
        private static readonly Regex _pattern = new Regex(
            "\"(\\w+)\"\\s*:\\s*(?:\"([^\"]*)\"|(-?\\d+))",
            RegexOptions.Compiled);

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
            _fields.TryGetValue(key, out string val);
            return val;
        }

        /// <summary>Returns true and sets <paramref name="val"/> if the key exists and is a valid integer.</summary>
        public bool TryGetInt(string key, out int val)
        {
            val = 0;
            return _fields.TryGetValue(key, out string s) && int.TryParse(s, out val);
        }
    }
}
