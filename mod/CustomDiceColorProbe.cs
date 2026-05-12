using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using LOR_BattleUnit_UI;
using UnityEngine;
using UnityEngine.UI;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Samples each unit's first SpeedDiceUI to derive two coordinated colours
    /// for the web UI:
    ///
    ///   <list type="bullet">
    ///     <item><c>fill</c> — alpha-weighted mean of img_normalFrame.sprite's
    ///       texture, representing the dim themed appearance of the hex
    ///       interior. Cached per sprite name.</item>
    ///     <item><c>accent</c> — _rouletteImg.color, the per-unit tint that
    ///       any speed-die colour mod sets to paint the numeric digits. Live
    ///       sample (no caching).</item>
    ///   </list>
    ///
    /// Together they let the frontend match the in-game appearance:
    /// CustomSpeedDiceColor's WARP Cleanup Agent reads as dark navy with
    /// bright blue numerals on the web just as it does in-game.
    /// </summary>
    internal static class CustomDiceColorProbe
    {
        // SpeedDiceUI.img_normalFrame and _rouletteImg are private; bind once.
        private static FieldInfo _normalFrameField;
        private static FieldInfo _rouletteImgField;
        private static bool _bindAttempted;
        private static bool _bindFailed;

        // Cache: frame sprite name -> alpha-weighted mean of opaque pixels.
        private static readonly Dictionary<string, Color> _frameMeanCache =
            new Dictionary<string, Color>();

        internal struct Colors
        {
            public string Fill;
            public string Accent;
            public bool HasAny => Fill != null || Accent != null;
        }

        private static void TryBindOnce()
        {
            if (_bindAttempted) return;
            _bindAttempted = true;
            _normalFrameField = typeof(SpeedDiceUI).GetField(
                "img_normalFrame",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            _rouletteImgField = typeof(SpeedDiceUI).GetField(
                "_rouletteImg",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (_normalFrameField == null || _rouletteImgField == null)
            {
                _bindFailed = true;
                Debug.LogWarning("[CustomDiceColorProbe] SpeedDiceUI fields not found via reflection; per-unit colour sampling disabled.");
            }
        }

        /// <summary>
        /// Returns the unit's representative die colours. Both fields may be
        /// null when the unit's SpeedDiceUI isn't initialised yet (BattleSetting
        /// preview, between waves) or its frame sprite is the prefab default.
        /// The frontend treats null fields as "use the per-faction fallback".
        /// </summary>
        internal static Colors TryGet(BattleUnitModel unit)
        {
            TryBindOnce();
            if (_bindFailed || unit == null) return default;

            var setter = unit.view?.speedDiceSetterUI;
            if (setter == null || setter.SpeedDicesCount <= 0) return default;

            var dieUi = setter.GetSpeedDiceByIndex(0);
            if (dieUi == null) return default;

            var result = new Colors();

            // Fill: cached alpha-weighted mean of the themed frame sprite.
            var frameImg = _normalFrameField.GetValue(dieUi) as Image;
            if (frameImg?.sprite != null)
            {
                string spriteName = frameImg.sprite.name;
                // Skip the un-Init'd prefab default (generic atlas sprite).
                if (!spriteName.StartsWith("AfterIcon"))
                {
                    if (!_frameMeanCache.TryGetValue(spriteName, out Color mean))
                    {
                        mean = ComputeSpriteMean(frameImg.sprite.texture);
                        _frameMeanCache[spriteName] = mean;
                    }
                    // Multiply by frame.color (typically white) so a future mod
                    // that tints the frame instead of the roulette still surfaces.
                    Color tinted = new Color(
                        mean.r * frameImg.color.r,
                        mean.g * frameImg.color.g,
                        mean.b * frameImg.color.b,
                        1f
                    );
                    result.Fill = ColorToHex(tinted);
                }
            }

            // Accent: live _rouletteImg tint — what CDC paints onto the digits.
            var rouletteRaw = _rouletteImgField.GetValue(dieUi) as Graphic;
            if (rouletteRaw != null)
            {
                result.Accent = ColorToHex(rouletteRaw.color);
            }

            return result;
        }

        // Standard blit + ReadPixels for non-CPU-readable textures.
        // Runs once per unique sprite per session; subsequent lookups hit cache.
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

            // Alpha-weighted mean over opaque pixels — biases toward the visible
            // sprite content (icon + edges) and away from the transparent halo.
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
