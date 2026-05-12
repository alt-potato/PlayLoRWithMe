using System.Globalization;
using System.Reflection;
using LOR_BattleUnit_UI;
using UnityEngine;
using UnityEngine.UI;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Samples the live colour of each unit's first <see cref="SpeedDiceUI"/>
    /// so per-unit tints applied by any colour-modifying mod
    /// (e.g. CustomSpeedDiceColor / Patty_SpeedDiceColor_MOD) flow through to
    /// the web UI as an optional <c>dieColor</c> payload field.
    ///
    /// This sits where an XML-list lookup used to: in practice CDC's per-unit
    /// colours come from a long hardcoded fallback in its SpeedDiceUI.Init
    /// postfix, not from its (often empty) speedDicesList. Sampling the live
    /// post-Init colour captures whatever upstream applies, with the bonus of
    /// covering any other speed-die tint mod the player has installed.
    /// </summary>
    internal static class CustomDiceColorProbe
    {
        // SpeedDiceUI._rouletteImg is private; bind via reflection once.
        private static FieldInfo _rouletteImgField;
        private static bool _bindAttempted;
        private static bool _bindFailed;

        private static void TryBindOnce()
        {
            if (_bindAttempted) return;
            _bindAttempted = true;
            _rouletteImgField = typeof(SpeedDiceUI).GetField(
                "_rouletteImg",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (_rouletteImgField == null)
            {
                _bindFailed = true;
                Debug.LogWarning("[CustomDiceColorProbe] SpeedDiceUI._rouletteImg field not found via reflection; per-unit colour sampling disabled.");
                return;
            }
            Debug.Log("[CustomDiceColorProbe] live-sample probe bound; per-unit colours will sample SpeedDiceUI._rouletteImg.color after init.");
        }

        /// <summary>
        /// Returns the unit's current speed-die fill colour as a <c>#rrggbb</c>
        /// hex string, or null when the unit's SpeedDiceUI isn't initialized
        /// yet (e.g. BattleSetting phase, between waves, dead units). The
        /// frontend treats absent <c>dieColor</c> as "use faction default".
        /// </summary>
        internal static string TryGet(BattleUnitModel unit)
        {
            TryBindOnce();
            if (_bindFailed || unit == null) return null;

            var setter = unit.view?.speedDiceSetterUI;
            if (setter == null || setter.SpeedDicesCount <= 0) return null;

            // All dice on a unit share the same CDC tint (the patch is per-die
            // but the inputs are per-unit), so the first slot is representative.
            var dieUi = setter.GetSpeedDiceByIndex(0);
            if (dieUi == null) return null;

            var graphic = _rouletteImgField.GetValue(dieUi) as Graphic;
            if (graphic == null) return null;

            // TEMPORARY: capture sprite textures for tint-factor calibration.
            // The extractor self-limits to the first few units and skips the
            // un-Init'd prefab; remove once calibration is done.
            SpeedDiceTextureExtractor.TryExtractFromDie(unit, dieUi);

            return ColorToHex(graphic.color);
        }

        private static string ColorToHex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return "#"
                + r.ToString("x2", CultureInfo.InvariantCulture)
                + g.ToString("x2", CultureInfo.InvariantCulture)
                + b.ToString("x2", CultureInfo.InvariantCulture);
        }
    }
}
