using System.IO;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Extracts gift preview sprites from GiftAppearance prefabs to the static
    /// asset directory.  Each gift is rendered onto the same shared canvas as the
    /// face/hair sprites so it layers correctly with <c>background-size: 100% auto</c>
    /// in the browser — no separate coordinate conversion required.
    /// </summary>
    internal static class GiftCache
    {
        private static bool _extracted = false;

        /// <summary>Whether gift sprite extraction has completed successfully.</summary>
        internal static bool IsReady => _extracted;

        private static string GiftDir => Path.Combine(Server.WwwRootPath, "assets", "gifts");

        /// <summary>
        /// Extracts all gift sprites once. Safe to call multiple times; subsequent calls are no-ops.
        /// </summary>
        public static void EnsureExtracted()
        {
            if (_extracted)
                return;
            try
            {
                Extract();
                _extracted = true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PlayLoRWithMe] GiftCache.EnsureExtracted failed: {ex.Message}");
            }
        }

        private static void Extract()
        {
            var list = Singleton<GiftXmlList>.Instance?.GetAvailableList();
            if (list == null)
                return;

            Directory.CreateDirectory(GiftDir);

            var bounds = AppearanceCache.FaceHairBounds;
            float ppu = AppearanceCache.FaceHairPpu;
            int canvasW = AppearanceCache.FaceHairCanvasW;
            int canvasH = AppearanceCache.FaceHairCanvasH;
            bool haveCanvas = canvasW > 0 && canvasH > 0 && ppu > 0f;

            if (!haveCanvas)
                Debug.LogWarning("[PlayLoRWithMe] GiftCache: face-canvas data not available, gifts will use raw sprites");

            foreach (var xml in list)
            {
                if (xml.NoAppear)
                    continue;

                var path = Path.Combine(GiftDir, $"gift_{xml.id}.png");
                if (File.Exists(path))
                    continue;

                try
                {
                    var prefab = Resources.Load<GameObject>($"Prefabs/Gifts/Gifts_NeedRename/Gift_{xml.Resource}");
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[PlayLoRWithMe] GiftCache: prefab not found for gift {xml.id} (Resource={xml.Resource})");
                        continue;
                    }

                    var appearance = prefab.GetComponent<GiftAppearance>();
                    if (appearance == null)
                    {
                        Debug.LogWarning($"[PlayLoRWithMe] GiftCache: no GiftAppearance component on gift {xml.id}");
                        continue;
                    }

                    var sprite = appearance.GetGiftPreview();
                    if (sprite == null)
                    {
                        Debug.LogWarning($"[PlayLoRWithMe] GiftCache: null sprite for gift {xml.id}");
                        continue;
                    }

                    if (haveCanvas)
                    {
                        // Compose the renderer's local-to-prefab transform by walking up
                        // from the sprite renderer through (and including) the prefab root,
                        // accumulating both position AND scale.  Dropping the scale portion
                        // extracts a prefab like Imperfect Ego (id 35) — whose hierarchy
                        // carries a non-unit localScale on a group above the renderer — at
                        // the wrong size even though the pivot lands in the right place.
                        var renderers = appearance.GetSpriteRenderers();
                        SpriteRenderer frontRenderer = null;
                        foreach (var r in renderers)
                        {
                            if (r != null) { frontRenderer = r; break; }
                        }

                        var worldOffset = Vector3.zero;
                        var worldScale  = Vector3.one;
                        if (frontRenderer != null)
                        {
                            var t = frontRenderer.transform;
                            while (t != null)
                            {
                                worldOffset = t.localPosition + Vector3.Scale(t.localScale, worldOffset);
                                worldScale  = Vector3.Scale(worldScale, t.localScale);
                                if (t == prefab.transform) break;
                                t = t.parent;
                            }
                        }

                        File.WriteAllBytes(path,
                            AppearanceCache.SpriteToPng(
                                sprite, canvasW, canvasH, bounds, ppu, worldOffset, worldScale.x));
                    }
                    else
                    {
                        // Fallback: raw sprite without canvas positioning.
                        File.WriteAllBytes(path, IconCache.SpriteToPng(sprite));
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[PlayLoRWithMe] GiftCache: failed to extract gift {xml.id}: {ex.Message}");
                }
            }
        }
    }
}
