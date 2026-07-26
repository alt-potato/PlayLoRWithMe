using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Fashion body compositing -- turns gathered body sprites into the per-book PNGs
    /// and records the canvas metadata the serializer reads back from FashionMeta.
    /// </summary>
    internal static partial class AppearanceCache
    {
        /// <summary>
        /// Composites body sprites per fashion book and writes
        /// wwwroot/assets/fashionbodies/{bookId}.png.
        ///
        /// Canvas strategy:
        ///   replacesHead = false  →  face/hair canvas.  Body sprites are adjusted by
        ///                            anchorPos so they align with the face/hair layers.
        ///   replacesHead = true   →  per-book canvas sized to the character's own body
        ///                            sprite extents, so the character fills the preview.
        /// </summary>
        private static void ExtractFashionBodies(
            List<FashionBookBody> fashionBodies,
            Bounds faceHairBounds,
            int faceHairW,
            int faceHairH,
            float ppu
        )
        {
            if (fashionBodies.Count == 0)
                return;
            Directory.CreateDirectory(FashionBodyDir);

            foreach (var body in fashionBodies)
            {
                // gendered variants use a suffix: fashionbodies/123_f.png, 123_m.png
                var suffix = body.Variant != null ? $"_{body.Variant}" : "";
                var stem = body.FileStem;
                var path = Path.Combine(FashionBodyDir, $"{stem}{suffix}.png");
                var frontPath = Path.Combine(FashionBodyFrontDir, $"{stem}{suffix}.png");

                var skinPath = Path.Combine(FashionBodyDir, $"{stem}{suffix}_skin.png");

                // Per-PNG cache flags — gate the expensive composite/encode/IO work
                // below.  Do NOT use these to skip the loop body wholesale: the bounds
                // math and `RecordFeetYFrac` call at the end of each branch must run on
                // every startup, since `FashionMeta` is rebuilt in-memory each session
                // (Pass 2c seeds `FeetYFrac` with a 1.0 placeholder).  An earlier
                // version had a `continue` here that left every cached book stuck on
                // the placeholder, which made feet land at the PNG bottom edge instead
                // of the authored feet position on every post-first-install startup.
                bool backDone = body.Sprites.Count == 0 || File.Exists(path);
                bool frontDone = body.FrontSprites.Count == 0 || File.Exists(frontPath);
                bool skinDone = body.SkinSprites.Count == 0 || File.Exists(skinPath);

                try
                {
                    // Sort all lists back-to-front (painter's algorithm).
                    System.Comparison<(SpriteSet sprSet, Vector3 worldPos)> bodySortCmp = (a, b) =>
                        a.sprSet.sprRenderer.sortingOrder.CompareTo(
                            b.sprSet.sprRenderer.sortingOrder
                        );
                    body.Sprites.Sort(bodySortCmp);
                    body.FrontSprites.Sort(bodySortCmp);
                    body.SkinSprites.Sort(bodySortCmp);

                    if (body.ReplacesHead)
                    {
                        // Tight per-body canvas: the face/hair layers are hidden for
                        // replacesHead skins, so we don't need to share dimensions with
                        // the face canvas.  A tight canvas lets the frontend render the
                        // body with background-size: contain so the full character fits
                        // the preview box.
                        //
                        // We composite back + front + skin sprites into a single PNG.
                        // Core LoR replacesHead books dump everything into body.Sprites,
                        // but workshop cloth bodies also populate body.FrontSprites with
                        // the Customize_Renderer_Front layer (front-facing accessories
                        // like ribbons, collars, hats) — these must be included or large
                        // accessories get dropped entirely.
                        //
                        // Workshop cloth sprites are loaded via SpriteUtil.LoadLargePivotSprite
                        // which forces every sprite to a 512x512 FullRect sprite — so
                        // sprite.rect, sprite.textureRect, and the logical bounds are all
                        // identical regardless of where the opaque pixels actually sit.
                        // Sizing the canvas by those bounds bakes large asymmetric
                        // transparent padding into the PNG.  We fix horizontal centering
                        // by symmetrizing the canvas X-bounds around AnchorPos.x (the
                        // head attach point from customPivot.position) — see below.
                        var allReplaceSprites = new List<(SpriteSet sprSet, Vector3 worldPos)>(
                            body.Sprites.Count + body.FrontSprites.Count + body.SkinSprites.Count
                        );
                        allReplaceSprites.AddRange(body.SkinSprites);
                        allReplaceSprites.AddRange(body.Sprites);
                        allReplaceSprites.AddRange(body.FrontSprites);
                        if (allReplaceSprites.Count == 0)
                            continue;

                        var bodyBounds = ComputeSpriteBounds(
                            allReplaceSprites,
                            Vector3.zero,
                            body.WorldScale
                        );

                        // Symmetrize X around the head attach point so the head
                        // lands at the horizontal center of the PNG.  In-game,
                        // AnchorPos (customPivot.position) is where the face is
                        // placed on the body; using it as the X-center mirrors
                        // the game's own character alignment.  Expand the canvas
                        // to the larger of the two sides so no content is clipped.
                        float headX = body.AnchorPos.x;
                        float leftExtent = headX - bodyBounds.min.x;
                        float rightExtent = bodyBounds.max.x - headX;
                        float halfW = Mathf.Max(leftExtent, rightExtent);
                        bodyBounds.SetMinMax(
                            new Vector3(headX - halfW, bodyBounds.min.y, bodyBounds.min.z),
                            new Vector3(headX + halfW, bodyBounds.max.y, bodyBounds.max.z)
                        );

                        int bW = Mathf.Max(1, Mathf.RoundToInt(bodyBounds.size.x * ppu));
                        int bH = Mathf.Max(1, Mathf.RoundToInt(bodyBounds.size.y * ppu));

                        // Composite + write the PNG only when it isn't already cached.
                        // BuildBodyCanvas iterates every pixel of every contributing
                        // sprite — too expensive to run for the metadata-only refresh
                        // path that drives `RecordFeetYFrac` below.
                        if (!backDone)
                        {
                            var canvasPixels = BuildBodyCanvas(
                                allReplaceSprites,
                                bW,
                                bH,
                                bodyBounds,
                                ppu,
                                Vector3.zero,
                                body.WorldScale
                            );
                            File.WriteAllBytes(path, EncodeCanvasToPng(canvasPixels, bW, bH));
                        }

                        // Feet sit at world Y = 0 (prefab origin authored at feet) — same
                        // convention as the non-replacesHead branch below.  Pixel-scanning
                        // for the visible bottom would misidentify props hanging below the
                        // feet (e.g. Mao's scabbard) as the floor reference.
                        RecordFeetYFrac(body.FileStem, bodyBounds.max.y, bodyBounds.min.y, 0f);
                        RecordBodyDims(body.FileStem, bW, bH);
                        // replacesHead=true → face overlay never shown; front PNG unused.
                    }
                    else
                    {
                        // The face/hair composite is placed at customPivot (AnchorPos) in-game,
                        // so body sprite positions in face-canvas space = worldPos - AnchorPos.
                        // Using AnchorPos directly as the anchor gives correct alignment for all
                        // books regardless of world scale — no per-book correction is needed.
                        var anchor = body.AnchorPos;

                        // Extend canvas downward to cover all sprites (back + front + skin).
                        // Use the visible sprite bottom rather than the logical rect to avoid
                        // inflating extH with transparent padding.
                        float extMaxY = faceHairBounds.max.y; // must match face/hair for alignment
                        float visMinYFace = faceHairBounds.min.y;
                        foreach (
                            var spriteList in new[]
                            {
                                body.Sprites,
                                body.FrontSprites,
                                body.SkinSprites,
                            }
                        )
                        {
                            foreach (var (ss, wpos) in spriteList)
                            {
                                var spr = ss.sprRenderer.sprite;
                                float visBtm =
                                    wpos.y
                                    + spr.bounds.min.y * body.WorldScale
                                    + spr.textureRectOffset.y / spr.pixelsPerUnit * body.WorldScale;
                                float visBtmFace = visBtm - anchor.y;
                                if (visBtmFace < visMinYFace)
                                    visMinYFace = visBtmFace;
                            }
                        }
                        // Always include the feet position (-anchor.y in face-canvas space)
                        // in the canvas range, even when no body sprite reaches that far.
                        // Books whose sprites cover only the upper body (e.g. E.G.O. gear
                        // like Distorted Yan, whose body sprites sit well above the prefab's
                        // authored feet) would otherwise have their visible bottom at the
                        // lowest sprite and feetYFrac clamp to 1.0 — placing the PNG bottom
                        // at the floor line and making the character float by the gap
                        // between the lowest sprite and the feet.  Extending the canvas to
                        // the feet keeps feetYFrac accurate, so the frontend's feet-anchored
                        // scale transform still lands feet on the shared floor line.  The
                        // extra space is transparent and costs a few empty pixel rows.
                        float extMinY = Mathf.Min(visMinYFace, -anchor.y);
                        var extBounds = new Bounds(
                            new Vector3(faceHairBounds.center.x, (extMinY + extMaxY) * 0.5f, 0f),
                            new Vector3(faceHairBounds.size.x, extMaxY - extMinY, 0.2f)
                        );
                        int extH = Mathf.Max(1, Mathf.RoundToInt((extMaxY - extMinY) * ppu));

                        if (!backDone)
                            File.WriteAllBytes(
                                path,
                                ComposeBodySprites(
                                    body.Sprites,
                                    faceHairW,
                                    extH,
                                    extBounds,
                                    ppu,
                                    anchor,
                                    body.WorldScale
                                )
                            );

                        if (!frontDone)
                        {
                            Directory.CreateDirectory(FashionBodyFrontDir);
                            File.WriteAllBytes(
                                frontPath,
                                ComposeBodySprites(
                                    body.FrontSprites,
                                    faceHairW,
                                    extH,
                                    extBounds,
                                    ppu,
                                    anchor,
                                    body.WorldScale
                                )
                            );
                        }

                        // Skin-type sprites (neck, collarbone) — saved separately so the
                        // frontend can tint them per-librarian with CSS multiply blending.
                        if (!skinDone)
                            File.WriteAllBytes(
                                skinPath,
                                ComposeBodySprites(
                                    body.SkinSprites,
                                    faceHairW,
                                    extH,
                                    extBounds,
                                    ppu,
                                    anchor,
                                    body.WorldScale
                                )
                            );

                        // Feet sit at world Y = 0 (prefab origin authored at feet); in
                        // canvas space that's -anchor.y.  Expose as a fraction from the top
                        // so the frontend can align feet to a shared floor line without
                        // needing the canvas' absolute coordinates.
                        RecordFeetYFrac(body.FileStem, extMaxY, extMinY, -anchor.y);
                        RecordBodyDims(body.FileStem, faceHairW, extH);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning(
                        $"[PRWM] AppearanceCache: fashion body {body.BookId} failed: {ex.Message}"
                    );
                }
            }
        }

        /// <summary>
        /// Updates <see cref="FashionMeta"/> for <paramref name="stem"/> with a feet-Y
        /// fraction: the vertical position of the character's feet within the body PNG,
        /// measured from the top as a value in [0, 1].  1.0 means feet sit exactly at the
        /// PNG bottom; smaller values mean the PNG extends below feet (weapons, props)
        /// and the frontend should offset inward to keep feet on the shared floor line.
        /// </summary>
        private static void RecordFeetYFrac(string stem, float topY, float bottomY, float feetY)
        {
            float canvasH = topY - bottomY;
            if (canvasH <= Mathf.Epsilon)
                return;
            float frac = Mathf.Clamp01((topY - feetY) / canvasH);
            if (!FashionMeta.TryGetValue(stem, out var m))
                return;
            FashionMeta[stem] = (
                m.TiltDeg,
                m.PivotFracX,
                m.PivotFracY,
                m.HasFrontLayer,
                m.HidesBackHair,
                m.SkinGender,
                frac,
                m.BodyW,
                m.BodyH
            );
        }

        /// <summary>
        /// Updates <see cref="FashionMeta"/> for <paramref name="stem"/> with the pixel
        /// dimensions of the extracted body PNG.  Recorded so the frontend can compute
        /// the body-layer height and feet pivot synchronously instead of waiting on
        /// an &lt;img&gt; @load event.
        /// </summary>
        private static void RecordBodyDims(string stem, int w, int h)
        {
            if (w <= 0 || h <= 0)
                return;
            if (!FashionMeta.TryGetValue(stem, out var m))
                return;
            FashionMeta[stem] = (
                m.TiltDeg,
                m.PivotFracX,
                m.PivotFracY,
                m.HasFrontLayer,
                m.HidesBackHair,
                m.SkinGender,
                m.FeetYFrac,
                w,
                h
            );
        }

        /// <summary>
        /// Computes the world-space bounding box of a set of sprites given an optional
        /// anchor adjustment (subtract anchorAdjust from each world position first).
        /// <paramref name="worldScale"/> is the character model's lossyScale — required
        /// because sprite.bounds is in local space and must be scaled to world space.
        /// </summary>
        private static Bounds ComputeSpriteBounds(
            List<(SpriteSet sprSet, Vector3 worldPos)> sprites,
            Vector3 anchorAdjust,
            float worldScale = 1f
        )
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var (ss, wpos) in sprites)
            {
                var adj = wpos - anchorAdjust;
                var spr = ss.sprRenderer.sprite;
                var bMin = new Vector3(
                    adj.x + spr.bounds.min.x * worldScale,
                    adj.y + spr.bounds.min.y * worldScale,
                    0
                );
                var bMax = new Vector3(
                    adj.x + spr.bounds.max.x * worldScale,
                    adj.y + spr.bounds.max.y * worldScale,
                    0
                );
                if (first)
                {
                    bounds = new Bounds((bMin + bMax) * 0.5f, bMax - bMin);
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(bMin);
                    bounds.Encapsulate(bMax);
                }
            }
            return bounds;
        }

        /// <summary>
        /// Composites a sorted list of body sprites onto a canvas using src-over alpha
        /// blending.  <paramref name="anchorAdjust"/> is subtracted from each sprite's
        /// world position before computing canvas offsets (used to align body sprites with
        /// the face/hair coordinate frame for replacesHead=false books).
        /// </summary>
        private static byte[] ComposeBodySprites(
            List<(SpriteSet sprSet, Vector3 worldPos)> sprites,
            int canvasW,
            int canvasH,
            Bounds canvasBounds,
            float ppu,
            Vector3 anchorAdjust,
            float worldScale = 1f
        )
        {
            var canvas = BuildBodyCanvas(
                sprites,
                canvasW,
                canvasH,
                canvasBounds,
                ppu,
                anchorAdjust,
                worldScale
            );
            return EncodeCanvasToPng(canvas, canvasW, canvasH);
        }

        /// <summary>
        /// Builds a composited Color[] canvas from a sorted list of body sprites.
        /// Factored out of <see cref="ComposeBodySprites"/> so the compositing step
        /// can be reused independently of PNG encoding.
        /// </summary>
        private static Color[] BuildBodyCanvas(
            List<(SpriteSet sprSet, Vector3 worldPos)> sprites,
            int canvasW,
            int canvasH,
            Bounds canvasBounds,
            float ppu,
            Vector3 anchorAdjust,
            float worldScale = 1f
        )
        {
            // Float Color for accurate src-over alpha compositing.
            var canvas = new Color[canvasW * canvasH]; // transparent black

            foreach (var (ss, worldPos) in sprites)
            {
                Texture2D crop = null;
                try
                {
                    var sprite = ss.sprRenderer.sprite;
                    var adjustedPos = worldPos - anchorAdjust;

                    // Body sprites may have a different pixelsPerUnit than the canvas (which
                    // uses the face/hair sprite PPU), and the character model may be rendered
                    // at a non-unit world scale.  Both factors affect how large the sprite
                    // appears on screen: scale the crop so one world-unit of body content
                    // occupies the same number of canvas pixels as one world-unit of face/hair.
                    float ppuScale = ppu / sprite.pixelsPerUnit * worldScale;
                    var rawCrop = ReadSpriteCrop(sprite);
                    int cropW,
                        cropH;
                    if (Mathf.Abs(ppuScale - 1f) < 0.01f)
                    {
                        crop = rawCrop;
                        cropW = rawCrop.width;
                        cropH = rawCrop.height;
                    }
                    else
                    {
                        cropW = Mathf.Max(1, Mathf.RoundToInt(rawCrop.width * ppuScale));
                        cropH = Mathf.Max(1, Mathf.RoundToInt(rawCrop.height * ppuScale));
                        crop = ScaleTexture(rawCrop, cropW, cropH); // destroys rawCrop
                    }

                    // Canvas offset: world-space bottom-left of the sprite's logical rect,
                    // mapped to pixels on the canvas.  adjustedPos is the renderer's pivot in
                    // the coordinate frame of canvasBounds; sprite.bounds.min (local space)
                    // is scaled to world space by worldScale before conversion.
                    int boundsOffX = Mathf.RoundToInt(
                        (adjustedPos.x + sprite.bounds.min.x * worldScale - canvasBounds.min.x)
                            * ppu
                    );
                    int boundsOffY = Mathf.RoundToInt(
                        (adjustedPos.y + sprite.bounds.min.y * worldScale - canvasBounds.min.y)
                            * ppu
                    );
                    // textureRectOffset is in native sprite pixels; scale to canvas pixels.
                    var texOff = sprite.textureRectOffset;
                    int offsetX = boundsOffX + Mathf.RoundToInt(texOff.x * ppuScale);
                    int offsetY = boundsOffY + Mathf.RoundToInt(texOff.y * ppuScale);

                    // Clamp for partial out-of-canvas sprites (floating-point rounding or
                    // sprites that naturally extend beyond the canvas edge).
                    int srcX0 = Mathf.Max(0, -offsetX);
                    int srcY0 = Mathf.Max(0, -offsetY);
                    int dstX0 = Mathf.Max(0, offsetX);
                    int dstY0 = Mathf.Max(0, offsetY);
                    int blitW = Mathf.Min(cropW - srcX0, canvasW - dstX0);
                    int blitH = Mathf.Min(cropH - srcY0, canvasH - dstY0);

                    if (blitW > 0 && blitH > 0)
                    {
                        var srcPixels = crop.GetPixels(srcX0, srcY0, blitW, blitH);
                        for (int y = 0; y < blitH; y++)
                        {
                            for (int x = 0; x < blitW; x++)
                            {
                                var src = srcPixels[y * blitW + x];
                                int dstIdx = (dstY0 + y) * canvasW + (dstX0 + x);
                                var dst = canvas[dstIdx];

                                // Standard src-over compositing (non-premultiplied alpha).
                                float outA = src.a + dst.a * (1f - src.a);
                                if (outA > 0f)
                                    canvas[dstIdx] = new Color(
                                        (src.r * src.a + dst.r * dst.a * (1f - src.a)) / outA,
                                        (src.g * src.a + dst.g * dst.a * (1f - src.a)) / outA,
                                        (src.b * src.a + dst.b * dst.a * (1f - src.a)) / outA,
                                        outA
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

            return canvas;
        }
    }
}
