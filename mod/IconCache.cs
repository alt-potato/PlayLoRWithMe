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
        private static string BuficonDir => Path.Combine(Server.WwwRootPath, "assets", "buficons");
        private static string CardIconDir =>
            Path.Combine(Server.WwwRootPath, "assets", "cardicons");
        private static string StageIconDir =>
            Path.Combine(Server.WwwRootPath, "assets", "stageicons");

        /// <summary>
        /// Extracts a sprite to PNG and caches it in the given directory.
        /// Returns the sprite name (icon ID) on success, or null if the sprite is null.
        /// </summary>
        private static string EnsureSprite(
            Sprite sprite,
            string dir,
            HashSet<string> written,
            string label
        )
        {
            if (sprite == null)
                return null;
            var id = sprite.name;
            if (written.Contains(id))
                return id;
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, id + ".png"), SpriteToPng(sprite));
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

        // Extracts the sprite's pixel region via RenderTexture — handles non-readable
        // textures and sprite-sheet atlases safely on the Unity main thread.
        internal static byte[] SpriteToPng(Sprite sprite)
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

            var png = dst.EncodeToPNG();
            UnityEngine.Object.Destroy(dst);
            return png;
        }
    }
}
