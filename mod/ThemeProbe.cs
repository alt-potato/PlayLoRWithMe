using System;
using System.Globalization;
using System.Reflection;
using LOR_BattleUnit_UI;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Samples vanilla-LoR per-faction speed-die colours from the
    /// <see cref="SpeedDiceUI"/> prefab so the web UI can match the in-game
    /// appearance instead of using neutral panel colours.
    ///
    /// Values live on a private <c>Refs</c> struct field; we reach them via
    /// reflection rather than mirroring the colours ourselves so a future
    /// Project Moon retune is picked up automatically. The probe is best-effort:
    /// on prefab/field rename it falls back to hardcoded approximations served
    /// from <c>app.vue</c>'s <c>:root</c>.
    /// </summary>
    internal static class ThemeProbe
    {
        /// <summary>Probed ally die fill as <c>#rrggbb</c>, or null until <see cref="TryProbe"/> succeeds.</summary>
        internal static string AllyDieColor { get; private set; }

        /// <summary>Probed enemy die fill as <c>#rrggbb</c>, or null until <see cref="TryProbe"/> succeeds.</summary>
        internal static string EnemyDieColor { get; private set; }

        /// <summary>True once a probe attempt has bound both colours; further calls are no-ops.</summary>
        internal static bool IsReady => AllyDieColor != null && EnemyDieColor != null;

        // set to true after a reflection failure that won't recover (the SpeedDiceUI
        // type or Refs field disappeared); skips further retries to avoid log spam.
        private static bool _giveUp;

        /// <summary>
        /// Attempts to sample the prefab colours. Cheap and idempotent — call from
        /// scene-activation hooks until <see cref="IsReady"/> is true. When no
        /// SpeedDiceUI prefab is loaded yet, returns silently so a later call can
        /// retry; only logs (once) on permanent reflection errors.
        /// </summary>
        internal static void TryProbe()
        {
            if (IsReady || _giveUp)
                return;

            try
            {
                var instances = Resources.FindObjectsOfTypeAll<SpeedDiceUI>();
                if (instances == null || instances.Length == 0)
                    return; // prefab not loaded yet; retry on a later scene activation

                var refsField = typeof(SpeedDiceUI).GetField(
                    "Refs",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (refsField == null)
                {
                    _giveUp = true;
                    Debug.LogWarning("[ThemeProbe] SpeedDiceUI.Refs field not found via reflection; falling back to defaults.");
                    return;
                }

                var refsValue = refsField.GetValue(instances[0]);
                if (refsValue == null)
                {
                    // null Refs on this instance — try the next; if none, defer.
                    for (int i = 1; i < instances.Length && refsValue == null; i++)
                        refsValue = refsField.GetValue(instances[i]);
                    if (refsValue == null)
                        return;
                }

                var refsType = refsValue.GetType();
                var allyField = refsType.GetField("color_allyDice");
                var enemyField = refsType.GetField("color_enemyDice");
                if (allyField == null || enemyField == null)
                {
                    _giveUp = true;
                    Debug.LogWarning("[ThemeProbe] SpeedDiceUI.Refs.color_allyDice / color_enemyDice not found; falling back to defaults.");
                    return;
                }

                AllyDieColor = ColorToHex((Color)allyField.GetValue(refsValue));
                EnemyDieColor = ColorToHex((Color)enemyField.GetValue(refsValue));
            }
            catch (Exception ex)
            {
                _giveUp = true;
                Debug.LogWarning("[ThemeProbe] Reflection failed; falling back to defaults: " + ex);
            }
        }

        // Unity Color uses 0..1 floats; clamp and round-trip to #rrggbb so the
        // wire format is CSS-ready without a frontend-side conversion step.
        private static string ColorToHex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return "#" + r.ToString("x2", CultureInfo.InvariantCulture)
                + g.ToString("x2", CultureInfo.InvariantCulture)
                + b.ToString("x2", CultureInfo.InvariantCulture);
        }
    }
}
