using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PlayLoRWithMe
{
    internal static class IconCache
    {
        private static readonly HashSet<string> _written = new HashSet<string>();
        private static readonly HashSet<string> _cardWritten = new HashSet<string>();
        private static readonly HashSet<string> _stageWritten = new HashSet<string>();
        private static readonly HashSet<string> _portraitWritten = new HashSet<string>();
        private static string BuficonDir => Path.Combine(Server.WwwRootPath, "assets", "buficons");
        private static string CardIconDir =>
            Path.Combine(Server.WwwRootPath, "assets", "cardicons");
        private static string StageIconDir =>
            Path.Combine(Server.WwwRootPath, "assets", "stageicons");
        private static string PortraitDir =>
            Path.Combine(Server.WwwRootPath, "assets", "portraits");

        /// <summary>
        /// Extracts a sprite to PNG and caches it in the given directory.
        /// Returns the icon ID on success, or null if the sprite is null.
        /// </summary>
        /// <param name="id">
        /// Overrides the sprite's own name as the icon ID and filename. Needed where the
        /// caller's key is not usable as a filename; defaults to <c>sprite.name</c>.
        /// </param>
        private static string EnsureSprite(
            Sprite sprite,
            string dir,
            HashSet<string> written,
            string label,
            string id = null,
            System.Func<Sprite, byte[]> encode = null
        )
        {
            if (sprite == null)
                return null;
            id = id ?? sprite.name;
            if (written.Contains(id))
                return id;
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(
                    Path.Combine(dir, id + ".png"),
                    (encode ?? SpriteToPng)(sprite)
                );
                written.Add(id);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PRWM] IconCache ({label}) failed for '{id}': {ex.Message}");
                return null;
            }
            return id;
        }

        internal static string EnsureIcon(Sprite sprite) =>
            EnsureSprite(sprite, BuficonDir, _written, "buff");

        internal static string EnsureCardIcon(Sprite sprite) =>
            EnsureSprite(sprite, CardIconDir, _cardWritten, "card");

        internal static string EnsureStageIcon(Sprite sprite) =>
            EnsureSprite(sprite, StageIconDir, _stageWritten, "stage");

        /// <summary>
        /// Extracts a story-character portrait under an explicit ID. Portraits are keyed
        /// by the dialogue's model name, which is not guaranteed to be ASCII and so cannot
        /// serve as a filename directly — callers pass a slug from
        /// <see cref="StoryLog.SlugifyPortraitKey"/>.
        /// </summary>
        /// <remarks>
        /// Padded to the sprite's logical rect rather than exported as a tight crop.
        /// Portraits are authored on a shared 256x256 canvas and stored trimmed to
        /// each character's own bounds (confirmed from sprite geometry: identical
        /// <c>rect</c>, crops varying from roughly 146x196 to 223x217, with the
        /// horizontal offset carrying the placement). Exporting the crop alone
        /// discarded that placement, leaving every character a different size with no
        /// common frame of reference, so heads landed at different scales in the UI's
        /// fixed frame.
        /// </remarks>
        internal static string EnsurePortrait(Sprite sprite, string id)
        {
            if (sprite != null && !_portraitWritten.Contains(id ?? sprite.name))
                LogPortraitGeometry(sprite, id);
            return EnsureSprite(
                sprite,
                PortraitDir,
                _portraitWritten,
                "portrait",
                id,
                SpriteToPaddedPng
            );
        }

        // One line per distinct portrait. The shared-canvas assumption the frontend's
        // framing is tuned against is not something we can assert at compile time, so
        // this leaves it checkable from the player log if a game update ever changes
        // the canvas size or starts shipping portraits untrimmed.
        private static void LogPortraitGeometry(Sprite sprite, string id)
        {
            ModLog.Info(
                $"[IconCache] portrait '{id}': rect={sprite.rect.width}x{sprite.rect.height} "
                    + $"crop={sprite.textureRect.width}x{sprite.textureRect.height} "
                    + $"offset={sprite.textureRectOffset.x},{sprite.textureRectOffset.y} "
                    + $"pivot={sprite.pivot.x},{sprite.pivot.y} ppu={sprite.pixelsPerUnit}"
            );
        }

        // Extracts the sprite's pixel region via RenderTexture — handles non-readable
        // textures and sprite-sheet atlases safely on the Unity main thread.
        internal static Texture2D ReadSpriteCrop(Sprite sprite)
        {
            var src = sprite.texture;
            var rect = sprite.textureRect; // pixel coords within source texture (bottom-left origin)

            var rtFull = RenderTexture.GetTemporary(
                src.width,
                src.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            );
            Graphics.Blit(src, rtFull);

            var prev = RenderTexture.active;
            RenderTexture.active = rtFull;

            var dst = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);

            // sprite.textureRect uses bottom-left origin; DX11 RenderTextures use
            // top-left origin, so we must flip Y when calling ReadPixels.
            float flippedY = src.height - rect.y - rect.height;
            dst.ReadPixels(new Rect(rect.x, flippedY, rect.width, rect.height), 0, 0);
            dst.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rtFull);
            return dst;
        }

        internal static byte[] SpriteToPng(Sprite sprite)
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

        /// <summary>
        /// Extracts a sprite onto its full logical rect instead of its tight crop, so
        /// sprites authored on a shared canvas but trimmed to differing bounds still
        /// line up with one another.
        /// </summary>
        /// <remarks>
        /// <c>sprite.rect</c> is the logical rect including transparent padding;
        /// <c>textureRect</c> is the trimmed region actually stored in the atlas, and
        /// <c>textureRectOffset</c> locates the latter inside the former. Exporting the
        /// crop alone discards that padding, so two portraits drawn on one canvas come
        /// out at different sizes and no longer share a frame of reference.
        ///
        /// Degrades to <see cref="SpriteToPng"/> when the sprite is untrimmed, since
        /// then the logical rect and the crop are the same region.
        /// </remarks>
        internal static byte[] SpriteToPaddedPng(Sprite sprite)
        {
            var crop = ReadSpriteCrop(sprite);
            Texture2D padded = null;
            try
            {
                int canvasW = Mathf.RoundToInt(sprite.rect.width);
                int canvasH = Mathf.RoundToInt(sprite.rect.height);
                int offsetX = Mathf.RoundToInt(sprite.textureRectOffset.x);
                int offsetY = Mathf.RoundToInt(sprite.textureRectOffset.y);

                bool untrimmed =
                    canvasW == crop.width
                    && canvasH == crop.height
                    && offsetX == 0
                    && offsetY == 0;
                if (untrimmed || canvasW <= 0 || canvasH <= 0)
                    return crop.EncodeToPNG();

                // SetPixels32 requires the block to fit exactly, so a crop that would
                // overhang the logical rect falls back to the unpadded export rather
                // than throwing on a malformed sprite.
                bool fits =
                    offsetX >= 0
                    && offsetY >= 0
                    && offsetX + crop.width <= canvasW
                    && offsetY + crop.height <= canvasH;
                if (!fits)
                    return crop.EncodeToPNG();

                padded = new Texture2D(canvasW, canvasH, TextureFormat.RGBA32, false);
                padded.SetPixels32(new Color32[canvasW * canvasH]); // transparent fill
                padded.SetPixels32(
                    offsetX,
                    offsetY,
                    crop.width,
                    crop.height,
                    crop.GetPixels32()
                );
                padded.Apply();
                return padded.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.Destroy(crop);
                if (padded != null)
                    UnityEngine.Object.Destroy(padded);
            }
        }
    }
}
