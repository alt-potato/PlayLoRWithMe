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
    /// </summary>
    internal static class AppearanceCache
    {
        private static bool _extracted = false;

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
        /// </summary>
        internal static readonly Dictionary<int, (float TiltDeg, float PivotFracX, float PivotFracY, bool HasFrontLayer, bool HidesBackHair, string SkinGender)>
            FashionMeta = new Dictionary<int, (float, float, float, bool, bool, string)>();

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
                const string CacheVersion = "23";
                var versionPath = Path.Combine(CustomizeDir, "_cache_version.txt");

                bool stale =
                    !File.Exists(versionPath)
                    || File.ReadAllText(versionPath).Trim() != CacheVersion;

                if (stale)
                {
                    // Wipe all asset dirs so everything re-extracts with the new layout.
                    foreach (var dir in new[] { CustomizeDir, BookIconDir, FashionBodyDir, FashionBodyFrontDir })
                        if (Directory.Exists(dir))
                            Directory.Delete(dir, recursive: true);
                }

                Extract();
                _extracted = true;

                File.WriteAllText(versionPath, CacheVersion);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[PlayLoRWithMe] AppearanceCache: extraction failed: {ex.Message}"
                );
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
                    catch { /* skip inaccessible indices */ }
                }
            }

            Gather("eyes",      loader.NumberOfEye(),  i => loader.GetEyeResourceSet(i)?.normal);
            Gather("brows",     loader.NumberOfBrow(), i => loader.GetBrowResourceSet(i)?.normal);
            Gather("mouths",    loader.NumberOfMouth(), i => loader.GetMouthResourceSet(i)?.normal);
            Gather("fronthair", loader.NumberOfCustomizingResources(CustomizingLookType.FrontHair),
                                i => loader.GetFrontHairSprite(i));
            Gather("backhair",  loader.NumberOfCustomizingResources(CustomizingLookType.BackHair),
                                i => loader.GetRearHairSprite(i));
            // Heads: only 2 variants; GetHeadSprite has no bounds check so we stop on error.
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    var s = loader.GetHeadSprite(i);
                    if (s != null)
                        entries.Add(("head", i, s));
                }
                catch { break; }
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
            var bodyGos       = new List<GameObject>();
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
            // Coordinate system: for replacesHead=false, body sprites are shifted by
            // AnchorPos so positions are relative to the face-canvas origin.  For
            // replacesHead=true, the prefab root is at world origin — same as face sprites.
            {
                float allMinX = faceHairBounds.min.x;
                float allMaxX = faceHairBounds.max.x;
                float allMaxY = faceHairBounds.max.y;
                foreach (var body in fashionBodies)
                {
                    var anchor = body.ReplacesHead ? Vector3.zero : body.AnchorPos;
                    foreach (var (ss, wpos) in body.Sprites)
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
                bool needsUpdate =
                    allMinX < faceHairBounds.min.x
                    || allMaxX > faceHairBounds.max.x
                    || allMaxY > faceHairBounds.max.y;
                if (needsUpdate)
                {
                    faceHairBounds.SetMinMax(
                        new Vector3(allMinX, faceHairBounds.min.y, faceHairBounds.min.z),
                        new Vector3(allMaxX, allMaxY,              faceHairBounds.max.z));
                    canvasW = Mathf.Max(1, Mathf.RoundToInt(faceHairBounds.size.x * ppu));
                    canvasH = Mathf.Max(1, Mathf.RoundToInt(faceHairBounds.size.y * ppu));
                }

                // Persist the canvas dimensions so the frontend can size the preview box
                // to match the actual canvas aspect ratio instead of using a fixed height.
                File.WriteAllText(
                    Path.Combine(CustomizeDir, "dimensions.json"),
                    $"{{\"w\":{canvasW},\"h\":{canvasH}}}");

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
                float bw = faceHairBounds.size.x, bh = faceHairBounds.size.y;
                // Canonical librarian pivot is at world origin (0, 0) — horizontal
                // center of the face canvas and neck-level on the Y axis.
                float fracX = (bw > 0f) ? Mathf.Clamp01(-faceHairBounds.min.x / bw) : 0.5f;
                float fracY = (bh > 0f) ? Mathf.Clamp01(faceHairBounds.max.y / bh)  : 0.5f;
                foreach (var b in fashionBodies)
                {
                    if (FashionMeta.ContainsKey(b.BookId))
                    {
                        // Second variant for same book — merge HasFrontLayer flag.
                        var existing = FashionMeta[b.BookId];
                        if (b.FrontSprites.Count > 0 && !existing.HasFrontLayer)
                            FashionMeta[b.BookId] = (existing.TiltDeg, existing.PivotFracX,
                                existing.PivotFracY, true, existing.HidesBackHair, existing.SkinGender);
                        continue;
                    }
                    var bxi = Singleton<BookXmlList>.Instance?.GetData(b.BookId);
                    string skinGender = bxi?.gender.ToString() ?? "N";
                    FashionMeta[b.BookId] = (b.PivotRotDeg, fracX, fracY,
                        b.FrontSprites.Count > 0, !b.ReplacesHead && b.HasHood, skinGender);
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
                        $"[PlayLoRWithMe] AppearanceCache: {prefix}[{index}] failed: {ex.Message}"
                    );
                }
            }

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
                        $"[PlayLoRWithMe] AppearanceCache: book thumb {bid} failed: {ex.Message}"
                    );
                }
            }
        }

        // --- Fashion body types ---

        // Per-book body data: sprite list, anchor position for face/hair alignment, and
        // whether the skin replaces the head model (controls canvas strategy).
        private class FashionBookBody
        {
            public int BookId;
            /// <summary>
            /// Gender variant suffix: null for neutral/ungendered, "f" for female, "m" for male.
            /// Used to produce distinct file names (e.g. fashionbodies/123_f.png).
            /// </summary>
            public string Variant = null;
            public bool ReplacesHead;
            // World position of customPivot (or Head SpriteSet renderer) within the
            // instantiated DefaultMotion prefab.  Face/hair sprites are placed at this
            // position in-game, so subtracting it from body sprite world positions brings
            // them into the same coordinate frame as the face/hair canvas.
            public Vector3 AnchorPos;
            // Uniform world-space scale of the character model.  Some books use a
            // non-unit scale (e.g. 0.7, 2.0), which makes sprite.bounds (local space)
            // disagree with the physical size of the sprite on screen.  Every canvas
            // calculation that uses sprite.bounds must multiply by this value.
            public float WorldScale = 1f;
            // Z-axis rotation of customPivot in degrees, normalized to (-180, 180].
            // Positive values are counter-clockwise on screen (Unity left-hand convention);
            // negate when converting to CSS rotate().
            public float PivotRotDeg = 0f;
            // Sprites behind the face overlay (sortingOrder < face threshold).
            public List<(SpriteSet sprSet, Vector3 worldPos)> Sprites
                = new List<(SpriteSet, Vector3)>();
            // Sprites in front of the face overlay (sortingOrder >= face threshold).
            // Empty for replacesHead=true books (face overlay is not shown).
            public List<(SpriteSet sprSet, Vector3 worldPos)> FrontSprites
                = new List<(SpriteSet, Vector3)>();
            // True when the character model contains a CharacterAppearanceType.Hood sprite.
            // The game hides all back hair when any Hood sprite is present
            // (RefreshAppearanceByMotion forcibly deactivates backHair renderers).
            public bool HasHood = false;
        }

        /// <summary>
        /// Loads a single prefab variant and returns a populated FashionBookBody, or null
        /// on failure. Callers add the result to the shared result list.
        /// </summary>
        private static FashionBookBody TryGatherOneBody(
            int bid, BookXmlInfo bxi, string suffix, string variant,
            AssetBundleManagerRemake abm, List<GameObject> instancesToDestroy)
        {
            var skinName = bxi.GetCharacterSkin();
            var prefab = abm.LoadCharacterPrefab_DefaultMotion(skinName, suffix, out string _);
            if (prefab == null) return null;

            var go = UnityEngine.Object.Instantiate(prefab);
            var motion = go.GetComponentInChildren<CharacterMotion>();
            if (motion == null) { UnityEngine.Object.Destroy(go); return null; }

            motion.SetSkinSprite(enable: true);
            motion.DisableSpritesByCustomizing(isLibrarian: true);

            Vector3 anchorPos;
            if (motion.customPivot != null)
                anchorPos = motion.customPivot.position;
            else
            {
                var headSet = motion.motionSpriteSet.Find(
                    ss => ss.sprType == CharacterAppearanceType.Head);
                anchorPos = headSet?.sprRenderer?.transform?.position ?? Vector3.zero;
            }

            float rawZ = motion.customPivot != null
                ? motion.customPivot.rotation.eulerAngles.z
                : 0f;
            float pivotRotDeg = rawZ > 180f ? rawZ - 360f : rawZ;

            var body = new FashionBookBody
            {
                BookId       = bid,
                Variant      = variant,
                ReplacesHead = bxi.skinType != "Lor",
                AnchorPos    = anchorPos,
                WorldScale   = motion.transform.lossyScale.y,
                PivotRotDeg  = pivotRotDeg,
            };

            // Split enabled sprites into behind-face and in-front-of-face groups.
            int faceLayerIdx = -1, faceOrder = int.MaxValue;
            if (!body.ReplacesHead)
            {
                var slayers = SortingLayer.layers;
                var layerIdxMap = new Dictionary<int, int>(slayers.Length);
                for (int li = 0; li < slayers.Length; li++)
                    layerIdxMap[slayers[li].id] = li;

                foreach (var ss in motion.motionSpriteSet)
                {
                    if (ss.sprRenderer == null
                        || ss.sprRenderer.enabled
                        || ss.sprRenderer.sprite == null
                        || ss.sprType != CharacterAppearanceType.Head)
                        continue;
                    int idx = layerIdxMap.TryGetValue(ss.sprRenderer.sortingLayerID, out var v) ? v : 0;
                    if (idx > faceLayerIdx || (idx == faceLayerIdx && ss.sprRenderer.sortingOrder > faceOrder))
                    {
                        faceLayerIdx = idx;
                        faceOrder = ss.sprRenderer.sortingOrder;
                    }
                }

                foreach (var ss in motion.motionSpriteSet)
                {
                    if (ss.sprRenderer == null
                        || !ss.sprRenderer.enabled
                        || ss.sprRenderer.sprite == null)
                        continue;
                    var entry = (ss, ss.sprRenderer.transform.position);
                    bool inFront = false;
                    if (faceLayerIdx >= 0)
                    {
                        int idx = layerIdxMap.TryGetValue(ss.sprRenderer.sortingLayerID, out var v) ? v : 0;
                        inFront = idx > faceLayerIdx
                            || (idx == faceLayerIdx && ss.sprRenderer.sortingOrder > faceOrder);
                    }
                    if (inFront)
                        body.FrontSprites.Add(entry);
                    else
                        body.Sprites.Add(entry);

                    if (ss.sprType == CharacterAppearanceType.Hood)
                        body.HasHood = true;
                }
            }
            else
            {
                foreach (var ss in motion.motionSpriteSet)
                {
                    if (ss.sprRenderer == null
                        || !ss.sprRenderer.enabled
                        || ss.sprRenderer.sprite == null)
                        continue;
                    body.Sprites.Add((ss, ss.sprRenderer.transform.position));
                }
            }

            if (body.Sprites.Count + body.FrontSprites.Count > 0)
            {
                instancesToDestroy.Add(go);
                return body;
            }
            UnityEngine.Object.Destroy(go);
            return null;
        }

        /// <summary>
        /// Instantiates the DefaultMotion prefab for each unlocked fashion book, runs
        /// SetSkinSprite + DisableSpritesByCustomizing to isolate body-only sprites, and
        /// records all enabled SpriteRenderers alongside their world positions.
        /// Populates <paramref name="instancesToDestroy"/> with objects the caller must
        /// destroy after extraction is complete.
        /// </summary>
        /// <summary>
        /// Gathers body sprites for a single book ID, handling gendered variants.
        /// </summary>
        private static void GatherBookBody(
            int bid, List<FashionBookBody> result,
            List<GameObject> instancesToDestroy,
            AssetBundleManagerRemake abm, HashSet<int> seen)
        {
            var bxi = Singleton<BookXmlList>.Instance?.GetData(bid);
            GatherBookBody(bid, bxi, result, instancesToDestroy, abm, seen);
        }

        private static void GatherBookBody(
            int bid, BookXmlInfo bxi, List<FashionBookBody> result,
            List<GameObject> instancesToDestroy,
            AssetBundleManagerRemake abm, HashSet<int> seen)
        {
            if (!seen.Add(bid)) return; // already gathered this book ID

            if (bxi == null) return;
            if (string.IsNullOrEmpty(bxi.GetCharacterSkin())) return;

            if (bxi.gender == Gender.N || bxi.gender == Gender.Creature || bxi.gender == Gender.EGO)
            {
                var body = TryGatherOneBody(bid, bxi, "_N", null, abm, instancesToDestroy);
                if (body == null)
                    body = TryGatherOneBody(bid, bxi, "", null, abm, instancesToDestroy);
                if (body != null)
                    result.Add(body);
            }
            else
            {
                var bodyF = TryGatherOneBody(bid, bxi, "_F", "f", abm, instancesToDestroy);
                if (bodyF != null) result.Add(bodyF);
                var bodyM = TryGatherOneBody(bid, bxi, "_M", "m", abm, instancesToDestroy);
                if (bodyM != null) result.Add(bodyM);
            }
        }

        private static void GatherFashionBodies(
            List<FashionBookBody> result,
            List<GameObject> instancesToDestroy
        )
        {
            var abm = Singleton<AssetBundleManagerRemake>.Instance;
            if (abm == null) return;

            var seen = new HashSet<int>();

            // Fashion books (custom core book projections).
            var ccbm = Singleton<CustomCoreBookInventoryModel>.Instance;
            if (ccbm != null)
            {
                foreach (var bid in ccbm.GetBookIdList_CustomCoreBook(SephirahType.None, false))
                {
                    try { GatherBookBody(bid, result, instancesToDestroy, abm, seen); }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning(
                            $"[PlayLoRWithMe] AppearanceCache: fashion body gather {bid} failed: {ex.Message}");
                    }
                }
            }

            // Equipped key pages — extract their bodies too so the preview can
            // show the librarian's current appearance when no fashion book is selected.
            var library = LibraryModel.Instance;
            if (library != null)
            {
                foreach (var floor in library.GetOpenedFloorList())
                {
                    foreach (var unit in floor.GetUnitDataList())
                    {
                        var book = unit.bookItem;
                        if (book?.ClassInfo == null) continue;
                        int bid = book.GetBookClassInfoId().id;
                        try { GatherBookBody(bid, result, instancesToDestroy, abm, seen); }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning(
                                $"[PlayLoRWithMe] AppearanceCache: key page body gather {bid} failed: {ex.Message}");
                        }
                    }
                }
            }


        }

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
            if (fashionBodies.Count == 0) return;
            Directory.CreateDirectory(FashionBodyDir);

            foreach (var body in fashionBodies)
            {
                // gendered variants use a suffix: fashionbodies/123_f.png, 123_m.png
                var suffix = body.Variant != null ? $"_{body.Variant}" : "";
                var path      = Path.Combine(FashionBodyDir,      $"{body.BookId}{suffix}.png");
                var frontPath = Path.Combine(FashionBodyFrontDir, $"{body.BookId}{suffix}.png");

                // Skip if already extracted.  The back file is the sentinel; for the rare
                // case where all sprites are front-layer, use the front file instead.
                bool backDone  = body.Sprites.Count      == 0 || File.Exists(path);
                bool frontDone = body.FrontSprites.Count == 0 || File.Exists(frontPath);
                if (backDone && frontDone) continue;

                try
                {
                    // Sort both lists back-to-front (painter's algorithm).
                    body.Sprites.Sort((a, b) =>
                        a.sprSet.sprRenderer.sortingOrder.CompareTo(b.sprSet.sprRenderer.sortingOrder));
                    body.FrontSprites.Sort((a, b) =>
                        a.sprSet.sprRenderer.sortingOrder.CompareTo(b.sprSet.sprRenderer.sortingOrder));

                    if (body.ReplacesHead)
                    {
                        // Canvas based on the face/hair bounds, extended upward if the body
                        // is taller.  Keeping the same width and bottom padding as face/hair
                        // sprites ensures the character stands at the same visual height as
                        // the librarian customization preview, avoiding the "pressed to the
                        // bottom" effect that a tight per-book canvas would produce.
                        var bodyBounds = ComputeSpriteBounds(body.Sprites, Vector3.zero, body.WorldScale);
                        float extMinX = faceHairBounds.min.x;
                        float extMaxX = faceHairBounds.max.x;
                        float extMinY = faceHairBounds.min.y; // preserve face/hair bottom padding
                        float extMaxY = Mathf.Max(faceHairBounds.max.y, bodyBounds.max.y);
                        var extBounds = new Bounds(
                            new Vector3((extMinX + extMaxX) * 0.5f, (extMinY + extMaxY) * 0.5f, 0f),
                            new Vector3(extMaxX - extMinX, extMaxY - extMinY, 0.2f));
                        int bW = Mathf.Max(1, Mathf.RoundToInt((extMaxX - extMinX) * ppu));
                        int bH = Mathf.Max(1, Mathf.RoundToInt((extMaxY - extMinY) * ppu));
                        if (!backDone)
                            File.WriteAllBytes(path,
                                ComposeBodySprites(body.Sprites, bW, bH, extBounds, ppu, Vector3.zero, body.WorldScale));
                        // replacesHead=true → face overlay never shown; no front layer needed.
                    }
                    else
                    {
                        // The face/hair composite is placed at customPivot (AnchorPos) in-game,
                        // so body sprite positions in face-canvas space = worldPos - AnchorPos.
                        // Using AnchorPos directly as the anchor gives correct alignment for all
                        // books regardless of world scale — no per-book correction is needed.
                        var anchor = body.AnchorPos;

                        // Extend canvas downward to cover all sprites (back + front).  Use the
                        // visible sprite bottom rather than the logical rect to avoid inflating
                        // extH with transparent padding.
                        float extMaxY = faceHairBounds.max.y; // must match face/hair for alignment
                        float visMinYFace = faceHairBounds.min.y;
                        foreach (var spriteList in new[]
                            { body.Sprites, body.FrontSprites })
                        {
                            foreach (var (ss, wpos) in spriteList)
                            {
                                var spr = ss.sprRenderer.sprite;
                                float visBtm = wpos.y + spr.bounds.min.y * body.WorldScale
                                    + spr.textureRectOffset.y / spr.pixelsPerUnit * body.WorldScale;
                                float visBtmFace = visBtm - anchor.y;
                                if (visBtmFace < visMinYFace)
                                    visMinYFace = visBtmFace;
                            }
                        }
                        float extMinY = Mathf.Min(faceHairBounds.min.y, visMinYFace);
                        var extBounds = new Bounds(
                            new Vector3(faceHairBounds.center.x, (extMinY + extMaxY) * 0.5f, 0f),
                            new Vector3(faceHairBounds.size.x, extMaxY - extMinY, 0.2f));
                        int extH = Mathf.Max(1, Mathf.RoundToInt((extMaxY - extMinY) * ppu));

                        if (!backDone)
                            File.WriteAllBytes(path,
                                ComposeBodySprites(body.Sprites, faceHairW, extH, extBounds, ppu, anchor, body.WorldScale));

                        if (!frontDone)
                        {
                            Directory.CreateDirectory(FashionBodyFrontDir);
                            File.WriteAllBytes(frontPath,
                                ComposeBodySprites(body.FrontSprites, faceHairW, extH, extBounds, ppu, anchor, body.WorldScale));
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning(
                        $"[PlayLoRWithMe] AppearanceCache: fashion body {body.BookId} failed: {ex.Message}"
                    );
                }
            }
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
                var adj  = wpos - anchorAdjust;
                var spr  = ss.sprRenderer.sprite;
                var bMin = new Vector3(adj.x + spr.bounds.min.x * worldScale, adj.y + spr.bounds.min.y * worldScale, 0);
                var bMax = new Vector3(adj.x + spr.bounds.max.x * worldScale, adj.y + spr.bounds.max.y * worldScale, 0);
                if (first) { bounds = new Bounds((bMin + bMax) * 0.5f, bMax - bMin); first = false; }
                else       { bounds.Encapsulate(bMin); bounds.Encapsulate(bMax); }
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
            // Float Color for accurate src-over alpha compositing.
            var canvas = new Color[canvasW * canvasH]; // transparent black

            foreach (var (ss, worldPos) in sprites)
            {
                var sprite      = ss.sprRenderer.sprite;
                var adjustedPos = worldPos - anchorAdjust;

                // Body sprites may have a different pixelsPerUnit than the canvas (which
                // uses the face/hair sprite PPU), and the character model may be rendered
                // at a non-unit world scale.  Both factors affect how large the sprite
                // appears on screen: scale the crop so one world-unit of body content
                // occupies the same number of canvas pixels as one world-unit of face/hair.
                float ppuScale = ppu / sprite.pixelsPerUnit * worldScale;
                var rawCrop = ReadSpriteCrop(sprite);
                Texture2D crop;
                int cropW, cropH;
                if (Mathf.Abs(ppuScale - 1f) < 0.01f)
                {
                    crop  = rawCrop;
                    cropW = rawCrop.width;
                    cropH = rawCrop.height;
                }
                else
                {
                    cropW = Mathf.Max(1, Mathf.RoundToInt(rawCrop.width  * ppuScale));
                    cropH = Mathf.Max(1, Mathf.RoundToInt(rawCrop.height * ppuScale));
                    crop  = ScaleTexture(rawCrop, cropW, cropH); // destroys rawCrop
                }

                // Canvas offset: world-space bottom-left of the sprite's logical rect,
                // mapped to pixels on the canvas.  adjustedPos is the renderer's pivot in
                // the coordinate frame of canvasBounds; sprite.bounds.min (local space)
                // is scaled to world space by worldScale before conversion.
                int boundsOffX = Mathf.RoundToInt(
                    (adjustedPos.x + sprite.bounds.min.x * worldScale - canvasBounds.min.x) * ppu);
                int boundsOffY = Mathf.RoundToInt(
                    (adjustedPos.y + sprite.bounds.min.y * worldScale - canvasBounds.min.y) * ppu);
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
                                    outA);
                        }
                    }
                }

                UnityEngine.Object.Destroy(crop);
            }

            var tex = new Texture2D(canvasW, canvasH, TextureFormat.RGBA32, false);
            tex.SetPixels(canvas);
            tex.Apply();
            return tex.EncodeToPNG();
        }

        /// <summary>
        /// Scales <paramref name="src"/> to <paramref name="targetW"/>×<paramref name="targetH"/>
        /// using a RenderTexture blit (bilinear filtering).
        /// Destroys <paramref name="src"/> and returns the new texture.
        /// </summary>
        private static Texture2D ScaleTexture(Texture2D src, int targetW, int targetH)
        {
            var rt = RenderTexture.GetTemporary(targetW, targetH, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var scaled = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
            scaled.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
            scaled.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(src);
            return scaled;
        }

        /// <summary>
        /// Reads the sprite's atlas sub-region into a new Texture2D via RenderTexture
        /// readback.  Caller is responsible for destroying the returned texture.
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

            var origFilter = src.filterMode;
            src.filterMode = FilterMode.Point;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            src.filterMode = origFilter;

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            // ReadPixels uses top-left origin (DX11); textureRect uses bottom-left, so flip Y.
            int flippedY = src.height - y1;
            var crop = new Texture2D(cropW, cropH, TextureFormat.RGBA32, false);
            crop.ReadPixels(new Rect(x0, flippedY, cropW, cropH), 0, 0);
            crop.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            return crop;
        }

        /// <summary>
        /// Converts a sprite (assumed to be a simple full-rectangle crop, not atlas-shared)
        /// to a PNG byte array via RenderTexture readback.
        /// </summary>
        private static byte[] SpriteToSimplePng(Sprite sprite)
        {
            var crop = ReadSpriteCrop(sprite);
            var result = crop.EncodeToPNG();
            UnityEngine.Object.Destroy(crop);
            return result;
        }

        // Mirrors IconCache.SpriteToPng, but positions the sprite crop within a shared
        // canvas whose dimensions are derived from the world-space bounding box of all
        // customization sprites. This ensures all layers composite at the correct pixel
        // offset when stacked in the browser.
        private static byte[] SpriteToPng(
            Sprite sprite,
            int canvasW,
            int canvasH,
            Bounds totalBounds,
            float ppu
        )
        {
            var crop = ReadSpriteCrop(sprite);
            int cropW = crop.width;
            int cropH = crop.height;

            // Compute where this sprite's tight crop sits on the shared canvas.
            //
            // sprite.bounds.min is the world-space bottom-left corner of the sprite's
            // LOGICAL rect (including transparent padding) relative to its pivot (0,0).
            // Subtracting totalBounds.min and scaling by ppu gives the pixel offset from
            // the shared canvas origin to the logical rect's bottom-left.
            //
            // textureRectOffset is the additional sub-pixel offset from the logical rect's
            // bottom-left to the tight crop's bottom-left, in pixels.
            int boundsOffsetX = Mathf.RoundToInt(
                (sprite.bounds.min.x - totalBounds.min.x) * ppu
            );
            int boundsOffsetY = Mathf.RoundToInt(
                (sprite.bounds.min.y - totalBounds.min.y) * ppu
            );
            var texRectOffset = sprite.textureRectOffset;
            int offsetX = boundsOffsetX + Mathf.RoundToInt(texRectOffset.x);
            int offsetY = boundsOffsetY + Mathf.RoundToInt(texRectOffset.y);

            // Fast path: the crop fills the entire shared canvas with no offset.
            if (canvasW == cropW && canvasH == cropH && offsetX == 0 && offsetY == 0)
            {
                var result = crop.EncodeToPNG();
                UnityEngine.Object.Destroy(crop);
                return result;
            }

            var dst = new Texture2D(canvasW, canvasH, TextureFormat.RGBA32, false);
            dst.SetPixels32(new Color32[canvasW * canvasH]); // transparent fill

            // Clamp to valid range in case of floating-point rounding edge cases.
            int safeW = Mathf.Clamp(cropW, 0, canvasW - offsetX);
            int safeH = Mathf.Clamp(cropH, 0, canvasH - offsetY);
            if (safeW > 0 && safeH > 0)
                dst.SetPixels32(offsetX, offsetY, safeW, safeH, crop.GetPixels32());

            UnityEngine.Object.Destroy(crop);
            dst.Apply();
            return dst.EncodeToPNG();
        }
    }
}
