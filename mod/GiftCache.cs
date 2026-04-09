using System.IO;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Extracts gift preview sprites from GiftAppearance prefabs to the static
    /// asset directory so the frontend can display them without runtime access to game resources.
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
        private static void Extract()
        {
            var list = Singleton<GiftXmlList>.Instance?.GetAvailableList();
            if (list == null)
                return;

            Directory.CreateDirectory(GiftDir);

            foreach (var xml in list)
            {
                // NoAppear gifts have no visible sprite; skip them.
                if (xml.NoAppear)
                    continue;

                var path = Path.Combine(GiftDir, $"gift_{xml.id}.png");
                if (File.Exists(path))
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

                    File.WriteAllBytes(path, IconCache.SpriteToPng(sprite));
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[PlayLoRWithMe] GiftCache: failed to extract gift {xml.id}: {ex.Message}");
                }
            }
        }
    }
}
