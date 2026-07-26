using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Patron (SpecialCustomizedAppearance) head extraction -- composites each
    /// sephirah's integrated head, face and hair renderers into front and rear PNGs.
    /// </summary>
    internal static partial class AppearanceCache
    {
        /// <summary>
        /// Extracts composite head sprites from patron (SpecialCustomizedAppearance) prefabs.
        /// Patron librarians replace the entire face (head, eyes, brows, mouth, hair) with
        /// their own integrated sprites.  Two PNGs per patron:
        ///   head_special_{id}.png       — head, face, front hair (renders above fashion body)
        ///   head_special_{id}_rear.png  — rear hair (renders behind fashion body)
        /// </summary>
        private static void ExtractPatronHeads(
            int canvasW,
            int canvasH,
            Bounds totalBounds,
            float ppu
        )
        {
            var loader = Singleton<CustomizingResourceLoader>.Instance;
            if (loader == null)
                return;

            var field = typeof(CustomizingResourceLoader).GetField(
                "_specialCustomPrefabDic",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );
            if (field == null)
                return;
            var dic = field.GetValue(loader) as Dictionary<int, SpecialCustomizedAppearance>;
            if (dic == null)
                return;

            foreach (var kvp in dic)
            {
                int sephirahId = kvp.Key;
                var prefab = kvp.Value;
                if (prefab?.list == null || prefab.list.Count == 0)
                    continue;

                var frontPath = Path.Combine(CustomizeDir, $"head_special_{sephirahId}.png");
                var rearPath = Path.Combine(CustomizeDir, $"head_special_{sephirahId}_rear.png");
                if (File.Exists(frontPath) && File.Exists(rearPath))
                    continue;

                // Instantiate the prefab so transforms and sprite positions are resolved.
                // Reset transform to identity — the prefab may carry a non-zero rotation
                // from the Unity editor, causing an unwanted tilt.
                var go = UnityEngine.Object.Instantiate(prefab.gameObject);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                try
                {
                    var inst = go.GetComponent<SpecialCustomizedAppearance>();
                    if (inst?.list == null || inst.list.Count == 0)
                        continue;

                    // Mirror the game's selection logic in RefreshAppearanceByMotion:
                    // 1) find head matching ActionDetail.Standing
                    // 2) fall back to front-facing direction
                    // 3) fall back to first available
                    var head =
                        inst.list.Find(h => h.detail == ActionDetail.Standing)
                        ?? inst.list.Find(h =>
                            h.motionDirection == CharacterMotion.MotionDirection.FrontView
                        )
                        ?? inst.list[0];

                    // Activate this variant so its renderers are live; deactivate others.
                    foreach (var h in inst.list)
                        h.rootObject?.SetActive(h == head);

                    // Reset the head's local transform — the game does this in
                    // RefreshAppearanceByMotion after parenting to the Head sprite set.
                    if (head.rootObject != null)
                    {
                        head.rootObject.transform.localPosition = Vector3.zero;
                        head.rootObject.transform.localRotation = Quaternion.identity;
                    }

                    // Partition renderers into rear (behind body) and front (above body).
                    // Rear hair renders behind the fashion body; everything else renders
                    // in front, matching the normal face/hair layer ordering.
                    var rearRenderers = new List<SpriteRenderer>();
                    var frontRenderers = new List<SpriteRenderer>();

                    void AddTo(List<SpriteRenderer> list, SpriteRenderer sr)
                    {
                        if (sr != null && sr.sprite != null)
                            list.Add(sr);
                    }
                    AddTo(frontRenderers, head.headRenderer);
                    AddTo(frontRenderers, head.faceRenderer);
                    AddTo(frontRenderers, head.frontHairRenderer);
                    AddTo(rearRenderers, head.rearHairRenderer);
                    if (head.additionalFace != null)
                        foreach (var sr in head.additionalFace)
                            AddTo(frontRenderers, sr);
                    if (head.additionalFrontHair != null)
                        foreach (var sr in head.additionalFrontHair)
                            AddTo(frontRenderers, sr);
                    if (head.additionalRearHair != null)
                        foreach (var sr in head.additionalRearHair)
                            AddTo(rearRenderers, sr);

                    if (frontRenderers.Count == 0 && rearRenderers.Count == 0)
                        continue;

                    // Sort each group by layer/order (back to front).
                    var slayers = SortingLayer.layers;
                    var layerIdxMap = new Dictionary<int, int>(slayers.Length);
                    for (int li = 0; li < slayers.Length; li++)
                        layerIdxMap[slayers[li].id] = li;
                    System.Comparison<SpriteRenderer> cmp = (a, b) =>
                    {
                        int la = layerIdxMap.TryGetValue(a.sortingLayerID, out var va) ? va : 0;
                        int lb = layerIdxMap.TryGetValue(b.sortingLayerID, out var vb) ? vb : 0;
                        if (la != lb)
                            return la.CompareTo(lb);
                        return a.sortingOrder.CompareTo(b.sortingOrder);
                    };
                    frontRenderers.Sort(cmp);
                    rearRenderers.Sort(cmp);

                    // Compute a unified bounding box from ALL renderers (front + rear) so
                    // both PNGs share the same canvas and align when CSS-stacked.
                    Bounds patronBounds = totalBounds;
                    foreach (var sr in frontRenderers)
                        patronBounds.Encapsulate(sr.bounds);
                    foreach (var sr in rearRenderers)
                        patronBounds.Encapsulate(sr.bounds);

                    int patronCanvasW = Mathf.Max(
                        canvasW,
                        Mathf.RoundToInt(patronBounds.size.x * ppu)
                    );
                    int patronCanvasH = Mathf.Max(
                        canvasH,
                        Mathf.RoundToInt(patronBounds.size.y * ppu)
                    );

                    if (!File.Exists(frontPath) && frontRenderers.Count > 0)
                        CompositeAndSave(
                            frontRenderers,
                            patronCanvasW,
                            patronCanvasH,
                            patronBounds,
                            ppu,
                            frontPath
                        );

                    if (!File.Exists(rearPath) && rearRenderers.Count > 0)
                        CompositeAndSave(
                            rearRenderers,
                            patronCanvasW,
                            patronCanvasH,
                            patronBounds,
                            ppu,
                            rearPath
                        );
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning(
                        $"[PRWM] AppearanceCache: patron head [{sephirahId}] failed: {ex.Message}"
                    );
                }
                finally
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
        }

        /// <summary>
        /// Composites a sorted list of SpriteRenderers (back-to-front) onto a canvas
        /// and saves the result as a PNG.
        /// </summary>
        private static void CompositeAndSave(
            List<SpriteRenderer> renderers,
            int cw,
            int ch,
            Bounds bounds,
            float ppu,
            string outPath
        )
        {
            // Hoist the pixel buffer outside the loop so we composite directly
            // into it, avoiding a full-canvas GetPixels32/SetPixels32 round-trip
            // per sprite (two O(cw*ch) copies per iteration).
            var dstPixels = new Color32[cw * ch];

            foreach (var sr in renderers)
            {
                Texture2D crop = null;
                try
                {
                    crop = ReadSpriteCrop(sr.sprite);
                    int cropW = crop.width;
                    int cropH = crop.height;

                    float spritePpu = sr.sprite.pixelsPerUnit;
                    float ppuRatio = ppu / spritePpu;

                    var wb = sr.bounds;
                    int boundsOffsetX = Mathf.RoundToInt((wb.min.x - bounds.min.x) * ppu);
                    int boundsOffsetY = Mathf.RoundToInt((wb.min.y - bounds.min.y) * ppu);

                    var texRectOffset = sr.sprite.textureRectOffset;
                    int offsetX = boundsOffsetX + Mathf.RoundToInt(texRectOffset.x * ppuRatio);
                    int offsetY = boundsOffsetY + Mathf.RoundToInt(texRectOffset.y * ppuRatio);

                    int finalW,
                        finalH;
                    Color32[] srcPixels;

                    if (Mathf.Approximately(ppuRatio, 1f))
                    {
                        finalW = cropW;
                        finalH = cropH;
                        srcPixels = crop.GetPixels32();
                    }
                    else
                    {
                        finalW = Mathf.Max(1, Mathf.RoundToInt(cropW * ppuRatio));
                        finalH = Mathf.Max(1, Mathf.RoundToInt(cropH * ppuRatio));
                        var rt = RenderTexture.GetTemporary(
                            finalW,
                            finalH,
                            0,
                            RenderTextureFormat.ARGB32
                        );
                        try
                        {
                            Graphics.Blit(crop, rt);
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            var scaled = new Texture2D(finalW, finalH, TextureFormat.RGBA32, false);
                            try
                            {
                                scaled.ReadPixels(new Rect(0, 0, finalW, finalH), 0, 0);
                                scaled.Apply();
                                srcPixels = scaled.GetPixels32();
                            }
                            finally
                            {
                                RenderTexture.active = prev;
                                UnityEngine.Object.Destroy(scaled);
                            }
                        }
                        finally
                        {
                            RenderTexture.ReleaseTemporary(rt);
                        }
                    }

                    // Apply SpriteRenderer.color tint (Unity multiplies every pixel
                    // by this color — e.g. a white head sprite × skin color = skin).
                    Color32 tint = sr.color;
                    bool hasTint = tint.r != 255 || tint.g != 255 || tint.b != 255 || tint.a != 255;

                    // Alpha-composite onto the canvas (back-to-front).
                    for (int sy = 0; sy < finalH; sy++)
                    {
                        int dy = offsetY + sy;
                        if (dy < 0 || dy >= ch)
                            continue;
                        for (int sx = 0; sx < finalW; sx++)
                        {
                            int dx = offsetX + sx;
                            if (dx < 0 || dx >= cw)
                                continue;
                            int si = sy * finalW + sx;
                            int di = dy * cw + dx;
                            var src = srcPixels[si];
                            if (src.a == 0)
                                continue;
                            if (hasTint)
                            {
                                src = new Color32(
                                    (byte)(src.r * tint.r / 255),
                                    (byte)(src.g * tint.g / 255),
                                    (byte)(src.b * tint.b / 255),
                                    (byte)(src.a * tint.a / 255)
                                );
                                if (src.a == 0)
                                    continue;
                            }
                            if (src.a == 255)
                            {
                                dstPixels[di] = src;
                                continue;
                            }
                            var dst = dstPixels[di];
                            float sa = src.a / 255f;
                            float da = dst.a / 255f;
                            float oa = sa + da * (1f - sa);
                            if (oa > 0f)
                            {
                                dstPixels[di] = new Color32(
                                    (byte)((src.r * sa + dst.r * da * (1f - sa)) / oa),
                                    (byte)((src.g * sa + dst.g * da * (1f - sa)) / oa),
                                    (byte)((src.b * sa + dst.b * da * (1f - sa)) / oa),
                                    (byte)(oa * 255f)
                                );
                            }
                        }
                    }
                }
                finally
                {
                    if (crop != null)
                        UnityEngine.Object.Destroy(crop);
                }
            }

            var canvas = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            try
            {
                canvas.SetPixels32(dstPixels);
                canvas.Apply();
                File.WriteAllBytes(outPath, canvas.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.Destroy(canvas);
            }
        }
    }
}
