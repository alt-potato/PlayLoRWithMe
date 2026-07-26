using System.Collections.Generic;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Library-wide inventory writers: the equippable key page and card pools plus
    /// the global customization option tables (name pool, titles, fashion books,
    /// workshop skins, dialogue presets), with their book-grouping sort helpers.
    /// </summary>
    public static partial class GameStateSerializer
    {
        /// <summary>
        /// Serializes book and card inventories available for equipping to librarians.
        /// Written alongside floors in the main-scene (non-BattleSetting) payload.
        /// </summary>
        private static void WriteLibraryInventory(JsonWriter w)
        {
            var abilityDescList = Singleton<BattleCardAbilityDescXmlList>.Instance;

            // Key pages sitting in the inventory pool (not yet assigned to any librarian).
            var availableBooks =
                BookInventoryModel.Instance?.GetBookList_equip()
                ?? new System.Collections.Generic.List<BookModel>();

            // Replicate the in-game equip-page list ordering:
            //   Section order (UISettingEquipPageScrollList.SetData): chapter DESC,
            //     workshopId ASC, UIStoryLine enum value DESC.
            //   Within each section (SortUtil.EquipPageCompByRarity): rarity DESC,
            //     bookId.id ASC.
            availableBooks.Sort(
                (x, y) =>
                {
                    int cmp = y.ClassInfo.Chapter.CompareTo(x.ClassInfo.Chapter);
                    if (cmp != 0)
                        return cmp;
                    cmp = string.Compare(
                        x.ClassInfo.workshopID,
                        y.ClassInfo.workshopID,
                        System.StringComparison.Ordinal
                    );
                    if (cmp != 0)
                        return cmp;
                    cmp = GetStoryLineInt(y).CompareTo(GetStoryLineInt(x));
                    if (cmp != 0)
                        return cmp;
                    cmp = ((int)y.ClassInfo.Rarity).CompareTo((int)x.ClassInfo.Rarity);
                    if (cmp != 0)
                        return cmp;
                    return x.ClassInfo.id.id.CompareTo(y.ClassInfo.id.id);
                }
            );

            w.AddArray(
                "availableKeyPages",
                arr =>
                {
                    foreach (var book in availableBooks)
                    {
                        if (book == null)
                            continue;
                        arr.AddObject(o =>
                        {
                            // The in-game equip screen (UISettingEquipPageScrollList.SetData)
                            // groups vanilla books by UIStoryLine (= BookXmlInfo.BookIcon) and
                            // workshop books by their package/workshop ID instead, because
                            // workshop BookIcon values are not valid UIStoryLine enum members.
                            string bookGroupKey = book.IsWorkshop
                                ? book.ClassInfo.workshopID
                                : book.ClassInfo.BookIcon;
                            o.Add("instanceId", book.instanceId)
                                .Add("name", book.Name)
                                .Add("speedMin", book.SpeedMin)
                                .Add("speedMax", book.SpeedMax)
                                .Add("chapter", book.ClassInfo.Chapter)
                                .Add("bookIcon", bookGroupKey)
                                .Add("bookGroupName", GetBookGroupName(book, bookGroupKey))
                                .Add("hp", book.HP)
                                .Add("breakGauge", book.Break)
                                .Add("equipRangeType", book.ClassInfo.RangeType.ToString())
                                .Add("rarity", book.ClassInfo.Rarity.ToString());
                            WriteRarityColorOverrides(
                                o,
                                book.ClassInfo.id?.packageId,
                                book.ClassInfo.Rarity,
                                book.ClassInfo
                            );
                            o.AddObject(
                                    "resistances",
                                    r =>
                                        r.Add("slashHp", book.sHpResist.ToString())
                                            .Add("pierceHp", book.pHpResist.ToString())
                                            .Add("bluntHp", book.hHpResist.ToString())
                                            .Add("slashBp", book.sBpResist.ToString())
                                            .Add("pierceBp", book.pBpResist.ToString())
                                            .Add("bluntBp", book.hBpResist.ToString())
                                );
                            AddLorId(o, "bookId", book.ClassInfo.id);

                            // Passive-giving eligibility mirrors GetBookList_PassiveEquip:
                            // a book can't be a passive source if it's already attributed
                            // elsewhere OR if it's equipped as someone's primary key page.
                            int givenToId = book.originData?.equipedPassiveBookInstanceId ?? -1;
                            if (givenToId >= 0)
                            {
                                o.Add("canGivePassive", false);
                                var targetBook = BookInventoryModel.Instance?.GetBookByInstanceId(
                                    givenToId
                                );
                                if (targetBook?.owner != null)
                                    o.Add("passiveGivenTo", targetBook.owner.name);
                            }
                            else if (book.owner != null)
                            {
                                o.Add("canGivePassive", false)
                                    .Add("passiveGivenTo", book.owner.name);
                            }

                            var inventoryPassiveList = book.CreatePassiveList();
                            o.AddArray(
                                "passives",
                                parr =>
                                {
                                    if (inventoryPassiveList == null)
                                        return;
                                    foreach (var p in inventoryPassiveList)
                                    {
                                        if (p == null || p.isHide || string.IsNullOrEmpty(p.name))
                                            continue;
                                        parr.AddObject(po =>
                                        {
                                            AddLorId(po, "id", p.id);
                                            po.Add("name", p.name)
                                                .Add("rare", p.rare.ToString())
                                                .Add("isNegative", p.isNegative);
                                            if (!string.IsNullOrEmpty(p.desc))
                                                po.Add("desc", p.desc);
                                            var passiveXml =
                                                Singleton<PassiveXmlList>.Instance?.GetData(p.id);
                                            if (passiveXml != null)
                                                po.Add("cost", passiveXml.cost);
                                            if (passiveXml != null && !passiveXml.CanGivePassive)
                                                po.Add("canTransfer", false);
                                            WriteRarityColorOverrides(po, p.id?.packageId, p.rare, passiveXml);
                                        });
                                    }
                                }
                            );
                        });
                    }
                }
            );

            // Cards in the shared inventory (not currently slotted in any librarian's deck).
            var cardList =
                Singleton<InventoryModel>.Instance?.GetCardList()
                ?? new System.Collections.Generic.List<DiceCardItemModel>();
            w.AddArray(
                "availableCards",
                arr =>
                {
                    foreach (var item in cardList)
                    {
                        if (item == null || item.ClassInfo == null || item.num <= 0)
                            continue;
                        // Personal and OnlyPage cards are bound to a specific key page and
                        // cannot be manually added to arbitrary decks. The game's
                        // AddCardFromInventoryToCurrentDeck rejects OnlyPage cards unless
                        // they appear in that book's EquipEffect.OnlyCard list.
                        if (item.ClassInfo.IsPersonal() || item.ClassInfo.IsOnlyPage())
                            continue;
                        // Skip cards whose XML wasn't found at load time
                        // (e.g. saved-state cards from a since-uninstalled mod).
                        // Surfacing them lets a client request an add, which
                        // would lodge an unremovable sentinel in the deck.
                        if (item.ClassInfo.isError)
                            continue;
                        var xml = item.ClassInfo;
                        var spec = xml.Spec;
                        arr.AddObject(o =>
                        {
                            AddLorId(o, "cardId", xml.id);
                            o.Add("name", xml.Name)
                                .Add("cost", spec.Cost)
                                .Add("range", spec.Ranged.ToString())
                                .Add("rarity", xml.Rarity.ToString())
                                .Add("count", item.num)
                                .Add("chapter", xml.Chapter);
                            WriteRarityColorOverrides(o, xml.id?.packageId, xml.Rarity, xml);
                            var abilityDesc = abilityDescList?.GetAbilityDescString(xml) ?? "";
                            if (!string.IsNullOrEmpty(abilityDesc))
                                o.Add("abilityDesc", abilityDesc);
                            WriteDiceBehaviours(o, xml.DiceBehaviourList, abilityDescList);
                        });
                    }
                }
            );

            // Customization options: name pool, gift-based title lists, and dialogue presets.
            // Sent once per library inventory snapshot so the frontend can populate
            // dropdowns and preset pickers without hard-coding game data.
            WriteCustomizeOptions(w);
        }

        // Serializes global customization option tables (names, titles, dialogue presets).
        private static void WriteCustomizeOptions(JsonWriter w)
        {
            w.AddObject(
                "customizeOptions",
                o =>
                {
                    // Suggested name pool via reflection (LibrariansNameXmlList._dictionary is private).
                    var nameXml = Singleton<LibrariansNameXmlList>.Instance;
                    var nameDict = _libNameDictField?.GetValue(nameXml) as Dictionary<int, string>;

                    o.AddArray(
                        "suggestedNames",
                        arr =>
                        {
                            if (nameDict == null)
                                return;
                            foreach (var name in nameDict.Values)
                                arr.AddString(name);
                        }
                    );

                    // Gift-based prefix and suffix titles.
                    // prefixID / postfixID in UnitDataModel reference GiftXmlInfo IDs whose
                    // text is looked up via GiftXmlList.GetPrefix / GetPostfix.
                    var giftList =
                        Singleton<GiftXmlList>.Instance?.GetAvailableList()
                        ?? new System.Collections.Generic.List<GiftXmlInfo>();

                    o.AddArray(
                        "prefixTitles",
                        arr =>
                        {
                            foreach (var gift in giftList)
                            {
                                var text =
                                    Singleton<GiftXmlList>.Instance?.GetPrefix(gift.id) ?? "";
                                if (string.IsNullOrEmpty(text) || text == "Unknown Gift Prefix")
                                    continue;
                                arr.AddObject(t => t.Add("id", gift.id).Add("text", text));
                            }
                        }
                    );

                    o.AddArray(
                        "suffixTitles",
                        arr =>
                        {
                            foreach (var gift in giftList)
                            {
                                var text =
                                    Singleton<GiftXmlList>.Instance?.GetPostfix(gift.id) ?? "";
                                if (string.IsNullOrEmpty(text) || text == "Unknown Gift Posfix")
                                    continue;
                                arr.AddObject(t => t.Add("id", gift.id).Add("text", text));
                            }
                        }
                    );

                    // Fashion books: custom core books the player has unlocked and can use as
                    // appearance projections. Each entry carries rangeType and skinType so the
                    // frontend can filter by range-compatibility and show when the full head is
                    // replaced (skinType != "Lor" means the fashion skin overrides the head).
                    var ccbm = Singleton<CustomCoreBookInventoryModel>.Instance;
                    var fashionIds =
                        ccbm?.GetBookIdList_CustomCoreBook(SephirahType.None, false)
                        ?? new System.Collections.Generic.List<int>();
                    o.AddArray(
                        "fashionBooks",
                        arr =>
                        {
                            foreach (var bid in fashionIds)
                            {
                                var bxi = Singleton<BookXmlList>.Instance?.GetData(new LorId(bid));
                                if (bxi == null || bxi.canNotEquip)
                                    continue;
                                arr.AddObject(fb =>
                                {
                                    fb.Add("id", bid)
                                        .Add("name", bxi.Name)
                                        .Add("rangeType", bxi.RangeType.ToString())
                                        .Add("replacesHead", bxi.skinType != "Lor");
                                    // SkinGender from the key page XML: controls whether the
                                    // body type toggle is available for this fashion book.
                                    if (bxi.gender != Gender.N)
                                        fb.Add("skinGender", bxi.gender.ToString());

                                    // Optional per-book appearance metadata from AppearanceCache.
                                    if (
                                        AppearanceCache.FashionMeta.TryGetValue(
                                            bid.ToString(),
                                            out var meta
                                        )
                                    )
                                    {
                                        // Head tilt and pivot: omitted when tilt is zero.
                                        if (Mathf.Abs(meta.TiltDeg) > 0.05f)
                                            fb.Add("headTiltDeg", meta.TiltDeg)
                                                .Add("pivotFracX", meta.PivotFracX)
                                                .Add("pivotFracY", meta.PivotFracY);
                                        // Front layer: some body sprites render in front of the face overlay.
                                        if (meta.HasFrontLayer)
                                            fb.Add("hasFrontLayer", true);
                                        // Hood present: game hides all back hair renderers in this case.
                                        if (meta.HidesBackHair)
                                            fb.Add("hidesBackHair", true);
                                        // Feet-Y fraction: emitted only when the body PNG extends below
                                        // feet (weapons/props), so feet-alignment math can offset inward.
                                        if (meta.FeetYFrac < 0.999f)
                                            fb.Add("feetYFrac", meta.FeetYFrac);
                                        // Body PNG pixel dimensions: lets the preview compute the body
                                        // layer height and feet pivot without waiting on @load.
                                        if (meta.BodyW > 0 && meta.BodyH > 0)
                                            fb.Add("bodyW", meta.BodyW).Add("bodyH", meta.BodyH);
                                    }
                                });
                            }

                            // Second pass: workshop mod books that can be used as projections.
                            // These have a non-empty packageId and are not tracked by
                            // CustomCoreBookInventoryModel (which explicitly skips workshop books).
                            var bookInv = Singleton<BookInventoryModel>.Instance;
                            var allBooks = bookInv?.GetBookListAll();
                            if (allBooks != null)
                            {
                                var seenWs = new HashSet<string>();
                                foreach (var book in allBooks)
                                {
                                    if (!book.IsWorkshop)
                                        continue;
                                    var lid = book.GetBookClassInfoId();
                                    var bxi = book.ClassInfo;
                                    if (bxi == null || bxi.canNotEquip)
                                        continue;
                                    if (string.IsNullOrEmpty(bxi.GetCharacterSkin()))
                                        continue;
                                    // Deduplicate by full LorId — same XML can appear as multiple instances.
                                    string key = $"{lid.packageId}:{lid.id}";
                                    if (!seenWs.Add(key))
                                        continue;
                                    Debug.Log(
                                        $"[PRWM] fashionBooks: ws book id={lid.id} pkg={lid.packageId} name={bxi.Name} range={bxi.RangeType} skinType={bxi.skinType}"
                                    );
                                    arr.AddObject(fb =>
                                    {
                                        fb.Add("id", lid.id)
                                            .Add("packageId", lid.packageId)
                                            .Add("name", bxi.Name)
                                            .Add("rangeType", bxi.RangeType.ToString())
                                            .Add("replacesHead", bxi.skinType != "Lor");
                                        if (bxi.gender != Gender.N)
                                            fb.Add("skinGender", bxi.gender.ToString());
                                        var wsStem = $"{lid.packageId}_{lid.id}";
                                        if (
                                            AppearanceCache.FashionMeta.TryGetValue(
                                                wsStem,
                                                out var meta
                                            )
                                        )
                                        {
                                            if (Mathf.Abs(meta.TiltDeg) > 0.05f)
                                                fb.Add("headTiltDeg", meta.TiltDeg)
                                                    .Add("pivotFracX", meta.PivotFracX)
                                                    .Add("pivotFracY", meta.PivotFracY);
                                            if (meta.HasFrontLayer)
                                                fb.Add("hasFrontLayer", true);
                                            if (meta.HidesBackHair)
                                                fb.Add("hidesBackHair", true);
                                            if (meta.FeetYFrac < 0.999f)
                                                fb.Add("feetYFrac", meta.FeetYFrac);
                                            if (meta.BodyW > 0 && meta.BodyH > 0)
                                                fb.Add("bodyW", meta.BodyW)
                                                    .Add("bodyH", meta.BodyH);
                                        }
                                    });
                                }
                            }
                        }
                    );

                    // Workshop skins from CustomizingResourceLoader — cloth overlay skins that
                    // ship with workshop content folders.  Equipped via unit.workshopSkin (a
                    // contentFolderIdx string), completely separate from the fashion-book system.
                    o.AddArray(
                        "workshopSkins",
                        ws =>
                        {
                            var wsLoader = Singleton<CustomizingResourceLoader>.Instance;
                            if (wsLoader == null)
                                return;
                            var allSkins = wsLoader.GetWorkshopSkinDataAll();
                            if (allSkins == null)
                                return;
                            foreach (var skin in allSkins)
                            {
                                if (skin == null)
                                    continue;
                                ws.AddObject(s =>
                                {
                                    s.Add("id", skin.id)
                                        .Add("name", skin.dataName)
                                        .Add("contentFolderIdx", skin.contentFolderIdx);
                                    var wsStem = $"ws_{skin.contentFolderIdx}";
                                    if (
                                        AppearanceCache.FashionMeta.TryGetValue(
                                            wsStem,
                                            out var meta
                                        )
                                    )
                                    {
                                        // ReplacesHead is encoded inversely in FashionMeta:
                                        // HidesBackHair is set when !ReplacesHead && HasHood,
                                        // but for workshop skins the authoritative source is
                                        // ClothCustomizeData.headEnabled (already baked into
                                        // the extracted body via FashionBookBody.ReplacesHead).
                                        // We can't recover ReplacesHead from FashionMeta alone,
                                        // so we check the skin data directly.
                                        bool headEnabled = true;
                                        if (skin.dic.TryGetValue(ActionDetail.Default, out var dc))
                                            headEnabled = dc.headEnabled;
                                        else if (
                                            skin.dic.TryGetValue(ActionDetail.Standing, out var sc)
                                        )
                                            headEnabled = sc.headEnabled;
                                        s.Add("replacesHead", !headEnabled);
                                        if (meta.HasFrontLayer)
                                            s.Add("hasFrontLayer", true);
                                        if (Mathf.Abs(meta.TiltDeg) > 0.05f)
                                            s.Add("headTiltDeg", meta.TiltDeg)
                                                .Add("pivotFracX", meta.PivotFracX)
                                                .Add("pivotFracY", meta.PivotFracY);
                                        if (meta.FeetYFrac < 0.999f)
                                            s.Add("feetYFrac", meta.FeetYFrac);
                                        if (meta.BodyW > 0 && meta.BodyH > 0)
                                            s.Add("bodyW", meta.BodyW).Add("bodyH", meta.BodyH);
                                    }
                                });
                            }
                        }
                    );

                    // Dialogue preset text per dialog type for the frontend preset picker.
                    var dlgXml = Singleton<BattleDialogXmlList>.Instance;
                    var dlgTypeMap = new[]
                    {
                        (LOR_XML.DialogType.START_BATTLE, "startBattle"),
                        (LOR_XML.DialogType.BATTLE_VICTORY, "victory"),
                        (LOR_XML.DialogType.DEATH, "death"),
                        (LOR_XML.DialogType.COLLEAGUE_DEATH, "colleagueDeath"),
                        (LOR_XML.DialogType.KILLS_OPPONENT, "killsOpponent"),
                    };
                    o.AddObject(
                        "dialoguePresets",
                        dp =>
                        {
                            foreach (var (dlgType, key) in dlgTypeMap)
                            {
                                dp.AddArray(
                                    key,
                                    arr =>
                                    {
                                        if (dlgXml == null)
                                            return;
                                        try
                                        {
                                            var presets = dlgXml.GetDialogPresetByType(dlgType);
                                            foreach (var p in presets)
                                                arr.AddString(p.dialogContent);
                                        }
                                        catch
                                        { /* "Librarian" group may not exist for non-standard saves */
                                        }
                                    }
                                );
                            }
                        }
                    );

                    // Shared face/hair canvas dimensions, sourced from AppearanceCache once
                    // extraction has run. Supplied so the frontend can size the head-tilt
                    // pivot synchronously instead of fetching dimensions.json after mount
                    // (which caused a head-snap on every fresh remount, e.g. floor-tab
                    // switches). Omitted before extraction completes — frontend keeps a
                    // safe square-canvas fallback for that initial window.
                    if (AppearanceCache.FaceHairCanvasW > 0 && AppearanceCache.FaceHairCanvasH > 0)
                        o.Add("faceCanvasW", AppearanceCache.FaceHairCanvasW)
                            .Add("faceCanvasH", AppearanceCache.FaceHairCanvasH);
                }
            );
        }

        /// <summary>
        /// Returns the integer value of the <see cref="UI.UIStoryLine"/> enum that corresponds
        /// to <paramref name="book"/>'s BookIcon field, or 0 for workshop books and any
        /// BookIcon that does not map to a valid enum member.
        /// Used to replicate the game's key-page group sort order.
        /// </summary>
        private static int GetStoryLineInt(BookModel book)
        {
            if (book.IsWorkshop)
                return 0;
            if (System.Enum.IsDefined(typeof(UI.UIStoryLine), book.ClassInfo.BookIcon))
                return (int)System.Enum.Parse(typeof(UI.UIStoryLine), book.ClassInfo.BookIcon);
            return 0;
        }

        /// <summary>
        /// Resolves the display name for a book's group header, mirroring the logic in
        /// <c>UISettingInvenEquipPageListSlot.SetBooksData</c>. Falls back to the raw
        /// <paramref name="bookGroupKey"/> if no localized name can be resolved.
        /// </summary>
        private static string GetBookGroupName(BookModel book, string bookGroupKey)
        {
            if (book.IsWorkshop)
                return "workshop " + book.ClassInfo.workshopID;

            if (!System.Enum.IsDefined(typeof(UI.UIStoryLine), book.ClassInfo.BookIcon))
                return bookGroupKey;

            var storyLine = (UI.UIStoryLine)
                System.Enum.Parse(typeof(UI.UIStoryLine), book.ClassInfo.BookIcon);

            // Mirrors the exact switch in UISettingInvenEquipPageListSlot.SetBooksData.
            // "Normal story" books use either chapter-header text keys or hardcoded
            // stage IDs from StageNameXmlList — there is no generic lookup path.
            switch (storyLine)
            {
                // chapter headers
                case UI.UIStoryLine.Chapter1:
                    return TextDataModel.GetText("ui_maintitle_citystate_1") ?? bookGroupKey;
                case UI.UIStoryLine.Chapter2:
                    return TextDataModel.GetText("ui_maintitle_citystate_2") ?? bookGroupKey;
                case UI.UIStoryLine.Chapter3:
                    return TextDataModel.GetText("ui_maintitle_citystate_3") ?? bookGroupKey;
                case UI.UIStoryLine.Chapter4:
                    return TextDataModel.GetText("ui_maintitle_citystate_4") ?? bookGroupKey;
                case UI.UIStoryLine.Chapter5:
                    return TextDataModel.GetText("ui_maintitle_citystate_5") ?? bookGroupKey;
                case UI.UIStoryLine.Chapter6:
                    return TextDataModel.GetText("ui_maintitle_citystate_6") ?? bookGroupKey;
                case UI.UIStoryLine.Chapter7:
                    return TextDataModel.GetText("ui_maintitle_citystate_7") ?? bookGroupKey;
                // normal-story books with hardcoded stage IDs
                case UI.UIStoryLine.HookOfficeRemnant:
                    return StageName(100002) ?? bookGroupKey;
                case UI.UIStoryLine.AxeGang:
                    return StageName(100008) ?? bookGroupKey;
                case UI.UIStoryLine.Grade7Fixers:
                    return StageName(100005) ?? bookGroupKey;
                case UI.UIStoryLine.Grade8Fixers:
                    return StageName(100004) ?? bookGroupKey;
                case UI.UIStoryLine.RustyChainGroup:
                    return StageName(100009) ?? bookGroupKey;
                case UI.UIStoryLine.WorkshopFixer:
                    return StageName(100010) ?? bookGroupKey;
                case UI.UIStoryLine.SevenAssociation:
                    return StageName(100011) ?? bookGroupKey;
                case UI.UIStoryLine.Sword:
                    return StageName(100012) ?? bookGroupKey;
                case UI.UIStoryLine.ClassOneFixer:
                    return StageName(100013) ?? bookGroupKey;
                case UI.UIStoryLine.Jeong:
                    return StageName(100014) ?? bookGroupKey;
                case UI.UIStoryLine.AwlOfNight:
                    return StageName(100015) ?? bookGroupKey;
                case UI.UIStoryLine.Usett:
                    return StageName(100016) ?? bookGroupKey;
                case UI.UIStoryLine.Mirae:
                    return StageName(100017) ?? bookGroupKey;
                case UI.UIStoryLine.Workshop:
                    return StageName(100018) ?? bookGroupKey;
                case UI.UIStoryLine.Bayyard:
                    return StageName(100019) ?? bookGroupKey;
            }

            // Reception-based books — look up via StageClassInfoList
            var allStages = Singleton<StageClassInfoList>.Instance?.GetAllDataList();
            if (allStages != null)
            {
                var stageInfo = allStages.Find(x => x.storyType == storyLine.ToString());
                if (stageInfo != null)
                {
                    string name = Singleton<StageNameXmlList>.Instance?.GetName(stageInfo);
                    if (!string.IsNullOrEmpty(name) && name != "Unknown")
                        return name;
                }
            }

            return bookGroupKey;
        }

        /// <summary>
        /// Shorthand for <c>StageNameXmlList.GetName(id)</c>, returning null when
        /// the singleton is unavailable or the result is the default "Unknown".
        /// </summary>
        private static string StageName(int id)
        {
            string name = Singleton<StageNameXmlList>.Instance?.GetName(id);
            return !string.IsNullOrEmpty(name) && name != "Unknown" ? name : null;
        }
    }
}
