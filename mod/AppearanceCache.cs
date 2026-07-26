using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Extracts appearance customization sprites (hair, eyes, mouths, etc.) to
    /// wwwroot/assets/customize/ as indexed PNGs used by the frontend preview.
    /// Also extracts fashion book body composites to wwwroot/assets/fashionbodies/
    /// (sprites behind the face overlay) and wwwroot/assets/fashionbodies_front/
    /// (sprites in front of the face overlay) so the preview renders the correct
    /// depth ordering.
    /// Called once after the main library scene loads; subsequent calls are no-ops.
    /// This file holds the cache state and the extraction pass orchestration; the
    /// patron heads, fashion bodies and sprite plumbing live in the
    /// AppearanceCache.*.cs partials.
    /// </summary>
    internal static partial class AppearanceCache
    {
        /// <summary>Number of head sprite variants available from the customization loader.</summary>
        private const int HeadVariantCount = 2;

        private static bool _extracted = false;

        /// <summary>Whether sprite extraction has completed successfully.</summary>
        internal static bool IsReady => _extracted;

        /// <summary>
        /// When non-null, <see cref="ReadSpriteCrop"/> caches blitted RenderTextures
        /// per source atlas so repeated reads from the same atlas skip the GPU blit.
        /// Populated at the start of <see cref="Extract"/> and released at the end.
        /// </summary>
        private static Dictionary<Texture2D, RenderTexture> _atlasRtCache;

        /// <summary>
        /// Per-book metadata populated during extraction and read by
        /// <see cref="GameStateSerializer"/> when serializing fashion books.
        /// TiltDeg is the Z-axis rotation of customPivot (positive = counter-clockwise on
        /// screen; negate for CSS rotate()).  PivotFracX / PivotFracY are the pivot's position as
        /// fractions of the canvas [0,1] from left and top respectively.
        /// HasFrontLayer is true when fashionbodies_front/{id}.png was extracted (some
        /// body sprites render in front of the face overlay in-game).
        /// HidesBackHair is true when the character model has a Hood sprite; the game
        /// hides all back hair renderers in that case.
        /// FeetYFrac is the vertical position of the character's feet within the body PNG,
        /// as a fraction [0,1] from the top — 1.0 means feet sit at the PNG bottom, &lt;1.0
        /// means the PNG extends below feet (weapons/props).  Used by the frontend to pin
        /// feet to a shared floor line when scaling.  Populated during extraction; defaults
        /// to 1.0 if the body has no sprites extending below feet.
        /// BodyW / BodyH are the pixel dimensions of the extracted body PNG, recorded so the
        /// frontend can lay out the preview without waiting on @load events to measure the
        /// image — required to avoid a feet-snap on first paint and on tab switches.
        /// Zero until populated in pass 4.
        /// </summary>
        internal static readonly Dictionary<
            string,
            (
                float TiltDeg,
                float PivotFracX,
                float PivotFracY,
                bool HasFrontLayer,
                bool HidesBackHair,
                string SkinGender,
                float FeetYFrac,
                int BodyW,
                int BodyH
            )
        > FashionMeta =
            new Dictionary<string, (float, float, float, bool, bool, string, float, int, int)>();

        /// <summary>
        /// Face/hair canvas bounds in world space, populated during extraction.
        /// Used by <see cref="GiftCache"/> to convert gift prefab positions to CSS coordinates.
        /// </summary>
        internal static Bounds FaceHairBounds { get; private set; }

        /// <summary>
        /// Pixels-per-unit of the face/hair sprites, populated during extraction.
        /// </summary>
        internal static float FaceHairPpu { get; private set; }

        /// <summary>
        /// Canvas pixel dimensions (computed from bounds × ppu), populated during extraction.
        /// </summary>
        internal static int FaceHairCanvasW { get; private set; }
        internal static int FaceHairCanvasH { get; private set; }

        private static string CustomizeDir =>
            Path.Combine(Server.WwwRootPath, "assets", "customize");

        private static string BookIconDir =>
            Path.Combine(Server.WwwRootPath, "assets", "bookicons");

        private static string FashionBodyDir =>
            Path.Combine(Server.WwwRootPath, "assets", "fashionbodies");

        private static string FashionBodyFrontDir =>
            Path.Combine(Server.WwwRootPath, "assets", "fashionbodies_front");

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
                const string CacheVersion = "35";
                var versionPath = Path.Combine(CustomizeDir, "_cache_version.txt");

                bool stale =
                    !File.Exists(versionPath)
                    || File.ReadAllText(versionPath).Trim() != CacheVersion;

                if (stale)
                {
                    // Wipe all asset dirs so everything re-extracts with the new layout.
                    foreach (
                        var dir in new[]
                        {
                            CustomizeDir,
                            BookIconDir,
                            FashionBodyDir,
                            FashionBodyFrontDir,
                        }
                    )
                        if (Directory.Exists(dir))
                            Directory.Delete(dir, recursive: true);
                }

                Extract();
                _extracted = true;

                File.WriteAllText(versionPath, CacheVersion);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PRWM] AppearanceCache: extraction failed: {ex.Message}");
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

            // Enable atlas blit caching for the duration of extraction. Multiple
            // face/hair sprites share a small number of atlas textures; caching the
            // blitted RenderTexture avoids redundant full-atlas GPU copies.
            _atlasRtCache = new Dictionary<Texture2D, RenderTexture>();
            try
            {
                ExtractInner(loader);
            }
            finally
            {
                foreach (var rt in _atlasRtCache.Values)
                    RenderTexture.ReleaseTemporary(rt);
                _atlasRtCache = null;
            }
        }

        private static void ExtractInner(CustomizingResourceLoader loader)
        {
            // --- Pass 1: gather all face/hair sprites ---
            // Collect every customization sprite so we can compute a shared world-space
            // bounding box before writing any files.
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
                    catch
                    { /* skip inaccessible indices */
                    }
                }
            }

            Gather("eyes", loader.NumberOfEye(), i => loader.GetEyeResourceSet(i)?.normal);
            Gather("brows", loader.NumberOfBrow(), i => loader.GetBrowResourceSet(i)?.normal);
            Gather("mouths", loader.NumberOfMouth(), i => loader.GetMouthResourceSet(i)?.normal);
            Gather(
                "fronthair",
                loader.NumberOfCustomizingResources(CustomizingLookType.FrontHair),
                i => loader.GetFrontHairSprite(i)
            );
            Gather(
                "backhair",
                loader.NumberOfCustomizingResources(CustomizingLookType.BackHair),
                i => loader.GetRearHairSprite(i)
            );
            // Heads: GetHeadSprite has no bounds check so we stop on error.
            for (int i = 0; i < HeadVariantCount; i++)
            {
                try
                {
                    var s = loader.GetHeadSprite(i);
                    if (s != null)
                        entries.Add(("head", i, s));
                }
                catch
                {
                    break;
                }
            }

            if (entries.Count == 0)
                return;

            ExtractBookThumbnails();

            // --- Pass 2: compute shared canvas from face/hair bounds only ---
            // These sprites are all rendered at the character's root transform, so their
            // bounds are directly comparable.  Body sprites from fashion books are NOT
            // included here — each fashion book gets its own canvas (see Pass 4).
            var faceHairBounds = entries[0].sprite.bounds;
            float ppu = entries[0].sprite.pixelsPerUnit;
            foreach (var (_, _, sp) in entries)
                faceHairBounds.Encapsulate(sp.bounds);

            int canvasW = Mathf.Max(1, Mathf.RoundToInt(faceHairBounds.size.x * ppu));
            int canvasH = Mathf.Max(1, Mathf.RoundToInt(faceHairBounds.size.y * ppu));

            // --- Pass 2b: gather body sprites from fashion book prefabs ---
            // Done before Pass 3 so body-sprite GameObjects can be destroyed after
            // Pass 3, but gathered here to keep Unity instantiation on the main thread.
            var fashionBodies = new List<FashionBookBody>();
            var bodyGos = new List<GameObject>();
            GatherFashionBodies(fashionBodies, bodyGos);

            // --- Pass 2c: expand shared canvas to cover all body sprite extents ---
            // All PNGs (face/hair AND body composites) must share the same pixel width so
            // that CSS background-size: 100% auto scales them identically and layers stay
            // aligned.  We also expand the top (Y-max) so sprites above the face canvas
            // (e.g. tall hats) are not clipped — all PNGs share the same extMaxY so the
            // top-aligned CSS positioning keeps face and body layers in sync.
            //
            // We use SpriteRenderer.bounds (world-space AABB) rather than sprite.bounds *
            // worldScale so that rotated sprites (e.g. the tilted head on Bamboo-hatted
            // Kim's Page) contribute their full visual extent to the canvas.
            //
            // replacesHead=false bodies participate in the shared canvas because their
            // sprites layer together with the face/hair PNGs (CSS background-size:
            // 100% auto requires a shared pixel width so layers align).  replacesHead=true
            // bodies are rendered alone (face/hair hidden) and get their own tight
            // per-body canvas in ExtractFashionBodies — they must not inflate the shared
            // canvas, which would push face/hair sprites off-center for every librarian.
            {
                float allMinX = faceHairBounds.min.x;
                float allMaxX = faceHairBounds.max.x;
                float allMaxY = faceHairBounds.max.y;
                foreach (var body in fashionBodies)
                {
                    if (body.ReplacesHead)
                        continue;
                    var anchor = body.AnchorPos;
                    foreach (var spriteList in new[] { body.Sprites, body.SkinSprites })
                    {
                        foreach (var (ss, wpos) in spriteList)
                        {
                            // World-space AABB from the renderer — accounts for any transform
                            // rotation applied to the sprite at runtime.
                            var rb = ss.sprRenderer.bounds;
                            float relCx = rb.center.x - anchor.x;
                            allMinX = Mathf.Min(allMinX, relCx - rb.extents.x);
                            allMaxX = Mathf.Max(allMaxX, relCx + rb.extents.x);
                            allMaxY = Mathf.Max(allMaxY, rb.max.y - anchor.y);
                        }
                    }
                }
                bool needsUpdate =
                    allMinX < faceHairBounds.min.x
                    || allMaxX > faceHairBounds.max.x
                    || allMaxY > faceHairBounds.max.y;
                if (needsUpdate)
                {
                    faceHairBounds.SetMinMax(
                        new Vector3(allMinX, faceHairBounds.min.y, faceHairBounds.min.z),
                        new Vector3(allMaxX, allMaxY, faceHairBounds.max.z)
                    );
                    canvasW = Mathf.Max(1, Mathf.RoundToInt(faceHairBounds.size.x * ppu));
                    canvasH = Mathf.Max(1, Mathf.RoundToInt(faceHairBounds.size.y * ppu));
                }

                // Expose canvas data so GiftCache can render gifts onto the same canvas.
                // (Previously also persisted to dimensions.json for the frontend to fetch
                // after mount, but that introduced a head-snap on every remount; the
                // canvas dims are now emitted inline in the customizeOptions state.)
                FaceHairBounds = faceHairBounds;
                FaceHairPpu = ppu;
                FaceHairCanvasW = canvasW;
                FaceHairCanvasH = canvasH;

                // Build per-book metadata for the serializer now that faceHairBounds is
                // final (pivot fractions depend on the fully-expanded canvas extents).
                //
                // Pivot position: the fashion book only contributes the rotation angle;
                // the librarian's own character model always supplies the customPivot
                // position (world origin = (0,0,0) for all standard librarians).  Using
                // the fashion book prefab's customPivot.position (AnchorPos) would give
                // the wrong pivot for skins whose pivot is placed at the hat brim or
                // other non-neck attachment points.
                FashionMeta.Clear();
                float bw = faceHairBounds.size.x,
                    bh = faceHairBounds.size.y;
                // Canonical librarian pivot is at world origin (0, 0) — horizontal
                // center of the face canvas and neck-level on the Y axis.
                float fracX = (bw > 0f) ? Mathf.Clamp01(-faceHairBounds.min.x / bw) : 0.5f;
                float fracY = (bh > 0f) ? Mathf.Clamp01(faceHairBounds.max.y / bh) : 0.5f;
                foreach (var b in fashionBodies)
                {
                    var stem = b.FileStem;
                    if (FashionMeta.ContainsKey(stem))
                    {
                        // Second variant for same book — merge HasFrontLayer flag.
                        var existing = FashionMeta[stem];
                        if (b.FrontSprites.Count > 0 && !existing.HasFrontLayer)
                            FashionMeta[stem] = (
                                existing.TiltDeg,
                                existing.PivotFracX,
                                existing.PivotFracY,
                                true,
                                existing.HidesBackHair,
                                existing.SkinGender,
                                existing.FeetYFrac,
                                existing.BodyW,
                                existing.BodyH
                            );
                        continue;
                    }
                    string skinGender;
                    if (!string.IsNullOrEmpty(b.PackageId))
                    {
                        // Workshop book: look up by full LorId to get the correct gender.
                        var wsInfo = Singleton<BookXmlList>.Instance?.GetData(
                            new LorId(b.PackageId, b.BookId)
                        );
                        skinGender = wsInfo?.gender.ToString() ?? "N";
                    }
                    else
                    {
                        var bxi = Singleton<BookXmlList>.Instance?.GetData(b.BookId);
                        skinGender = bxi?.gender.ToString() ?? "N";
                    }
                    // Initial placeholder for FeetYFrac; overwritten in pass 4 once
                    // the body PNG's actual extent is known.  1.0 means "feet at PNG bottom"
                    // and is a safe default for bodies whose sprite extents match the feet.
                    // BodyW/BodyH are zero until pass 4 records them.
                    FashionMeta[stem] = (
                        b.PivotRotDeg,
                        fracX,
                        fracY,
                        b.FrontSprites.Count > 0,
                        !b.ReplacesHead && b.HasHood,
                        skinGender,
                        1f,
                        0,
                        0
                    );
                }
            }

            // --- Pass 3: extract each face/hair sprite onto the shared canvas ---
            foreach (var (prefix, index, sprite) in entries)
            {
                try
                {
                    ExtractSprite(prefix, index, sprite, canvasW, canvasH, faceHairBounds, ppu);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning(
                        $"[PRWM] AppearanceCache: {prefix}[{index}] failed: {ex.Message}"
                    );
                }
            }

            // --- Pass 3b: extract patron (special custom) head sprites ---
            // Patron librarians (sephirah IDs 1-10 etc.) use SpecialCustomizedAppearance
            // prefabs with unique head sprites, rather than the shared generic heads.
            // We pick the front-facing standing head from each prefab's SpecialCustomHead list.
            ExtractPatronHeads(canvasW, canvasH, faceHairBounds, ppu);

            // --- Pass 4: composite body sprites per fashion book ---
            ExtractFashionBodies(fashionBodies, faceHairBounds, canvasW, canvasH, ppu);

            foreach (var go in bodyGos)
                UnityEngine.Object.Destroy(go);
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

        /// <summary>
        /// Extracts thumbnail sprites for all unlocked fashion (custom core) books to
        /// wwwroot/assets/bookicons/{id}.png. These are shown in the AppearancePreview
        /// when a fashion projection is selected, so the player can see what skin they
        /// are picking without needing the game's 3D character renderer.
        /// </summary>
        private static void ExtractBookThumbnails()
        {
            Directory.CreateDirectory(BookIconDir);

            var ccbm = Singleton<CustomCoreBookInventoryModel>.Instance;
            if (ccbm == null)
                return;

            var ids = ccbm.GetBookIdList_CustomCoreBook(SephirahType.None, false);
            foreach (var bid in ids)
            {
                var path = Path.Combine(BookIconDir, $"{bid}.png");
                if (File.Exists(path))
                    continue;

                try
                {
                    // Book thumbnails live at "Sprites/Books/Thumb/{id}" in Resources.
                    var sprite = Resources.Load<Sprite>($"Sprites/Books/Thumb/{bid}");
                    if (sprite == null)
                        continue;

                    File.WriteAllBytes(path, SpriteToSimplePng(sprite));
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning(
                        $"[PRWM] AppearanceCache: book thumb {bid} failed: {ex.Message}"
                    );
                }
            }
        }
    }
}
