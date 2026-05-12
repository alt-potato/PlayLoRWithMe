using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using CustomSpeedDiceXML;
using LOR_DiceSystem;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Soft-dependency bridge to the CustomSpeedDiceColor workshop mod
    /// (Patty_SpeedDiceColor_MOD, id 2746914901). When the mod is loaded,
    /// per-book speed-die colour overrides flow through to the web UI as an
    /// optional <c>dieColor</c> field on each unit's payload.
    ///
    /// The mod is referenced at compile time via HintPath in the csproj
    /// (Private=False so we don't bundle it). The runtime soft-dep contract
    /// is enforced by the <see cref="HasCdc"/> gate plus <c>[MethodImpl(NoInlining)]</c>
    /// on <see cref="LookupColor"/>: the CLR resolves CDC types lazily, only
    /// when LookupColor's body is JIT-compiled, which only happens once HasCdc
    /// has gated us through. Mod stays loadable when the subscription is absent.
    /// </summary>
    internal static class CustomDiceColorProbe
    {
        // Cached once per session — AppDomain.GetAssemblies enumeration is O(n)
        // and stable for the lifetime of the process. Nullable = not yet checked.
        private static bool? _hasCdc;

        private static bool HasCdc
        {
            get
            {
                if (!_hasCdc.HasValue)
                {
                    _hasCdc = AppDomain.CurrentDomain.GetAssemblies()
                        .Any(a => a.GetName().Name == "Patty_SpeedDiceColor_MOD");
                }
                return _hasCdc.Value;
            }
        }

        /// <summary>
        /// Returns the per-unit speed-die colour override hex if CDC is loaded
        /// and one of its <c>SpeedDice</c> entries matches the unit (faction +
        /// either current book id or default book id). Returns null when CDC
        /// is absent or no entry matches.
        /// </summary>
        internal static string TryGet(BattleUnitModel unit)
        {
            if (!HasCdc) return null;
            return LookupColor(unit);
        }

        // Separate method, intentionally not inlined: CDC type references live
        // here, so the CLR only resolves SpeedDiceManager / SpeedDice when this
        // method's body is JIT'd. HasCdc gates the call site, so JIT never
        // touches this method when the CDC assembly is absent.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string LookupColor(BattleUnitModel unit)
        {
            if (unit == null) return null;
            var list = Singleton<SpeedDiceManager>.Instance?.speedDicesList;
            if (list == null) return null;

            var book = unit.Book?.BookId;
            var defaultBook = unit.UnitData?.unitData?.defaultBook?.BookId;

            foreach (var entry in list)
            {
                if (entry == null) continue;
                if (entry.Faction != unit.faction) continue;

                bool match =
                    (book != null
                        && entry.BookID == book.id
                        && entry.BookUniqueID == book.packageId)
                    || (defaultBook != null
                        && entry.DefaultBookID == defaultBook.id
                        && entry.DefaultBookUniqueID == defaultBook.packageId);
                if (!match) continue;

                // entry.Color is only populated by SpeedDiceManager.LoadFromString;
                // CDC's own initializer uses LoadFromFolder which skips that step,
                // so the Color32 field is (0,0,0,0) for folder-loaded entries.
                // DiceColor (the XML-loaded RGBA) is always correct.
                var c = entry.DiceColor;
                return "#"
                    + c.R.ToString("x2", CultureInfo.InvariantCulture)
                    + c.G.ToString("x2", CultureInfo.InvariantCulture)
                    + c.B.ToString("x2", CultureInfo.InvariantCulture);
            }
            return null;
        }
    }
}
