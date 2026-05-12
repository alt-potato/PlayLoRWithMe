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
    /// TEMPORARY one-shot diagnostic extractor: walks every live SpeedDiceUI
    /// instance after a battle scene activates, dumps each unique assigned
    /// sprite texture to wwwroot/assets/dice-debug/ as PNG, and logs each
    /// unit's (frame sprite, glow sprite, sampled tint) tuple so the captured
    /// textures can be correlated with the tints observed in-game.
    ///
    /// Purpose: calibrate the tint → visible-colour relationship for
    /// CustomSpeedDiceColor-themed sprites (which darken the tint by
    /// per-channel multiplication against the underlying texture).
    ///
    /// Remove this file (and its csproj entry and the StateBroadcaster call)
    /// once the calibration factor is captured.
    /// </summary>
    internal static class SpeedDiceTextureExtractor
    {
        private static bool _done;
        private static readonly HashSet<string> _writtenSprites = new HashSet<string>();

        internal static void ExtractOnce()
        {
            if (_done) return;
            try
            {
                var outDir = Path.Combine(Server.WwwRootPath, "assets", "dice-debug");
                Directory.CreateDirectory(outDir);

                var instances = Resources.FindObjectsOfTypeAll<SpeedDiceUI>();
                if (instances == null || instances.Length == 0) return;

                int unitNumber = 0;
                foreach (var instance in instances)
                {
                    if (instance == null) continue;

                    var frameImg = ReadField<Image>(instance, "img_normalFrame");
                    var glowImg = ReadField<Image>(instance, "img_lightFrame");
                    var rouletteRaw = ReadField<RawImage>(instance, "_rouletteImg");

                    if (frameImg?.sprite == null) continue;

                    unitNumber++;
                    Color tint = rouletteRaw?.color ?? frameImg.color;
                    string frameSpriteName = frameImg.sprite.name;
                    string glowSpriteName = glowImg?.sprite?.name ?? "<none>";

                    Debug.Log(
                        $"[SpeedDiceTextureExtractor] unit#{unitNumber} "
                        + $"frame={frameSpriteName} glow={glowSpriteName} "
                        + $"frameColor={ColorToHexRgba(frameImg.color)} "
                        + $"rouletteColor={ColorToHexRgba(tint)}"
                    );

                    WriteSpriteTexture(frameImg.sprite, outDir);
                    if (glowImg?.sprite != null) WriteSpriteTexture(glowImg.sprite, outDir);
                    if (rouletteRaw?.texture != null)
                        WriteTexture(rouletteRaw.texture, "_rouletteImg-texture", outDir);
                }

                if (unitNumber == 0) return; // no Init'd dice yet, retry next time

                _done = true;
                Debug.Log(
                    $"[SpeedDiceTextureExtractor] wrote {_writtenSprites.Count} "
                    + $"unique sprite textures to {outDir}"
                );
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

        private static void WriteSpriteTexture(Sprite sprite, string outDir)
        {
            string name = sprite.name;
            if (_writtenSprites.Contains(name)) return;
            _writtenSprites.Add(name);
            WriteTexture(sprite.texture, name, outDir);
        }

        // Standard blit-then-readback pattern for non-CPU-readable textures —
        // mirrors what AppearanceCache does for character art. Slightly wasteful
        // (re-allocates per call) but this runs once per session.
        private static void WriteTexture(Texture source, string name, string outDir)
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
                string path = Path.Combine(outDir, $"{SanitizeFilename(name)}.png");
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
