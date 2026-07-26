using System.Collections.Generic;
using UnityEngine;
using Workshop;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Fashion body gathering -- the per-book FashionBookBody model plus the prefab
    /// loading that collects body sprites for core, workshop and cloth-overlay skins.
    /// </summary>
    internal static partial class AppearanceCache
    {
        // --- Fashion body types ---

        // Per-book body data: sprite list, anchor position for face/hair alignment, and
        // whether the skin replaces the head model (controls canvas strategy).
        private class FashionBookBody
        {
            public int BookId;

            /// <summary>
            /// Non-empty for workshop books; used to disambiguate file names and metadata
            /// keys when multiple mods share the same integer BookId.
            /// </summary>
            public string PackageId = "";

            /// <summary>
            /// Gender variant suffix: null for neutral/ungendered, "f" for female, "m" for male.
            /// Used to produce distinct file names (e.g. fashionbodies/123_f.png).
            /// </summary>
            public string Variant = null;

            /// <summary>
            /// Explicit file stem override. When set, takes priority over the
            /// computed "{PackageId}_{BookId}" or "{BookId}" pattern.
            /// </summary>
            public string FileStemOverride = null;

            /// <summary>
            /// File stem for this entry: explicit override if set, else "{BookId}" for
            /// core books, "{PackageId}_{BookId}" for workshop books.
            /// </summary>
            public string FileStem =>
                FileStemOverride
                ?? (string.IsNullOrEmpty(PackageId) ? BookId.ToString() : $"{PackageId}_{BookId}");
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
            public List<(SpriteSet sprSet, Vector3 worldPos)> Sprites =
                new List<(SpriteSet, Vector3)>();

            // Sprites in front of the face overlay (sortingOrder >= face threshold).
            // Empty for replacesHead=true books (face overlay is not shown).
            public List<(SpriteSet sprSet, Vector3 worldPos)> FrontSprites =
                new List<(SpriteSet, Vector3)>();

            // Skin-type sprites (neck, collarbone) — extracted separately so the
            // frontend can tint them with the librarian's individual skin color via
            // CSS multiply blending.  In-game, these are white silhouettes tinted at
            // runtime by CharacterMotion.CustomizeSkinColor().
            public List<(SpriteSet sprSet, Vector3 worldPos)> SkinSprites =
                new List<(SpriteSet, Vector3)>();

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
            int bid,
            BookXmlInfo bxi,
            string suffix,
            string variant,
            AssetBundleManagerRemake abm,
            List<GameObject> instancesToDestroy
        )
        {
            var skinName = bxi.GetCharacterSkin();
            var prefab = abm.LoadCharacterPrefab_DefaultMotion(skinName, suffix, out string _);
            if (prefab == null)
                return null;

            var go = UnityEngine.Object.Instantiate(prefab);
            var motion = go.GetComponentInChildren<CharacterMotion>();
            if (motion == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }

            motion.SetSkinSprite(enable: true);
            motion.DisableSpritesByCustomizing(isLibrarian: true);

            Vector3 anchorPos;
            if (motion.customPivot != null)
                anchorPos = motion.customPivot.position;
            else
            {
                var headSet = motion.motionSpriteSet.Find(ss =>
                    ss.sprType == CharacterAppearanceType.Head
                );
                anchorPos = headSet?.sprRenderer?.transform?.position ?? Vector3.zero;
            }

            float rawZ =
                motion.customPivot != null ? motion.customPivot.rotation.eulerAngles.z : 0f;
            float pivotRotDeg = rawZ > 180f ? rawZ - 360f : rawZ;

            var body = new FashionBookBody
            {
                BookId = bid,
                Variant = variant,
                ReplacesHead = bxi.skinType != "Lor",
                AnchorPos = anchorPos,
                WorldScale = motion.transform.lossyScale.y,
                PivotRotDeg = pivotRotDeg,
            };

            // Split enabled sprites into behind-face and in-front-of-face groups.
            int faceLayerIdx = -1,
                faceOrder = int.MaxValue;
            if (!body.ReplacesHead)
            {
                var slayers = SortingLayer.layers;
                var layerIdxMap = new Dictionary<int, int>(slayers.Length);
                for (int li = 0; li < slayers.Length; li++)
                    layerIdxMap[slayers[li].id] = li;

                foreach (var ss in motion.motionSpriteSet)
                {
                    if (
                        ss.sprRenderer == null
                        || ss.sprRenderer.enabled
                        || ss.sprRenderer.sprite == null
                        || ss.sprType != CharacterAppearanceType.Head
                    )
                        continue;
                    int idx = layerIdxMap.TryGetValue(ss.sprRenderer.sortingLayerID, out var v)
                        ? v
                        : 0;
                    if (
                        idx > faceLayerIdx
                        || (idx == faceLayerIdx && ss.sprRenderer.sortingOrder > faceOrder)
                    )
                    {
                        faceLayerIdx = idx;
                        faceOrder = ss.sprRenderer.sortingOrder;
                    }
                }

                foreach (var ss in motion.motionSpriteSet)
                {
                    if (
                        ss.sprRenderer == null
                        || !ss.sprRenderer.enabled
                        || ss.sprRenderer.sprite == null
                    )
                        continue;
                    var entry = (ss, ss.sprRenderer.transform.position);
                    bool inFront = false;
                    if (faceLayerIdx >= 0)
                    {
                        int idx = layerIdxMap.TryGetValue(ss.sprRenderer.sortingLayerID, out var v)
                            ? v
                            : 0;
                        inFront =
                            idx > faceLayerIdx
                            || (idx == faceLayerIdx && ss.sprRenderer.sortingOrder > faceOrder);
                    }
                    if (ss.sprType == CharacterAppearanceType.Skin)
                        body.SkinSprites.Add(entry);
                    else if (inFront)
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
                    if (
                        ss.sprRenderer == null
                        || !ss.sprRenderer.enabled
                        || ss.sprRenderer.sprite == null
                    )
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
        /// Extracts body sprites for a workshop book that uses <c>skinType == "Custom"</c>.
        /// These books don't have their own character prefab; instead the game loads
        /// <c>[Prefab]Appearance_Custom</c> and applies cloth-overlay sprites via
        /// <see cref="WorkshopSkinDataSetter"/>.  The cloth sprites live on child
        /// GameObjects named <c>Customize_Renderer</c> (rear) and
        /// <c>Customize_Renderer_Front</c> (front), not in <c>motionSpriteSet</c>.
        /// </summary>
        /// <summary>
        /// Resolves <see cref="WorkshopSkinData"/> for a workshop book and delegates
        /// to <see cref="TryGatherClothBody"/>.
        /// </summary>
        private static FashionBookBody TryGatherWorkshopBody(
            int bid,
            BookXmlInfo bxi,
            string packageId,
            List<GameObject> instancesToDestroy
        )
        {
            var loader = Singleton<CustomizingBookSkinLoader>.Instance;
            if (loader == null)
            {
                Debug.LogWarning($"[PRWM] workshop body {bid}: CustomizingBookSkinLoader is null");
                return null;
            }
            var skinName = bxi.GetCharacterSkin();
            var skinData = loader.GetWorkshopBookSkinData(packageId, skinName);
            if (skinData == null)
            {
                Debug.LogWarning(
                    $"[PRWM] workshop body {bid}: no skin data for pkg={packageId} skin={skinName}"
                );
                return null;
            }
            return TryGatherClothBody(skinData, bid, packageId, $"book {bid}", instancesToDestroy);
        }

        /// <summary>
        /// Extracts body sprites from a <see cref="WorkshopSkinData"/> cloth overlay.
        /// Used for both workshop book bodies (<c>skinType == "Custom"</c>) and
        /// workshop skins from <see cref="CustomizingResourceLoader"/>.
        /// The cloth sprites live on child GameObjects named
        /// <c>Customize_Renderer</c> (rear) and <c>Customize_Renderer_Front</c> (front),
        /// not in <c>motionSpriteSet</c>.
        /// </summary>
        private static FashionBookBody TryGatherClothBody(
            WorkshopSkinData skinData,
            int bid,
            string packageId,
            string label,
            List<GameObject> instancesToDestroy
        )
        {
            var prefab = Resources.Load<GameObject>("Prefabs/Characters/[Prefab]Appearance_Custom");
            if (prefab == null)
            {
                Debug.LogWarning($"[PRWM] cloth body {label}: Appearance_Custom prefab not found");
                return null;
            }

            var go = UnityEngine.Object.Instantiate(prefab);
            var setter = go.GetComponent<WorkshopSkinDataSetter>();
            if (setter == null)
            {
                Debug.LogWarning($"[PRWM] cloth body {label}: no WorkshopSkinDataSetter on prefab");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            setter.SetData(skinData);

            // Get the Standing motion (same as Default for workshop skins per Init()).
            var appearance = go.GetComponent<CharacterAppearance>();
            if (appearance == null)
            {
                Debug.LogWarning($"[PRWM] cloth body {label}: no CharacterAppearance on prefab");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            var motion = appearance.GetCharacterMotion(ActionDetail.Standing);
            if (motion == null)
                motion = appearance.GetCharacterMotion(ActionDetail.Default);
            if (motion == null)
            {
                Debug.LogWarning($"[PRWM] cloth body {label}: no Standing/Default motion found");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            // Find the cloth renderers and pivot on the Standing motion.
            SpriteRenderer rearRenderer = null,
                frontRenderer = null;
            Transform customPivot = null;
            foreach (Transform child in motion.transform)
            {
                if (child.gameObject.name == "Customize_Renderer")
                    rearRenderer = child.GetComponent<SpriteRenderer>();
                else if (child.gameObject.name == "Customize_Renderer_Front")
                    frontRenderer = child.GetComponent<SpriteRenderer>();
                else if (child.gameObject.name == "CustomizePivot")
                    customPivot = child;
            }

            if (rearRenderer == null || rearRenderer.sprite == null)
            {
                Debug.LogWarning(
                    $"[PRWM] cloth body {label}: rear renderer={rearRenderer != null}, sprite={rearRenderer?.sprite != null}"
                );
                UnityEngine.Object.Destroy(go);
                return null;
            }

            Vector3 anchorPos = customPivot != null ? customPivot.position : Vector3.zero;

            float rawZ = customPivot != null ? customPivot.rotation.eulerAngles.z : 0f;
            float pivotRotDeg = rawZ > 180f ? rawZ - 360f : rawZ;

            // headEnabled in the skin data means the head/face should be visible
            // on top of the body sprite — i.e. the skin does NOT replace the head.
            bool headEnabled = true;
            if (skinData.dic.TryGetValue(ActionDetail.Default, out var defaultCloth))
                headEnabled = defaultCloth.headEnabled;
            else if (skinData.dic.TryGetValue(ActionDetail.Standing, out var standingCloth))
                headEnabled = standingCloth.headEnabled;

            var body = new FashionBookBody
            {
                BookId = bid,
                PackageId = packageId,
                Variant = null, // workshop skins are ungendered
                ReplacesHead = !headEnabled,
                AnchorPos = anchorPos,
                WorldScale = motion.transform.lossyScale.y,
                PivotRotDeg = pivotRotDeg,
            };

            // Rear cloth sprite → behind-face layer.
            var rearSet = new SpriteSet(rearRenderer, CharacterAppearanceType.Body);
            body.Sprites.Add((rearSet, rearRenderer.transform.position));

            // Front cloth sprite → in-front-of-face layer.
            if (
                frontRenderer != null
                && frontRenderer.sprite != null
                && frontRenderer.gameObject.activeSelf
            )
            {
                var frontSet = new SpriteSet(frontRenderer, CharacterAppearanceType.Body);
                body.FrontSprites.Add((frontSet, frontRenderer.transform.position));
            }

            Debug.Log(
                $"[PRWM] workshop body {bid}: OK, rear sprite={rearRenderer.sprite.name}, front={frontRenderer?.sprite?.name ?? "none"}"
            );
            instancesToDestroy.Add(go);
            return body;
        }

        /// <summary>
        /// Gathers body sprites for a single book ID, handling gendered variants.
        /// </summary>
        private static void GatherBookBody(
            int bid,
            List<FashionBookBody> result,
            List<GameObject> instancesToDestroy,
            AssetBundleManagerRemake abm,
            HashSet<int> seen
        )
        {
            var bxi = Singleton<BookXmlList>.Instance?.GetData(bid);
            GatherBookBody(bid, bxi, result, instancesToDestroy, abm, seen);
        }

        private static void GatherBookBody(
            int bid,
            BookXmlInfo bxi,
            List<FashionBookBody> result,
            List<GameObject> instancesToDestroy,
            AssetBundleManagerRemake abm,
            HashSet<int> seen
        )
        {
            if (!seen.Add(bid))
                return; // already gathered this book ID

            if (bxi == null)
                return;
            if (string.IsNullOrEmpty(bxi.GetCharacterSkin()))
                return;

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
                if (bodyF != null)
                    result.Add(bodyF);
                var bodyM = TryGatherOneBody(bid, bxi, "_M", "m", abm, instancesToDestroy);
                if (bodyM != null)
                    result.Add(bodyM);
            }
        }

        private static void GatherFashionBodies(
            List<FashionBookBody> result,
            List<GameObject> instancesToDestroy
        )
        {
            var abm = Singleton<AssetBundleManagerRemake>.Instance;
            if (abm == null)
                return;

            var seen = new HashSet<int>();
            // Separate dedup set for workshop books keyed by "{packageId}:{id}" to avoid
            // collisions with core books and between mods that share the same integer id.
            var seenWs = new HashSet<string>();

            // Fashion books (custom core book projections).
            var ccbm = Singleton<CustomCoreBookInventoryModel>.Instance;
            if (ccbm != null)
            {
                foreach (var bid in ccbm.GetBookIdList_CustomCoreBook(SephirahType.None, false))
                {
                    try
                    {
                        GatherBookBody(bid, result, instancesToDestroy, abm, seen);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning(
                            $"[PRWM] AppearanceCache: fashion body gather {bid} failed: {ex.Message}"
                        );
                    }
                }
            }

            // Equipped key pages and workshop books — extract their bodies too so
            // the preview can show the librarian's current appearance when no
            // fashion book is selected.  Workshop books (IsWorkshop) are included
            // because they aren't tracked by CustomCoreBookInventoryModel.
            var library = LibraryModel.Instance;
            if (library != null)
            {
                foreach (var floor in library.GetOpenedFloorList())
                {
                    foreach (var unit in floor.GetUnitDataList())
                    {
                        var book = unit.bookItem;
                        if (book?.ClassInfo == null)
                            continue;
                        var lid = book.GetBookClassInfoId();
                        int bid = lid.id;
                        var bxi = book.ClassInfo;
                        try
                        {
                            if (book.IsWorkshop && bxi.skinType == "Custom")
                            {
                                if (seenWs.Add($"{lid.packageId}:{lid.id}"))
                                {
                                    var wsBody = TryGatherWorkshopBody(
                                        bid,
                                        bxi,
                                        lid.packageId,
                                        instancesToDestroy
                                    );
                                    if (wsBody != null)
                                        result.Add(wsBody);
                                }
                            }
                            else
                            {
                                GatherBookBody(bid, bxi, result, instancesToDestroy, abm, seen);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning(
                                $"[PRWM] AppearanceCache: key page body gather {bid} failed: {ex.Message}"
                            );
                        }
                    }
                }
            }

            // Workshop mod books from the book inventory — these have standard
            // character prefabs but are excluded from CustomCoreBookInventoryModel.
            var bookInv = Singleton<BookInventoryModel>.Instance;
            Debug.Log(
                $"[PRWM] AppearanceCache: bookInv={bookInv != null}, count={bookInv?.GetBookListAll()?.Count ?? -1}"
            );
            if (bookInv != null)
            {
                int wsCount = 0;
                foreach (var book in bookInv.GetBookListAll())
                {
                    if (!book.IsWorkshop)
                        continue;
                    wsCount++;
                    var bxi = book.ClassInfo;
                    var lid = book.GetBookClassInfoId();
                    Debug.Log(
                        $"[PRWM] AppearanceCache: ws book id={lid.id} pkg={lid.packageId} skinType={bxi?.skinType} charSkin={bxi?.GetCharacterSkin()} classInfo={bxi != null}"
                    );
                    if (bxi == null || string.IsNullOrEmpty(bxi.GetCharacterSkin()))
                        continue;
                    int bid = lid.id;
                    try
                    {
                        if (bxi.skinType == "Custom")
                        {
                            // Workshop books with custom skins use cloth overlays,
                            // not standard character prefabs.
                            if (!seenWs.Add($"{lid.packageId}:{lid.id}"))
                                continue;
                            Debug.Log(
                                $"[PRWM] AppearanceCache: workshop Custom book {bid} pkg={lid.packageId} skin={bxi.GetCharacterSkin()}"
                            );
                            var wsBody = TryGatherWorkshopBody(
                                bid,
                                bxi,
                                lid.packageId,
                                instancesToDestroy
                            );
                            if (wsBody != null)
                                result.Add(wsBody);
                            else
                                Debug.LogWarning(
                                    $"[PRWM] AppearanceCache: TryGatherWorkshopBody returned null for {bid}"
                                );
                        }
                        else
                        {
                            GatherBookBody(bid, bxi, result, instancesToDestroy, abm, seen);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning(
                            $"[PRWM] AppearanceCache: workshop body gather {bid} failed: {ex.Message}\n{ex.StackTrace}"
                        );
                    }
                }
                Debug.Log(
                    $"[PRWM] AppearanceCache: workshop books scanned, {wsCount} workshop books found"
                );
            }

            // Workshop skins (cloth overlays from CustomizingResourceLoader).
            // These are separate from workshop books — they're skin-only mods
            // with no key page, loaded by contentFolderIdx.
            var wsLoader = Singleton<CustomizingResourceLoader>.Instance;
            if (wsLoader != null)
            {
                foreach (var skin in wsLoader.GetWorkshopSkinDataAll())
                {
                    if (skin?.dic == null || skin.dic.Count == 0)
                        continue;
                    // File stem uses "ws_" prefix + contentFolderIdx to distinguish
                    // from book-based bodies.
                    var folderIdx = skin.contentFolderIdx;
                    try
                    {
                        var stem = $"ws_{folderIdx}";
                        var wsBody = TryGatherClothBody(
                            skin,
                            0,
                            "",
                            $"skin {folderIdx}",
                            instancesToDestroy
                        );
                        if (wsBody != null)
                        {
                            wsBody.FileStemOverride = stem;
                            result.Add(wsBody);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning(
                            $"[PRWM] AppearanceCache: workshop skin {folderIdx} failed: {ex.Message}\n{ex.StackTrace}"
                        );
                    }
                }
            }
        }
    }
}
