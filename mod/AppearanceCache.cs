using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Extracts appearance customization sprites (hair, eyes, mouths, etc.) to
    /// wwwroot/assets/customize/ as indexed PNGs used by the frontend preview.
    /// Called once after the main library scene loads; subsequent calls are no-ops.
    /// </summary>
    internal static class AppearanceCache
    {
        private static bool _extracted = false;

        private static string CustomizeDir =>
            Path.Combine(Server.WwwRootPath, "assets", "customize");

        /// <summary>
        /// Extracts sprites if not already done. Must be called on the Unity main thread.
        /// </summary>
        internal static void EnsureExtracted()
        {
            if (_extracted)
                return;
            try
            {
                // Bump this whenever extraction logic changes to invalidate the on-disk cache.
                const string CacheVersion = "4";
                var versionPath = Path.Combine(CustomizeDir, "_cache_version.txt");

                bool stale =
                    !File.Exists(versionPath)
                    || File.ReadAllText(versionPath).Trim() != CacheVersion;

                if (stale && Directory.Exists(CustomizeDir))
                    Directory.Delete(CustomizeDir, recursive: true);

                Extract();
                _extracted = true;

                File.WriteAllText(versionPath, CacheVersion);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[PlayLoRWithMe] AppearanceCache: extraction failed: {ex.Message}"
                );
            }
        }

        private static void Extract()
        {
            Directory.CreateDirectory(CustomizeDir);
            var loader = Singleton<CustomizingResourceLoader>.Instance;
            if (loader == null)
                return;

            // Ensure the loader's sprite arrays are populated before querying them.
            loader.LoadData();

            // --- Pass 1: gather all sprites ---
            // Collect every sprite we intend to export so we can compute a shared
            // world-space bounding box before writing any files.
            var entries = new List<(string prefix, int index, Sprite sprite)>();

            void Gather(string prefix, int count, System.Func<int, Sprite> getter)
            {
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var s = getter(i);
                        if (s != null)
                            entries.Add((prefix, i, s));
                    }
                    catch { /* skip inaccessible indices */ }
                }
            }

            Gather("eyes",      loader.NumberOfEye(),  i => loader.GetEyeResourceSet(i)?.normal);
            Gather("brows",     loader.NumberOfBrow(), i => loader.GetBrowResourceSet(i)?.normal);
            Gather("mouths",    loader.NumberOfMouth(), i => loader.GetMouthResourceSet(i)?.normal);
            Gather("fronthair", loader.NumberOfCustomizingResources(CustomizingLookType.FrontHair),
                                i => loader.GetFrontHairSprite(i));
            Gather("backhair",  loader.NumberOfCustomizingResources(CustomizingLookType.BackHair),
                                i => loader.GetRearHairSprite(i));
            // Heads: only 2 variants; GetHeadSprite has no bounds check so we stop on error.
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    var s = loader.GetHeadSprite(i);
                    if (s != null)
                        entries.Add(("head", i, s));
                }
                catch { break; }
            }

            if (entries.Count == 0)
                return;

            // --- Pass 2: compute shared canvas via world-space bounds ---
            // All customization sprites share the same coordinate frame — they are rendered
            // at the same transform position, so their bounds are directly comparable.
            // Encapsulating all bounds gives a canvas that fits every layer perfectly.
            var totalBounds = entries[0].sprite.bounds;
            float ppu = entries[0].sprite.pixelsPerUnit;
            foreach (var (_, _, sp) in entries)
                totalBounds.Encapsulate(sp.bounds);

            int canvasW = Mathf.Max(1, Mathf.RoundToInt(totalBounds.size.x * ppu));
            int canvasH = Mathf.Max(1, Mathf.RoundToInt(totalBounds.size.y * ppu));

            // --- Pass 3: extract each sprite onto the shared canvas ---
            foreach (var (prefix, index, sprite) in entries)
            {
                try
                {
                    ExtractSprite(prefix, index, sprite, canvasW, canvasH, totalBounds, ppu);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning(
                        $"[PlayLoRWithMe] AppearanceCache: {prefix}[{index}] failed: {ex.Message}"
                    );
                }
            }
        }

        private static void ExtractSprite(
            string prefix,
            int index,
            Sprite sprite,
            int canvasW,
            int canvasH,
            Bounds totalBounds,
            float ppu
        )
        {
            if (sprite == null)
                return;
            var path = Path.Combine(CustomizeDir, $"{prefix}_{index}.png");
            if (File.Exists(path))
                return; // idempotent — skip already-extracted sprites
            File.WriteAllBytes(path, SpriteToPng(sprite, canvasW, canvasH, totalBounds, ppu));
        }

        // Mirrors IconCache.SpriteToPng, but positions the sprite crop within a shared
        // canvas whose dimensions are derived from the world-space bounding box of all
        // customization sprites. This ensures all layers composite at the correct pixel
        // offset when stacked in the browser.
        private static byte[] SpriteToPng(
            Sprite sprite,
            int canvasW,
            int canvasH,
            Bounds totalBounds,
            float ppu
        )
        {
            var src = sprite.texture;
            var atlasRect = sprite.textureRect;

            // Use floor for the start corner and floor for the end corner so the crop
            // region stays strictly within the sprite's atlas rect. Using RoundToInt or
            // passing raw floats to ReadPixels can overshoot by up to 1px into adjacent
            // atlas content, producing a thin white border on the right/top edges.
            int x0 = Mathf.FloorToInt(atlasRect.x);
            int y0 = Mathf.FloorToInt(atlasRect.y);
            int x1 = Mathf.FloorToInt(atlasRect.x + atlasRect.width);
            int y1 = Mathf.FloorToInt(atlasRect.y + atlasRect.height);
            int cropW = Mathf.Max(1, x1 - x0);
            int cropH = Mathf.Max(1, y1 - y0);

            // Blit the atlas texture to a RenderTexture for CPU readback
            // (handles non-readable textures safely). Point filter prevents bilinear
            // sampling from bleeding neighboring atlas pixels into the crop.
            var origFilter = src.filterMode;
            src.filterMode = FilterMode.Point;
            var rtFull = RenderTexture.GetTemporary(
                src.width,
                src.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            );
            Graphics.Blit(src, rtFull);
            src.filterMode = origFilter;

            var prev = RenderTexture.active;
            RenderTexture.active = rtFull;

            // ReadPixels uses top-left origin (DX11), but sprite.textureRect uses
            // bottom-left origin, so Y must be flipped.
            int flippedY = src.height - y1;
            var crop = new Texture2D(cropW, cropH, TextureFormat.RGBA32, false);
            crop.ReadPixels(new Rect(x0, flippedY, cropW, cropH), 0, 0);
            crop.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rtFull);

            // Compute where this sprite's tight crop sits on the shared canvas.
            //
            // sprite.bounds.min is the world-space bottom-left corner of the sprite's
            // LOGICAL rect (including transparent padding) relative to its pivot (0,0).
            // Subtracting totalBounds.min and scaling by ppu gives the pixel offset from
            // the shared canvas origin to the logical rect's bottom-left.
            //
            // textureRectOffset is the additional sub-pixel offset from the logical rect's
            // bottom-left to the tight crop's bottom-left, in pixels.
            int boundsOffsetX = Mathf.RoundToInt(
                (sprite.bounds.min.x - totalBounds.min.x) * ppu
            );
            int boundsOffsetY = Mathf.RoundToInt(
                (sprite.bounds.min.y - totalBounds.min.y) * ppu
            );
            var texRectOffset = sprite.textureRectOffset;
            int offsetX = boundsOffsetX + Mathf.RoundToInt(texRectOffset.x);
            int offsetY = boundsOffsetY + Mathf.RoundToInt(texRectOffset.y);

            // Fast path: the crop fills the entire shared canvas with no offset.
            if (canvasW == cropW && canvasH == cropH && offsetX == 0 && offsetY == 0)
                return crop.EncodeToPNG();

            var dst = new Texture2D(canvasW, canvasH, TextureFormat.RGBA32, false);
            dst.SetPixels32(new Color32[canvasW * canvasH]); // transparent fill

            // Clamp to valid range in case of floating-point rounding edge cases.
            int safeW = Mathf.Clamp(cropW, 0, canvasW - offsetX);
            int safeH = Mathf.Clamp(cropH, 0, canvasH - offsetY);
            if (safeW > 0 && safeH > 0)
                dst.SetPixels32(offsetX, offsetY, safeW, safeH, crop.GetPixels32());

            dst.Apply();
            return dst.EncodeToPNG();
        }
    }
}
