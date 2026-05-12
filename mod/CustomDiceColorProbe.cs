using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using LOR_BattleUnit_UI;
using UnityEngine;
using UnityEngine.UI;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Samples each unit's frame sprite to derive a representative "die colour"
    /// for the web UI. Per-unit tints applied by any speed-die colour mod
    /// (e.g. CustomSpeedDiceColor / Patty_SpeedDiceColor_MOD) flow through as
    /// an optional <c>dieColor</c> payload field.
    ///
    /// Why the frame sprite rather than the tint:
    ///   CDC's hardcoded fallback (see SpeedDiceUI.Init postfix) swaps the
    ///   frame sprite to a themed one and tints the inner roulette image
    ///   separately. The visible die is dominated by the frame texture (the
    ///   themed sprite shown at color=#ffffff), not by the roulette tint we
    ///   were previously sampling. Sampling the frame sprite's alpha-weighted
    ///   mean captures the muted, accent-coloured appearance that matches
    ///   in-game rendering.
    ///
    /// Per-sprite cost is one blit + readback the first time each sprite is
    /// encountered. The Color result is cached by sprite name; subsequent
    /// lookups are dictionary hits.
    /// </summary>
    internal static class CustomDiceColorProbe
    {
        // SpeedDiceUI.img_normalFrame is private; bind via reflection once.
        private static FieldInfo _normalFrameField;
        private static bool _bindAttempted;
        private static bool _bindFailed;

        // Cache: sprite name -> mean colour (alpha-weighted, opaque pixels only).
        // Keyed by sprite name because CDC reuses the same themed sprite across
        // many units (e.g. all WARP Cleanup Agents share "WarpCrew"), so the
        // computed mean is identical for them.
        private static readonly Dictionary<string, Color> _meanCache =
            new Dictionary<string, Color>();

        private static void TryBindOnce()
        {
            if (_bindAttempted) return;
            _bindAttempted = true;
            _normalFrameField = typeof(SpeedDiceUI).GetField(
                "img_normalFrame",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (_normalFrameField == null)
            {
                _bindFailed = true;
                Debug.LogWarning("[CustomDiceColorProbe] SpeedDiceUI.img_normalFrame field not found via reflection; per-unit colour sampling disabled.");
            }
        }

        /// <summary>
        /// Returns the unit's representative speed-die colour as a
        /// <c>#rrggbb</c> hex string, or null when the unit's SpeedDiceUI
        /// isn't initialised (BattleSetting preview, between waves, dead unit
        /// cleanup) or its frame sprite is the un-Init'd prefab default.
        /// </summary>
        internal static string TryGet(BattleUnitModel unit)
        {
            TryBindOnce();
            if (_bindFailed || unit == null) return null;

            var setter = unit.view?.speedDiceSetterUI;
            if (setter == null || setter.SpeedDicesCount <= 0) return null;

            // All dice on a unit share the same themed sprite (CDC's patch is
            // per-die but the inputs are per-unit), so the first slot is
            // representative.
            var dieUi = setter.GetSpeedDiceByIndex(0);
            if (dieUi == null) return null;

            var frameImg = _normalFrameField.GetValue(dieUi) as Image;
            if (frameImg?.sprite == null) return null;

            // Skip the un-Init'd prefab clone — its default sprite is from a
            // generic icon atlas (e.g. "AfterIcon_9_9"), not a themed frame.
            string spriteName = frameImg.sprite.name;
            if (spriteName.StartsWith("AfterIcon")) return null;

            if (!_meanCache.TryGetValue(spriteName, out Color mean))
            {
                mean = ComputeSpriteMean(frameImg.sprite.texture);
                _meanCache[spriteName] = mean;
            }

            // Multiply by the Image's own colour (typically white, but mods
            // could tint the frame differently). Force alpha=1 — we emit
            // RGB-only hex and the underlying alpha here is a Unity rendering
            // concern, not a wire-format one.
            Color tint = frameImg.color;
            Color visible = new Color(mean.r * tint.r, mean.g * tint.g, mean.b * tint.b, 1f);
            return ColorToHex(visible);
        }

        // Standard blit + ReadPixels pattern for non-CPU-readable textures,
        // matching what AppearanceCache does. Runs once per unique sprite per
        // session; subsequent lookups return the cached Color.
        private static Color ComputeSpriteMean(Texture source)
        {
            if (source == null) return Color.white;
            int w = source.width;
            int h = source.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(w, h, TextureFormat.ARGB32, false);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            // Alpha-weighted mean over opaque-ish pixels. Weighting by alpha
            // skews the mean toward the visible sprite content (icon + edges)
            // and away from the soft transparent halo around the hex shape.
            var pixels = readable.GetPixels32();
            double rs = 0, gs = 0, bs = 0, totalA = 0;
            foreach (var p in pixels)
            {
                if (p.a == 0) continue;
                rs += (double)p.r * p.a;
                gs += (double)p.g * p.a;
                bs += (double)p.b * p.a;
                totalA += p.a;
            }
            UnityEngine.Object.Destroy(readable);

            if (totalA <= 0) return Color.white;
            return new Color(
                (float)(rs / totalA / 255.0),
                (float)(gs / totalA / 255.0),
                (float)(bs / totalA / 255.0),
                1f
            );
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
