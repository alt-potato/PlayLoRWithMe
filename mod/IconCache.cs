using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PlayLoRWithMe
{
    internal static class IconCache
    {
        private static readonly HashSet<string> _written = new HashSet<string>();
        private static readonly HashSet<string> _cardWritten = new HashSet<string>();
        private static string BuficonDir => Path.Combine(Server.WwwRootPath, "assets", "buficons");
        private static string CardIconDir =>
            Path.Combine(Server.WwwRootPath, "assets", "cardicons");

        // Returns the icon ID (sprite.name) on success, null if sprite is null.
        internal static string EnsureIcon(Sprite sprite)
        {
            if (sprite == null)
                return null;
            var id = sprite.name;
            if (_written.Contains(id))
                return id;
            try
            {
                Directory.CreateDirectory(BuficonDir);
                File.WriteAllBytes(Path.Combine(BuficonDir, id + ".png"), SpriteToPng(sprite));
                _written.Add(id);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PlayLoRWithMe] IconCache failed for '{id}': {ex.Message}");
                return null;
            }
            return id;
        }

        internal static string EnsureCardIcon(Sprite sprite)
        {
            if (sprite == null)
                return null;
            var id = sprite.name;
            if (_cardWritten.Contains(id))
                return id;
            try
            {
                Directory.CreateDirectory(CardIconDir);
                File.WriteAllBytes(Path.Combine(CardIconDir, id + ".png"), SpriteToPng(sprite));
                _cardWritten.Add(id);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[PlayLoRWithMe] IconCache (card) failed for '{id}': {ex.Message}"
                );
                return null;
            }
            return id;
        }

        // Extracts the sprite's pixel region via RenderTexture — handles non-readable
        // textures and sprite-sheet atlases safely on the Unity main thread.
        private static byte[] SpriteToPng(Sprite sprite)
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

            return dst.EncodeToPNG();
        }
    }
}
