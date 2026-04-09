using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Extracts gift preview sprites from GiftAppearance prefabs to the static
    /// asset directory so the frontend can display them without runtime access to game resources.
    /// Also produces a layout manifest (positions.json) mapping each gift ID to its
    /// CSS position/size on the face-canvas preview.
    /// </summary>
    internal static class GiftCache
    {
        private static bool _extracted = false;

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

        // Iterates all gift XML entries and writes a PNG for each that has a visible appearance.
        // Also collects per-gift position data and writes positions.json.
        private static void Extract()
        {
            var list = Singleton<GiftXmlList>.Instance?.GetAvailableList();
            if (list == null)
                return;

            Directory.CreateDirectory(GiftDir);

            var bounds = AppearanceCache.FaceHairBounds;
            float bw = bounds.size.x;
            float bh = bounds.size.y;
            bool haveBounds = bw > 0f && bh > 0f;

            // gift ID → { leftPct, topPct, widthPct, heightPct }
            var layouts = new Dictionary<int, (float left, float top, float w, float h)>();

            foreach (var xml in list)
            {
                // NoAppear gifts have no visible sprite; skip them.
                if (xml.NoAppear)
                    continue;

                try
                {
                    // Gift prefabs follow a fixed naming convention in the Resources folder.
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

                    var path = Path.Combine(GiftDir, $"gift_{xml.id}.png");
                    if (!File.Exists(path))
                        File.WriteAllBytes(path, IconCache.SpriteToPng(sprite));

                    // Compute CSS position for this gift on the face-canvas preview.
                    if (haveBounds)
                    {
                        // The gift prefab's localPosition is where it sits relative to
                        // the character root.  The front sprite renderer may have an
                        // additional offset within the prefab.
                        var giftLocalPos = prefab.transform.localPosition;
                        var renderers = appearance.GetSpriteRenderers();
                        SpriteRenderer frontRenderer = null;
                        foreach (var r in renderers)
                        {
                            if (r != null) { frontRenderer = r; break; }
                        }

                        if (frontRenderer != null)
                        {
                            // Total offset: gift root + renderer within the prefab.
                            var rendererLocal = frontRenderer.transform.localPosition;
                            float worldX = giftLocalPos.x + rendererLocal.x;
                            float worldY = giftLocalPos.y + rendererLocal.y;

                            // Sprite world-space dimensions.
                            float sprW = sprite.bounds.size.x;
                            float sprH = sprite.bounds.size.y;

                            // Center of the sprite in world space, accounting for pivot offset.
                            float cx = worldX + sprite.bounds.center.x;
                            float cy = worldY + sprite.bounds.center.y;

                            // Convert to CSS percentage coordinates on the face-canvas.
                            // CSS (0%, 0%) = top-left = (bounds.min.x, bounds.max.y)
                            float leftPct = (cx - bounds.min.x) / bw * 100f;
                            float topPct = (bounds.max.y - cy) / bh * 100f;
                            float widthPct = sprW / bw * 100f;
                            float heightPct = sprH / bh * 100f;

                            layouts[xml.id] = (leftPct, topPct, widthPct, heightPct);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[PlayLoRWithMe] GiftCache: failed to extract gift {xml.id}: {ex.Message}");
                }
            }

            // Write the layout manifest for the frontend.
            if (layouts.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append("{");
                bool first = true;
                foreach (var kv in layouts)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    var (l, t, w, h) = kv.Value;
                    sb.Append($"\"{kv.Key}\":{{\"l\":{l:F2},\"t\":{t:F2},\"w\":{w:F2},\"h\":{h:F2}}}");
                }
                sb.Append("}");
                File.WriteAllText(Path.Combine(GiftDir, "layout.json"), sb.ToString());
            }
        }
    }
}
