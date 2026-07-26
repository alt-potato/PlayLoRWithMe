using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Sprite and texture plumbing -- atlas crop readback, rescaling, canvas encoding
    /// and the shared-canvas sprite renderer used by every extraction pass.
    /// </summary>
    internal static partial class AppearanceCache
    {
        /// <summary>
        /// Encodes a Color[] canvas of the given dimensions to a PNG byte array.
        /// </summary>
        private static byte[] EncodeCanvasToPng(Color[] canvas, int canvasW, int canvasH)
        {
            var tex = new Texture2D(canvasW, canvasH, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels(canvas);
                tex.Apply();
                return tex.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        /// <summary>
        /// Scales <paramref name="src"/> to <paramref name="targetW"/>×<paramref name="targetH"/>
        /// using a RenderTexture blit (bilinear filtering).
        /// Destroys <paramref name="src"/> and returns the new texture.
        /// </summary>
        private static Texture2D ScaleTexture(Texture2D src, int targetW, int targetH)
        {
            var rt = RenderTexture.GetTemporary(
                targetW,
                targetH,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            );
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var scaled = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
                try
                {
                    scaled.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
                    scaled.Apply();
                }
                catch
                {
                    UnityEngine.Object.Destroy(scaled);
                    throw;
                }
                return scaled;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(src);
            }
        }

        /// <summary>
        /// Reads the sprite's atlas sub-region into a new Texture2D via RenderTexture
        /// readback.  Caller is responsible for destroying the returned texture.
        ///
        /// When <see cref="_atlasRtCache"/> is active (non-null), the full-atlas blit
        /// result is cached per source texture so that multiple sprites sharing the
        /// same atlas skip the redundant GPU copy.
        /// </summary>
        private static Texture2D ReadSpriteCrop(Sprite sprite)
        {
            var src = sprite.texture;
            var atlasRect = sprite.textureRect;

            // Floor both corners so the crop stays strictly inside the atlas rect.
            int x0 = Mathf.FloorToInt(atlasRect.x);
            int y0 = Mathf.FloorToInt(atlasRect.y);
            int x1 = Mathf.FloorToInt(atlasRect.x + atlasRect.width);
            int y1 = Mathf.FloorToInt(atlasRect.y + atlasRect.height);
            int cropW = Mathf.Max(1, x1 - x0);
            int cropH = Mathf.Max(1, y1 - y0);

            bool useCache = _atlasRtCache != null;
            RenderTexture cachedRt = null;
            bool cacheHit = useCache && _atlasRtCache.TryGetValue(src, out cachedRt);

            RenderTexture rt;
            if (cacheHit)
            {
                rt = cachedRt;
            }
            else
            {
                var origFilter = src.filterMode;
                src.filterMode = FilterMode.Point;
                rt = RenderTexture.GetTemporary(
                    src.width,
                    src.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB
                );
                Graphics.Blit(src, rt);
                src.filterMode = origFilter;

                if (useCache)
                    _atlasRtCache[src] = rt;
            }

            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                // ReadPixels uses top-left origin (DX11); textureRect uses bottom-left, so flip Y.
                int flippedY = src.height - y1;
                var crop = new Texture2D(cropW, cropH, TextureFormat.RGBA32, false);
                try
                {
                    crop.ReadPixels(new Rect(x0, flippedY, cropW, cropH), 0, 0);
                    crop.Apply();
                }
                catch
                {
                    UnityEngine.Object.Destroy(crop);
                    throw;
                }
                return crop;
            }
            finally
            {
                RenderTexture.active = prev;
                // only release when not cached — the cache owns the RT and releases
                // all entries at the end of Extract().
                if (!useCache)
                    RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// Converts a sprite (assumed to be a simple full-rectangle crop, not atlas-shared)
        /// to a PNG byte array via RenderTexture readback.
        /// </summary>
        private static byte[] SpriteToSimplePng(Sprite sprite)
        {
            var crop = ReadSpriteCrop(sprite);
            try
            {
                return crop.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.Destroy(crop);
            }
        }

        // Mirrors IconCache.SpriteToPng, but positions the sprite crop within a shared
        // canvas whose dimensions are derived from the world-space bounding box of all
        // customization sprites. This ensures all layers composite at the correct pixel
        // offset when stacked in the browser.
        /// <summary>
        /// Renders a sprite onto the shared face-canvas, optionally shifted by a
        /// world-space offset (used by <see cref="GiftCache"/> to position gift
        /// sprites relative to their prefab transform).
        /// </summary>
        internal static byte[] SpriteToPng(
            Sprite sprite,
            int canvasW,
            int canvasH,
            Bounds totalBounds,
            float ppu,
            Vector3 worldOffset = default,
            float worldScale = 1f
        )
        {
            var crop = ReadSpriteCrop(sprite);
            try
            {
                int cropW = crop.width;
                int cropH = crop.height;

                float spritePpu = sprite.pixelsPerUnit;
                // Scale factor combining two effects: ppu mismatch between the sprite and
                // the shared canvas (e.g. 50-ppu sprite on a 100-ppu canvas needs 2× upscale),
                // and any non-unit lossy scale accumulated through the prefab transform chain
                // (e.g. a gift whose renderer sits under a group with localScale != 1).
                // Mirrors BuildBodyCanvas's ppuScale so gift sprites extract at the same
                // size the game would render them at worldScale = 1.
                float ppuRatio = ppu / spritePpu * worldScale;

                // Compute where this sprite's tight crop sits on the shared canvas.
                //
                // sprite.bounds.min is the bottom-left corner of the sprite's LOGICAL rect
                // (including transparent padding) in the renderer's local space.  Scaling
                // by worldScale converts it to world units, then worldOffset places the
                // pivot on the shared canvas.  Subtracting totalBounds.min and multiplying
                // by ppu yields the pixel offset from the canvas origin.
                //
                // textureRectOffset is the additional sub-pixel offset from the logical rect's
                // bottom-left to the tight crop's bottom-left — in the sprite's own pixel
                // space, so it must be scaled by ppuRatio to match the canvas pixels.
                int boundsOffsetX = Mathf.RoundToInt(
                    (sprite.bounds.min.x * worldScale + worldOffset.x - totalBounds.min.x) * ppu
                );
                int boundsOffsetY = Mathf.RoundToInt(
                    (sprite.bounds.min.y * worldScale + worldOffset.y - totalBounds.min.y) * ppu
                );
                var texRectOffset = sprite.textureRectOffset;
                int offsetX = boundsOffsetX + Mathf.RoundToInt(texRectOffset.x * ppuRatio);
                int offsetY = boundsOffsetY + Mathf.RoundToInt(texRectOffset.y * ppuRatio);

                // When ppu values match, blit the crop directly onto the canvas.
                if (Mathf.Approximately(ppuRatio, 1f))
                {
                    // Fast path: the crop fills the entire shared canvas with no offset.
                    if (canvasW == cropW && canvasH == cropH && offsetX == 0 && offsetY == 0)
                    {
                        var result = crop.EncodeToPNG();
                        UnityEngine.Object.Destroy(crop);
                        crop = null; // prevent double-destroy in finally
                        return result;
                    }

                    var dst = new Texture2D(canvasW, canvasH, TextureFormat.RGBA32, false);
                    try
                    {
                        dst.SetPixels32(new Color32[canvasW * canvasH]); // transparent fill

                        int safeW = Mathf.Clamp(cropW, 0, canvasW - offsetX);
                        int safeH = Mathf.Clamp(cropH, 0, canvasH - offsetY);
                        if (safeW > 0 && safeH > 0)
                            dst.SetPixels32(offsetX, offsetY, safeW, safeH, crop.GetPixels32());

                        dst.Apply();
                        return dst.EncodeToPNG();
                    }
                    finally
                    {
                        UnityEngine.Object.Destroy(dst);
                    }
                }

                // PPU mismatch: rescale the crop via RenderTexture before blitting.
                int scaledW = Mathf.Max(1, Mathf.RoundToInt(cropW * ppuRatio));
                int scaledH = Mathf.Max(1, Mathf.RoundToInt(cropH * ppuRatio));

                var rt = RenderTexture.GetTemporary(
                    scaledW,
                    scaledH,
                    0,
                    RenderTextureFormat.ARGB32
                );
                var prev = RenderTexture.active;
                try
                {
                    Graphics.Blit(crop, rt);
                    UnityEngine.Object.Destroy(crop);
                    crop = null; // prevent double-destroy in finally

                    RenderTexture.active = rt;
                    var scaled = new Texture2D(scaledW, scaledH, TextureFormat.RGBA32, false);
                    try
                    {
                        scaled.ReadPixels(new Rect(0, 0, scaledW, scaledH), 0, 0);
                        scaled.Apply();

                        var canvas = new Texture2D(canvasW, canvasH, TextureFormat.RGBA32, false);
                        try
                        {
                            canvas.SetPixels32(new Color32[canvasW * canvasH]);

                            int safeW2 = Mathf.Clamp(scaledW, 0, canvasW - offsetX);
                            int safeH2 = Mathf.Clamp(scaledH, 0, canvasH - offsetY);
                            if (safeW2 > 0 && safeH2 > 0)
                                canvas.SetPixels32(
                                    offsetX,
                                    offsetY,
                                    safeW2,
                                    safeH2,
                                    scaled.GetPixels32()
                                );

                            canvas.Apply();
                            return canvas.EncodeToPNG();
                        }
                        finally
                        {
                            UnityEngine.Object.Destroy(canvas);
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.Destroy(scaled);
                    }
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            finally
            {
                if (crop != null)
                    UnityEngine.Object.Destroy(crop);
            }
        }
    }
}
