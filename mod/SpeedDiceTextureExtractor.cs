using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using LOR_BattleUnit_UI;
using UnityEngine;
using UnityEngine.UI;

namespace PlayLoRWithMe
{
    /// <summary>
    /// TEMPORARY one-shot diagnostic extractor for calibrating the tint →
    /// visible-colour relationship on CustomSpeedDiceColor-themed sprites.
    /// Invoked from <see cref="CustomDiceColorProbe.TryGet"/> on the first live
    /// die we sample, so the SpeedDiceUI has already been Init'd (and any
    /// upstream tint mod has applied its swaps and colours). For each unique
    /// frame sprite encountered we dump the texture as PNG to
    /// wwwroot/assets/dice-debug/ and log the (sprite name, tint) pair.
    ///
    /// Remove this file (and the call from CustomDiceColorProbe) once the
    /// approximation factor is captured.
    /// </summary>
    internal static class SpeedDiceTextureExtractor
    {
        private const int MaxUnitsToLog = 8;
        private static int _unitsLogged;
        private static bool _everWroteAnything;
        private static readonly HashSet<string> _writtenSprites = new HashSet<string>();
        private static string _outDir;

        // Called per-unit, but only logs/writes for the first MaxUnitsToLog
        // calls that reach this code path. Each call cheaply returns once
        // we've captured enough variety to calibrate against.
        internal static void TryExtractFromDie(BattleUnitModel unit, SpeedDiceUI dieUi)
        {
            if (_unitsLogged >= MaxUnitsToLog) return;
            if (dieUi == null) return;

            try
            {
                var frameImg = ReadField<Image>(dieUi, "img_normalFrame");
                var glowImg = ReadField<Image>(dieUi, "img_lightFrame");
                var rouletteRaw = ReadField<RawImage>(dieUi, "_rouletteImg");
                if (frameImg?.sprite == null) return;

                // Skip the prefab clone — its default sprite is from an icon
                // atlas (e.g. "AfterIcon_9_9"). CDC-swapped sprites are named
                // after themes (Malkuth, Yesod, RedMist, Abnormalities, etc.).
                var frameSpriteName = frameImg.sprite.name;
                if (frameSpriteName.StartsWith("AfterIcon")) return;

                if (_outDir == null)
                {
                    _outDir = Path.Combine(Server.WwwRootPath, "assets", "dice-debug");
                    Directory.CreateDirectory(_outDir);
                }

                _unitsLogged++;
                var glowSpriteName = glowImg?.sprite?.name ?? "<none>";
                string unitName = unit?.UnitData?.unitData?.name ?? "<unknown>";
                int unitId = unit?.id ?? -1;

                Debug.Log(
                    $"[SpeedDiceTextureExtractor] unit#{_unitsLogged} "
                    + $"id={unitId} name=\"{unitName}\" "
                    + $"frame={frameSpriteName} glow={glowSpriteName} "
                    + $"frameColor={ColorToHexRgba(frameImg.color)} "
                    + $"glowColor={(glowImg != null ? ColorToHexRgba(glowImg.color) : "<n/a>")} "
                    + $"rouletteColor={(rouletteRaw != null ? ColorToHexRgba(rouletteRaw.color) : "<n/a>")}"
                );

                WriteSpriteTexture(frameImg.sprite);
                if (glowImg?.sprite != null) WriteSpriteTexture(glowImg.sprite);
                if (rouletteRaw?.texture != null && !_everWroteAnything)
                {
                    // roulette texture is shared across all dice; capture once
                    WriteTexture(rouletteRaw.texture, "_rouletteImg-texture");
                }

                _everWroteAnything = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpeedDiceTextureExtractor] extraction failed: {ex}");
            }
        }

        private static T ReadField<T>(object target, string fieldName) where T : class
        {
            var f = target.GetType().GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            return f?.GetValue(target) as T;
        }

        private static void WriteSpriteTexture(Sprite sprite)
        {
            string name = sprite.name;
            if (_writtenSprites.Contains(name)) return;
            _writtenSprites.Add(name);
            WriteTexture(sprite.texture, name);
        }

        // Standard blit-then-readback pattern for non-CPU-readable textures —
        // mirrors what AppearanceCache does for character art.
        private static void WriteTexture(Texture source, string name)
        {
            try
            {
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
                byte[] png = readable.EncodeToPNG();
                string path = Path.Combine(_outDir, $"{SanitizeFilename(name)}.png");
                File.WriteAllBytes(path, png);
                UnityEngine.Object.Destroy(readable);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpeedDiceTextureExtractor] failed to write {name}: {ex}");
            }
        }

        private static string SanitizeFilename(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static string ColorToHexRgba(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255);
            return "#"
                + r.ToString("x2", CultureInfo.InvariantCulture)
                + g.ToString("x2", CultureInfo.InvariantCulture)
                + b.ToString("x2", CultureInfo.InvariantCulture)
                + a.ToString("x2", CultureInfo.InvariantCulture);
        }
    }
}
