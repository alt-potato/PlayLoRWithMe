using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using LOR_DiceSystem;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Soft-dependency reflection probe for the Patty_SpeedDiceColor_MOD
    /// workshop mod (id 2746914901). When loaded, per-book speed-die colour
    /// overrides flow through to the web UI as an optional <c>dieColor</c>
    /// field on each unit's payload; otherwise <see cref="TryGet"/> returns
    /// null and units render with the vanilla per-faction defaults.
    ///
    /// Match semantics mirror the CDC mod's own SpeedDiceUI.Init postfix:
    /// faction must match, and either (BookID + BookUniqueID) or
    /// (DefaultBookID + DefaultBookUniqueID) must match the unit's books.
    /// </summary>
    internal static class CustomDiceColorProbe
    {
        // cached reflection plumbing — bound once at init, accessed per-lookup
        private static IEnumerable _speedDicesList;
        private static FieldInfo _factionField;
        private static FieldInfo _bookIdField;
        private static FieldInfo _bookUniqueIdField;
        private static FieldInfo _defaultBookIdField;
        private static FieldInfo _defaultBookUniqueIdField;
        private static FieldInfo _colorField;
        private static bool _giveUp;

        /// <summary>True after a successful bind; further probes are no-ops.</summary>
        internal static bool IsReady => _speedDicesList != null;

        /// <summary>
        /// Idempotent. Resolves the CDC singleton's SpeedDice list and the
        /// field accessors needed for per-unit lookups. Best-effort: missing
        /// type / field returns silently and a single warning is logged so
        /// subsequent state pushes don't retry.
        /// </summary>
        internal static void TryProbe()
        {
            if (IsReady || _giveUp)
                return;

            try
            {
                var managerType = Type.GetType(
                    "CustomSpeedDiceXML.SpeedDiceManager, Patty_SpeedDiceColor_MOD"
                );
                if (managerType == null)
                {
                    // CDC mod isn't installed — this is the common case and not a
                    // failure. Mark as given-up so future state pushes skip the probe.
                    _giveUp = true;
                    return;
                }

                var singletonType = typeof(Singleton<>).MakeGenericType(managerType);
                var instanceProp = singletonType.GetProperty(
                    "Instance",
                    BindingFlags.Public | BindingFlags.Static
                );
                var manager = instanceProp?.GetValue(null);
                if (manager == null)
                    return; // singleton not yet initialised — retry later

                var listField = managerType.GetField("speedDicesList");
                if (listField == null)
                {
                    _giveUp = true;
                    Debug.LogWarning("[CustomDiceColorProbe] SpeedDiceManager.speedDicesList field not found; CDC overrides disabled.");
                    return;
                }
                var list = listField.GetValue(manager) as IEnumerable;
                if (list == null)
                    return;

                var speedDiceType = Type.GetType(
                    "CustomSpeedDiceXML.SpeedDice, Patty_SpeedDiceColor_MOD"
                );
                if (speedDiceType == null)
                {
                    _giveUp = true;
                    Debug.LogWarning("[CustomDiceColorProbe] SpeedDice type not found via reflection; CDC overrides disabled.");
                    return;
                }

                _factionField = speedDiceType.GetField("Faction");
                _bookIdField = speedDiceType.GetField("BookID");
                _bookUniqueIdField = speedDiceType.GetField("BookUniqueID");
                _defaultBookIdField = speedDiceType.GetField("DefaultBookID");
                _defaultBookUniqueIdField = speedDiceType.GetField("DefaultBookUniqueID");
                _colorField = speedDiceType.GetField("Color");

                if (
                    _factionField == null
                    || _bookIdField == null
                    || _bookUniqueIdField == null
                    || _defaultBookIdField == null
                    || _defaultBookUniqueIdField == null
                    || _colorField == null
                )
                {
                    _giveUp = true;
                    Debug.LogWarning("[CustomDiceColorProbe] SpeedDice fields missing; CDC overrides disabled.");
                    return;
                }

                _speedDicesList = list;
            }
            catch (Exception ex)
            {
                _giveUp = true;
                Debug.LogWarning("[CustomDiceColorProbe] reflection failed: " + ex);
            }
        }

        /// <summary>
        /// Looks up the per-unit override colour. Returns null when CDC isn't
        /// loaded or no entry matches. The unit's faction is compared against
        /// each CDC entry's faction; the book / default-book IDs are matched
        /// the same way the CDC mod's own SpeedDiceUI.Init postfix matches.
        /// </summary>
        internal static string TryGet(BattleUnitModel unit)
        {
            if (_speedDicesList == null || unit == null)
                return null;

            var book = unit.Book?.BookId;
            var defaultBook = unit.UnitData?.unitData?.defaultBook?.BookId;

            foreach (var entry in _speedDicesList)
            {
                if (entry == null)
                    continue;
                var entryFaction = (Faction)_factionField.GetValue(entry);
                if (entryFaction != unit.faction)
                    continue;

                var entryBookId = (int)_bookIdField.GetValue(entry);
                var entryBookUid = (string)_bookUniqueIdField.GetValue(entry);
                var entryDefBookId = (int)_defaultBookIdField.GetValue(entry);
                var entryDefBookUid = (string)_defaultBookUniqueIdField.GetValue(entry);

                bool match =
                    (book != null && entryBookId == book.id && entryBookUid == book.packageId)
                    || (
                        defaultBook != null
                        && entryDefBookId == defaultBook.id
                        && entryDefBookUid == defaultBook.packageId
                    );
                if (!match)
                    continue;

                var color = (Color32)_colorField.GetValue(entry);
                return Color32ToHex(color);
            }
            return null;
        }

        private static string Color32ToHex(Color32 c)
        {
            return "#"
                + c.r.ToString("x2", CultureInfo.InvariantCulture)
                + c.g.ToString("x2", CultureInfo.InvariantCulture)
                + c.b.ToString("x2", CultureInfo.InvariantCulture);
        }
    }
}
