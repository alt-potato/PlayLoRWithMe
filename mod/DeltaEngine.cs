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
            // Guards all mutable fields below. BuildMessage reads and writes these
            // fields, which can be called concurrently for the same session (e.g.
            // a resync racing with a normal broadcast).
            public readonly object Lock = new object();

            // Monotonically increasing sequence number for this session's stream.
            public int Seq;

            // Raw JSON from the last send. Stored instead of parsing immediately so
            // the first message (which has nothing to diff against) can be sent without
            // any JSON scanning. Parsed lazily into LastFields on the next call.
            public string LastJson;

            // Whether LastFields has been populated (replaces the null check now that
            // we reuse dictionaries instead of allocating new ones).
            public bool HasLastFields;

            // Double-buffered top-level field dictionaries. One holds last-sent data
            // while the other is cleared and filled with current data; they swap each
            // cycle to avoid allocating new dictionaries on every broadcast.
            public Dictionary<string, string> FieldsA = new Dictionary<string, string>();
            public Dictionary<string, string> FieldsB = new Dictionary<string, string>();

            // Which buffer currently holds "last" data. When true, FieldsA is last
            // and FieldsB is the scratch buffer (and vice versa).
            public bool FieldsAIsLast;

            // Double-buffered unit dictionaries for allies and enemies.
            public Dictionary<int, string> AlliesA = new Dictionary<int, string>();
            public Dictionary<int, string> AlliesB = new Dictionary<int, string>();
            public bool AlliesAIsLast;

            public Dictionary<int, string> EnemiesA = new Dictionary<int, string>();
            public Dictionary<int, string> EnemiesB = new Dictionary<int, string>();
            public bool EnemiesAIsLast;

            // Reusable scratch collections for DiffUnitArray to avoid per-call allocations.
            public readonly List<string> ScratchChanged = new List<string>();
            public readonly List<int> ScratchRemoved = new List<int>();

            // Reusable StringBuilder for BuildDelta.
            public readonly StringBuilder DeltaBuilder = new StringBuilder(256);

            /// <summary>Returns the buffer currently holding last-sent top-level fields.</summary>
            public Dictionary<string, string> LastFields =>
                FieldsAIsLast ? FieldsA : FieldsB;

            /// <summary>Returns the scratch buffer for populating new top-level fields.</summary>
            public Dictionary<string, string> NewFieldsScratch =>
                FieldsAIsLast ? FieldsB : FieldsA;

            /// <summary>Swaps the field buffers so the scratch becomes "last".</summary>
            public void SwapFields() => FieldsAIsLast = !FieldsAIsLast;

            public Dictionary<int, string> LastAllies =>
                AlliesAIsLast ? AlliesA : AlliesB;

            public Dictionary<int, string> NewAlliesScratch =>
                AlliesAIsLast ? AlliesB : AlliesA;

            public void SwapAllies() => AlliesAIsLast = !AlliesAIsLast;

            public Dictionary<int, string> LastEnemies =>
                EnemiesAIsLast ? EnemiesA : EnemiesB;

            public Dictionary<int, string> NewEnemiesScratch =>
                EnemiesAIsLast ? EnemiesB : EnemiesA;

            public void SwapEnemies() => EnemiesAIsLast = !EnemiesAIsLast;
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
            _sessions[sessionId] = new SessionState { Seq = 0 };
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

            lock (state.Lock)
            {
                state.Seq++;
                int seq = state.Seq;

                // First message: send the full state immediately without parsing anything —
                // there is no previous state to diff against. Store the raw JSON so the
                // next call can parse it as the baseline for delta computation.
                if (state.LastJson == null && !state.HasLastFields)
                {
                    state.LastJson = filteredJson;
                    return WrapFull(seq, filteredJson);
                }

                // Second call onward: if we have an unparsed baseline, parse it now
                // into the current "last" buffer.
                if (!state.HasLastFields)
                {
                    var lastBuf = state.LastFields;
                    lastBuf.Clear();
                    ParseTopLevelFields(state.LastJson, lastBuf);
                    UpdateUnitCaches(state, lastBuf);
                    state.HasLastFields = true;
                    state.LastJson = null;
                }

                // Parse new state into the scratch buffer, then diff against last.
                var newFields = state.NewFieldsScratch;
                newFields.Clear();
                ParseTopLevelFields(filteredJson, newFields);

                var delta = BuildDelta(state, newFields);

                // Swap so the scratch (now holding new data) becomes "last".
                state.SwapFields();
                UpdateUnitCaches(state, state.LastFields);

                if (delta == null)
                    return null; // nothing changed — skip this broadcast

                return WrapDelta(seq, delta);
            }
        }

        // -------------------------------------------------------------------------
        // JSON parsing helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Splits a flat JSON object into a dictionary of top-level key → raw value
        /// substring. Handles string, number, boolean, null, object, and array values.
        /// Operates on the raw JSON string to avoid a full parse.
        /// The caller must clear <paramref name="result"/> beforehand.
        /// </summary>
        private static void ParseTopLevelFields(string json, Dictionary<string, string> result)
        {
            int i = SkipWhitespace(json, 0);
            if (i >= json.Length || json[i] != '{')
                return;
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
        }

        /// <summary>
        /// Parses the "allies" or "enemies" JSON array into a dictionary of unit id
        /// → raw unit object JSON. Used for per-unit change detection.
        /// The caller must clear <paramref name="result"/> beforehand.
        /// </summary>
        private static void ParseUnitArray(string arrayJson, Dictionary<int, string> result)
        {
            if (string.IsNullOrEmpty(arrayJson) || arrayJson[0] != '[')
                return;

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
        }

        // -------------------------------------------------------------------------
        // Delta computation
        // -------------------------------------------------------------------------

        // Returns null if nothing changed, otherwise a raw JSON object string
        // containing only the changed fields (ready to embed in a delta envelope).
        private static string BuildDelta(SessionState state, Dictionary<string, string> newFields)
        {
            var sb = state.DeltaBuilder;
            sb.Clear();
            sb.Append('{');
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

            // Diff unit arrays using double-buffered dictionaries and scratch lists.
            DiffUnitArray(
                state.LastAllies,
                state.NewAlliesScratch,
                newFields.TryGetValue("allies", out string newAlliesJson) ? newAlliesJson : null,
                sb,
                "allies",
                state.ScratchChanged,
                state.ScratchRemoved,
                ref hasChanges
            );

            DiffUnitArray(
                state.LastEnemies,
                state.NewEnemiesScratch,
                newFields.TryGetValue("enemies", out string newEnemiesJson) ? newEnemiesJson : null,
                sb,
                "enemies",
                state.ScratchChanged,
                state.ScratchRemoved,
                ref hasChanges
            );

            if (!hasChanges)
                return null;

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Compares the last-known unit map against the new array JSON. Parses the new
        /// array into <paramref name="newUnitsScratch"/> (which the caller will swap into
        /// the "last" position after this returns). Appends only changed or new units to
        /// sb; appends a "_removed" array if any units disappeared.
        /// </summary>
        private static bool DiffUnitArray(
            Dictionary<int, string> lastUnits,
            Dictionary<int, string> newUnitsScratch,
            string newArrayJson,
            StringBuilder sb,
            string fieldName,
            List<string> changedScratch,
            List<int> removedScratch,
            ref bool hasChanges
        )
        {
            newUnitsScratch.Clear();
            if (!string.IsNullOrEmpty(newArrayJson))
                ParseUnitArray(newArrayJson, newUnitsScratch);

            if (newUnitsScratch.Count == 0 && lastUnits.Count == 0)
                return false;

            changedScratch.Clear();
            removedScratch.Clear();

            foreach (var kv in newUnitsScratch)
            {
                if (!lastUnits.TryGetValue(kv.Key, out string oldJson) || oldJson != kv.Value)
                    changedScratch.Add(kv.Value);
            }

            foreach (int id in lastUnits.Keys)
                if (!newUnitsScratch.ContainsKey(id))
                    removedScratch.Add(id);

            if (changedScratch.Count == 0 && removedScratch.Count == 0)
                return false;

            if (hasChanges)
                sb.Append(',');
            hasChanges = true;

            // Changed/new units as a partial array.
            sb.Append('"').Append(fieldName).Append("\":[");
            for (int i = 0; i < changedScratch.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(changedScratch[i]);
            }
            sb.Append(']');

            // Signal removed units separately so the client can purge them.
            if (removedScratch.Count > 0)
            {
                sb.Append(",\"_removed_").Append(fieldName).Append("\":[");
                for (int i = 0; i < removedScratch.Count; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append(removedScratch[i]);
                }
                sb.Append(']');
            }

            return true;
        }

        /// <summary>
        /// Swaps the unit double-buffers so DiffUnitArray's scratch results become
        /// the new "last" snapshots. When called on the initial baseline (before any
        /// DiffUnitArray has run), parses into the current last buffer directly.
        /// </summary>
        private static void UpdateUnitCaches(SessionState state, Dictionary<string, string> fields)
        {
            if (!state.HasLastFields)
            {
                // Initial baseline — parse directly into the current "last" buffers.
                var allies = state.LastAllies;
                allies.Clear();
                if (fields.TryGetValue("allies", out string aJson))
                    ParseUnitArray(aJson, allies);

                var enemies = state.LastEnemies;
                enemies.Clear();
                if (fields.TryGetValue("enemies", out string eJson))
                    ParseUnitArray(eJson, enemies);
                return;
            }

            // DiffUnitArray already populated the scratch buffers; swap them in.
            // If the field was absent, the scratch buffer was cleared by DiffUnitArray.
            state.SwapAllies();
            state.SwapEnemies();
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
