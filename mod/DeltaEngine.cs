using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Tracks the last-sent state per session and computes deltas so only changed
    /// data is transmitted on each broadcast. Reduces bandwidth when large parts of
    /// the game state (unrelated units, non-battle scenes) haven't changed.
    /// </summary>
    internal sealed class DeltaEngine
    {
        // Threshold: if more than this fraction of top-level keys changed, send a
        // full state snapshot instead of a delta to keep the delta format simple.
        private const double FullStateThreshold = 0.5;

        private readonly ConcurrentDictionary<string, SessionState> _sessions =
            new ConcurrentDictionary<string, SessionState>();

        private sealed class SessionState
        {
            // Monotonically increasing sequence number for this session's stream.
            public int Seq;

            // Raw JSON from the last send. Stored instead of parsing immediately so
            // the first message (which has nothing to diff against) can be sent without
            // any JSON scanning. Parsed lazily into LastFields on the next call.
            public string LastJson;

            // The last full JSON string sent to this session, keyed by top-level field.
            // Null until the second BuildMessage call (first actual delta computation).
            public Dictionary<string, string> LastFields;

            // Per-unit JSON strings for allies and enemies, keyed by unit id.
            public Dictionary<int, string> LastAllies;
            public Dictionary<int, string> LastEnemies;
        }

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Registers a new session. Must be called before the first
        /// <see cref="BuildMessage"/> for that session.
        /// </summary>
        public void AddSession(string sessionId)
        {
            _sessions[sessionId] = new SessionState
            {
                Seq = 0,
                LastFields = null,
                LastAllies = new Dictionary<int, string>(),
                LastEnemies = new Dictionary<int, string>(),
            };
        }

        /// <summary>Removes all tracking state for a session.</summary>
        public void RemoveSession(string sessionId)
        {
            _sessions.TryRemove(sessionId, out _);
        }

        /// <summary>
        /// Returns the next message to send to <paramref name="sessionId"/> given the
        /// current filtered state JSON. The message is either a full
        /// <c>{"type":"state",...}</c> or a smaller <c>{"type":"delta",...}</c>.
        /// </summary>
        public string BuildMessage(string sessionId, string filteredJson)
        {
            if (!_sessions.TryGetValue(sessionId, out var state))
                return WrapFull(1, filteredJson);

            state.Seq++;
            int seq = state.Seq;

            // First message: send the full state immediately without parsing anything —
            // there is no previous state to diff against. Store the raw JSON so the
            // next call can parse it as the baseline for delta computation.
            if (state.LastJson == null && state.LastFields == null)
            {
                state.LastJson = filteredJson;
                return WrapFull(seq, filteredJson);
            }

            // Second call onward: if we have an unparsed baseline, parse it now.
            if (state.LastFields == null)
            {
                state.LastFields = ParseTopLevelFields(state.LastJson);
                UpdateUnitCaches(state, state.LastFields);
                state.LastJson = null;
            }

            var newFields = ParseTopLevelFields(filteredJson);
            var delta = BuildDelta(state, newFields);

            state.LastFields = newFields;
            UpdateUnitCaches(state, newFields);

            if (delta == null)
                return null; // nothing changed — skip this broadcast

            return WrapDelta(seq, delta);
        }

        // -------------------------------------------------------------------------
        // JSON parsing helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Splits a flat JSON object into a dictionary of top-level key → raw value
        /// substring. Handles string, number, boolean, null, object, and array values.
        /// Operates on the raw JSON string to avoid a full parse.
        /// </summary>
        private static Dictionary<string, string> ParseTopLevelFields(string json)
        {
            var result = new Dictionary<string, string>();
            int i = SkipWhitespace(json, 0);
            if (i >= json.Length || json[i] != '{')
                return result;
            i++; // skip '{'

            while (true)
            {
                i = SkipWhitespace(json, i);
                if (i >= json.Length || json[i] == '}')
                    break;

                // Read key (quoted string)
                string key = ReadString(json, ref i);
                i = SkipWhitespace(json, i);
                if (i >= json.Length || json[i] != ':')
                    break;
                i++; // skip ':'
                i = SkipWhitespace(json, i);

                // Read value (any JSON value)
                int valueStart = i;
                SkipValue(json, ref i);
                string value = json.Substring(valueStart, i - valueStart).Trim();
                result[key] = value;

                i = SkipWhitespace(json, i);
                if (i < json.Length && json[i] == ',')
                    i++;
            }

            return result;
        }

        /// <summary>
        /// Parses the "allies" or "enemies" JSON array into a dictionary of unit id
        /// → raw unit object JSON. Used for per-unit change detection.
        /// </summary>
        private static Dictionary<int, string> ParseUnitArray(string arrayJson)
        {
            var result = new Dictionary<int, string>();
            if (string.IsNullOrEmpty(arrayJson) || arrayJson[0] != '[')
                return result;

            int i = 1; // skip '['
            while (true)
            {
                i = SkipWhitespace(arrayJson, i);
                if (i >= arrayJson.Length || arrayJson[i] == ']')
                    break;

                int start = i;
                SkipValue(arrayJson, ref i);
                string unitJson = arrayJson.Substring(start, i - start);

                // Extract "id" field from this unit object.
                int id = ExtractIntField(unitJson, "id");
                if (id >= 0)
                    result[id] = unitJson;

                i = SkipWhitespace(arrayJson, i);
                if (i < arrayJson.Length && arrayJson[i] == ',')
                    i++;
            }

            return result;
        }

        // -------------------------------------------------------------------------
        // Delta computation
        // -------------------------------------------------------------------------

        // Returns null if nothing changed, otherwise a raw JSON object string
        // containing only the changed fields (ready to embed in a delta envelope).
        private static string BuildDelta(SessionState state, Dictionary<string, string> newFields)
        {
            var sb = new StringBuilder("{");
            bool hasChanges = false;

            foreach (var kv in newFields)
            {
                string key = kv.Key;
                string newVal = kv.Value;

                if (key == "allies" || key == "enemies")
                    continue; // handled separately below

                if (!state.LastFields.TryGetValue(key, out string oldVal) || oldVal != newVal)
                {
                    if (hasChanges)
                        sb.Append(',');
                    AppendKeyValue(sb, key, newVal);
                    hasChanges = true;
                }
            }

            // Check for removed top-level keys (rare — e.g. abnormalitySelection disappearing).
            foreach (var key in state.LastFields.Keys)
            {
                if (key == "allies" || key == "enemies")
                    continue;
                if (!newFields.ContainsKey(key))
                {
                    if (hasChanges)
                        sb.Append(',');
                    AppendKeyValue(sb, key, "null");
                    hasChanges = true;
                }
            }

            // Diff unit arrays.
            bool alliesDiff = DiffUnitArray(
                state.LastAllies,
                newFields.TryGetValue("allies", out string newAlliesJson) ? newAlliesJson : null,
                sb,
                "allies",
                ref hasChanges
            );

            bool enemiesDiff = DiffUnitArray(
                state.LastEnemies,
                newFields.TryGetValue("enemies", out string newEnemiesJson) ? newEnemiesJson : null,
                sb,
                "enemies",
                ref hasChanges
            );

            if (!hasChanges)
                return null;

            sb.Append('}');
            return sb.ToString();
        }

        // Compares the last-known unit map against the new array JSON. Appends only
        // changed or new units to sb; appends a "_removed" array if any units left.
        // Returns true if any diff was written.
        private static bool DiffUnitArray(
            Dictionary<int, string> lastUnits,
            string newArrayJson,
            StringBuilder sb,
            string fieldName,
            ref bool hasChanges
        )
        {
            if (string.IsNullOrEmpty(newArrayJson) && lastUnits.Count == 0)
                return false;

            var newUnits = string.IsNullOrEmpty(newArrayJson)
                ? new Dictionary<int, string>()
                : ParseUnitArray(newArrayJson);

            var changed = new List<string>();
            var removed = new List<int>();

            foreach (var kv in newUnits)
            {
                if (!lastUnits.TryGetValue(kv.Key, out string oldJson) || oldJson != kv.Value)
                    changed.Add(kv.Value);
            }

            foreach (int id in lastUnits.Keys)
                if (!newUnits.ContainsKey(id))
                    removed.Add(id);

            if (changed.Count == 0 && removed.Count == 0)
                return false;

            if (hasChanges)
                sb.Append(',');
            hasChanges = true;

            // Changed/new units as a partial array.
            sb.Append('"').Append(fieldName).Append("\":[");
            for (int i = 0; i < changed.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(changed[i]);
            }
            sb.Append(']');

            // Signal removed units separately so the client can purge them.
            if (removed.Count > 0)
            {
                sb.Append(",\"_removed_").Append(fieldName).Append("\":[");
                for (int i = 0; i < removed.Count; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append(removed[i]);
                }
                sb.Append(']');
            }

            return true;
        }

        private static void UpdateUnitCaches(SessionState state, Dictionary<string, string> fields)
        {
            if (fields.TryGetValue("allies", out string alliesJson))
                state.LastAllies = ParseUnitArray(alliesJson);
            else
                state.LastAllies.Clear();

            if (fields.TryGetValue("enemies", out string enemiesJson))
                state.LastEnemies = ParseUnitArray(enemiesJson);
            else
                state.LastEnemies.Clear();
        }

        // -------------------------------------------------------------------------
        // Message envelope builders
        // -------------------------------------------------------------------------

        private static string WrapFull(int seq, string json) =>
            "{\"type\":\"state\",\"seq\":" + seq + ",\"data\":" + json + "}";

        private static string WrapDelta(int seq, string deltaJson) =>
            "{\"type\":\"delta\",\"seq\":" + seq + ",\"data\":" + deltaJson + "}";

        // -------------------------------------------------------------------------
        // Minimal JSON scanner (no allocations for the common path)
        // -------------------------------------------------------------------------

        private static int SkipWhitespace(string s, int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
                i++;
            return i;
        }

        private static string ReadString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"')
                return string.Empty;
            i++; // skip opening quote
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++; // skip backslash; append the escaped char literally
                }
                sb.Append(s[i++]);
            }
            if (i < s.Length)
                i++; // skip closing quote
            return sb.ToString();
        }

        // Advances i past one complete JSON value (any type).
        private static void SkipValue(string s, ref int i)
        {
            if (i >= s.Length)
                return;
            char c = s[i];
            if (c == '"')
            {
                SkipString(s, ref i);
                return;
            }
            if (c == '{')
            {
                SkipObject(s, ref i);
                return;
            }
            if (c == '[')
            {
                SkipArray(s, ref i);
                return;
            }
            // number, true, false, null — scan until delimiter
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && s[i] != ' ')
                i++;
        }

        private static void SkipString(string s, ref int i)
        {
            i++; // opening quote
            while (i < s.Length)
            {
                if (s[i] == '\\')
                {
                    i += 2;
                    continue;
                }
                if (s[i] == '"')
                {
                    i++;
                    return;
                }
                i++;
            }
        }

        private static void SkipObject(string s, ref int i)
        {
            i++; // '{'
            int depth = 1;
            while (i < s.Length && depth > 0)
            {
                if (s[i] == '"')
                    SkipString(s, ref i);
                else if (s[i] == '{')
                {
                    depth++;
                    i++;
                }
                else if (s[i] == '}')
                {
                    depth--;
                    i++;
                }
                else
                    i++;
            }
        }

        private static void SkipArray(string s, ref int i)
        {
            i++; // '['
            int depth = 1;
            while (i < s.Length && depth > 0)
            {
                if (s[i] == '"')
                    SkipString(s, ref i);
                else if (s[i] == '[')
                {
                    depth++;
                    i++;
                }
                else if (s[i] == ']')
                {
                    depth--;
                    i++;
                }
                else
                    i++;
            }
        }

        private static int ExtractIntField(string json, string fieldName)
        {
            string needle = "\"" + fieldName + "\":";
            int pos = json.IndexOf(needle);
            if (pos < 0)
                return -1;
            pos += needle.Length;
            pos = SkipWhitespace(json, pos);
            int end = pos;
            while (end < json.Length && char.IsDigit(json[end]))
                end++;
            if (end == pos)
                return -1;
            return int.TryParse(json.Substring(pos, end - pos), out int val) ? val : -1;
        }

        private static void AppendKeyValue(StringBuilder sb, string key, string rawValue)
        {
            sb.Append('"').Append(key).Append("\":").Append(rawValue);
        }
    }
}
