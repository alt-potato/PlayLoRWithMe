using System.Collections.Generic;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Per-librarian writers for the main-library scene: identity, key pages,
    /// passives, attribution, decks, exclusive cards, and appearance customization.
    /// Split out of GameStateSerializer.cs so the floor roster stays readable.
    /// </summary>
    public static partial class GameStateSerializer
    {
        /// <summary>
        /// Serializes a single librarian object: identity, key pages, passives,
        /// attribution, decks, exclusive cards, and all appearance customization.
        /// </summary>
        private static void WriteLibrarian(
            JsonWriter o,
            UnitDataModel unit,
            BookModel book,
            int floorIdx,
            int unitIdx,
            BattleCardAbilityDescXmlList abilityDescList
        )
        {
            o.Add("floorIndex", floorIdx).Add("unitIndex", unitIdx).Add("name", unit.name);

            // Emit the display name of whoever holds the edit lock, so the UI can show
            // a "being edited by X" badge.
            var lockerName = Server.Instance?.SessionManager?.GetLibrarianLockerName(
                floorIdx + ":" + unitIdx
            );
            if (!string.IsNullOrEmpty(lockerName))
                o.Add("lockedBy", lockerName);

            // Key page -- reads base values directly from BookModel (no battle buffs
            // applied outside of combat). HP includes the unit's gift stat bonus,
            // matching unit.MaxHp's formula and the BattleSetting preview.
            WriteLibrarianKeyPage(o, "keyPage", book, unit.MaxHp);

            // Base (origin) key page -- `defaultBook` is bound to the unit and cannot be
            // transferred. The frontend uses this to surface the base in the editor and
            // to detect "currently on base" via the shared instanceId.
            var baseBook = unit.defaultBook;
            if (baseBook != null)
            {
                int baseHp = baseBook.HP + (unit.giftInventory?.GetStatBonus_Hp() ?? 0);
                WriteLibrarianKeyPage(o, "baseKeyPage", baseBook, baseHp);
            }

            WriteLibrarianFashionMeta(o, book);
            WriteLibrarianPassives(o, book);
            WriteLibrarianAttribution(o, book);
            WriteLibrarianDecks(o, book, unit, abilityDescList);
            WriteLibrarianOnlyCards(o, book, abilityDescList);
            WriteLibrarianAppearance(o, unit);
            WriteLibrarianDialogue(o, unit);
            WriteLibrarianCosmetics(o, unit, book);
            WriteLibrarianGifts(o, unit);
        }

        /// <summary>
        /// Appends fashion metadata for the equipped key page's body preview as sibling
        /// fields on the librarian object (not nested inside keyPage, which is already
        /// closed). Field names mirror fashionBooks[].
        /// </summary>
        private static void WriteLibrarianFashionMeta(JsonWriter o, BookModel book)
        {
            var kpLid = book.GetBookClassInfoId();
            var kpBxi = book.ClassInfo;
            if (kpBxi == null || string.IsNullOrEmpty(kpBxi.GetCharacterSkin()))
                return;

            if (kpBxi.gender != Gender.N)
                o.Add("keyPageSkinGender", kpBxi.gender.ToString());
            o.Add("keyPageReplacesHead", kpBxi.skinType != "Lor");
            var kpStem = string.IsNullOrEmpty(kpLid.packageId)
                ? kpLid.id.ToString()
                : $"{kpLid.packageId}_{kpLid.id}";
            if (!AppearanceCache.FashionMeta.TryGetValue(kpStem, out var kpMeta))
                return;

            if (Mathf.Abs(kpMeta.TiltDeg) > 0.05f)
                o.Add("keyPageHeadTiltDeg", kpMeta.TiltDeg)
                    .Add("keyPagePivotFracX", kpMeta.PivotFracX)
                    .Add("keyPagePivotFracY", kpMeta.PivotFracY);
            if (kpMeta.HasFrontLayer)
                o.Add("keyPageHasFrontLayer", true);
            if (kpMeta.HidesBackHair)
                o.Add("keyPageHidesBackHair", true);
            if (kpMeta.FeetYFrac < 0.999f)
                o.Add("keyPageFeetYFrac", kpMeta.FeetYFrac);
            if (kpMeta.BodyW > 0 && kpMeta.BodyH > 0)
                o.Add("keyPageBodyW", kpMeta.BodyW).Add("keyPageBodyH", kpMeta.BodyH);
        }

        /// <summary>
        /// Appends the librarian's passive list ("passives") via CreatePassiveList,
        /// covering built-in key-page passives and equipped passive books.
        /// </summary>
        private static void WriteLibrarianPassives(JsonWriter o, BookModel book)
        {
            var passiveList = book.CreatePassiveList();
            o.AddArray(
                "passives",
                parr =>
                {
                    if (passiveList == null)
                        return;
                    foreach (var p in passiveList)
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
                            var passiveXml = Singleton<PassiveXmlList>.Instance?.GetData(p.id);
                            if (passiveXml != null)
                                po.Add("cost", passiveXml.cost);
                            WriteRarityColorOverrides(po, p.id?.packageId, p.rare, passiveXml);
                        });
                    }
                }
            );
        }

        /// <summary>
        /// Appends passive-attribution (succession) metadata: slot capacity, cost budget
        /// ("passiveSlotCount"/"maxPassiveCost"/"currentPassiveCost"), the source key
        /// page ids, and the attributed (succession-received) passives.
        /// </summary>
        private static void WriteLibrarianAttribution(JsonWriter o, BookModel book)
        {
            o.Add("passiveSlotCount", book.ClassInfo?.SuccessionPossibleNumber ?? 0)
                .Add("maxPassiveCost", book.GetMaxPassiveCost())
                .Add("currentPassiveCost", book.GetCurrentPassiveCost());

            var sourceIds = book.originData?.equipedBookIdListInPassive;
            if (sourceIds != null && sourceIds.Count > 0)
            {
                o.AddArray(
                    "sourceKeyPageIds",
                    sArr =>
                    {
                        foreach (var sid in sourceIds)
                            sArr.AddInt(sid);
                    }
                );
            }

            // Attributed (succession-received) passives -- passives whose source book
            // differs from this book's own instance.
            var allPassives = _activatedAllPassivesField?.GetValue(book) as List<PassiveModel>;
            if (allPassives == null)
                return;

            var attributed = allPassives.FindAll(pm =>
                pm.originData != null
                && pm.originData.currentpassive?.id != EmptyAttributionPassiveId
                && pm.originData.receivepassivebookId != pm.BookInstanceId
            );
            if (attributed.Count == 0)
                return;

            o.AddArray(
                "attributedPassives",
                apArr =>
                {
                    var descList = Singleton<PassiveDescXmlList>.Instance;
                    foreach (var pm in attributed)
                    {
                        var pmData = pm.originData;
                        if (pmData?.currentpassive == null)
                            continue;
                        var pxml = pmData.currentpassive;
                        // Resolve localized name/desc the same way BookPassiveInfo does --
                        // the raw XML fields are blank for most vanilla entries.
                        string pname = pxml.id.IsWorkshop()
                            ? pxml.name
                            : (descList?.GetName(pxml.id) ?? pxml.name);
                        string pdesc = pxml.id.IsWorkshop()
                            ? pxml.desc
                            : (descList?.GetDesc(pxml.id) ?? pxml.desc);
                        apArr.AddObject(ap =>
                        {
                            ap.AddObject(
                                "passive",
                                pp =>
                                {
                                    AddLorId(pp, "id", pxml.id);
                                    pp.Add("name", pname)
                                        .Add("rare", pxml.rare.ToString())
                                        .Add("isNegative", pxml.isNegative);
                                    if (!string.IsNullOrEmpty(pdesc))
                                        pp.Add("desc", pdesc);
                                    pp.Add("cost", pxml.cost);
                                    WriteRarityColorOverrides(
                                        pp,
                                        pxml.id?.packageId,
                                        pxml.rare,
                                        pxml
                                    );
                                }
                            );
                            ap.Add("sourceInstanceId", pmData.receivepassivebookId);
                            var srcBook = BookInventoryModel.Instance?.GetBookByInstanceId(
                                pmData.receivepassivebookId
                            );
                            if (srcBook != null)
                                ap.Add("sourceName", srcBook.Name);
                        });
                    }
                }
            );
        }

        /// <summary>
        /// Appends per-deck-slot card lists ("decks"). Single-deck books emit a length-1
        /// array; multi-deck books emit one entry per exposed slot.
        /// </summary>
        /// <remarks>
        /// Per-deck cards use ChangeDeck(idx) + GetCardListFromCurrentDeck() so any mod's
        /// postfix on that method (the canonical extension point -- Binah injects deck 1's
        /// cards there based on GetCurrentDeckIndex()) sees the right active index.
        /// GetCardListByIndex bypasses that postfix and would surface the raw _deckList[idx]
        /// contents, which for mods like Binah are empty until GetCardListFromCurrentDeck
        /// synthesizes them. The prevIdx/finally restore mirrors the action-handler pattern
        /// so a transient ChangeDeck never leaks past the serializer.
        /// </remarks>
        private static void WriteLibrarianDecks(
            JsonWriter o,
            BookModel book,
            UnitDataModel unit,
            BattleCardAbilityDescXmlList abilityDescList
        )
        {
            o.AddArray(
                "decks",
                decksArr =>
                {
                    bool isMulti = book.IsMultiDeck();
                    if (isMulti)
                    {
                        // Synthesize a SetDeckLayout invocation if the player hasn't opened
                        // the in-game deck editor for this book. Without this, mod-supplied
                        // labels (Binah's "Philosophy" / "Arbiter") and tab-deactivation never
                        // reach the cache because their patches only run when the in-game
                        // panel renders.
                        MultiDeckLabelsAdapter.EnsureLabelsCached(book, unit);
                    }
                    int deckCount = isMulti ? MultiDeckLabelsAdapter.GetEffectiveDeckCount(book) : 1;
                    string[] localizedLabels = null;
                    if (isMulti)
                        MultiDeckLabelsAdapter.TryGetLabels(book, out localizedLabels);
                    int prevIdx = isMulti ? book.GetCurrentDeckIndex() : 0;
                    try
                    {
                        for (int di = 0; di < deckCount; di++)
                        {
                            int idx = di;
                            List<LOR_DiceSystem.DiceCardXmlInfo> rawCards;
                            if (isMulti)
                            {
                                if (idx != book.GetCurrentDeckIndex())
                                    book.ChangeDeck(idx);
                                rawCards = book.GetCardListFromCurrentDeck();
                            }
                            else
                            {
                                rawCards = book.GetCardListFromCurrentDeck();
                            }
                            decksArr.AddObject(deckObj =>
                            {
                                deckObj.Add("index", idx);
                                // Cache hits may have null/empty entries for tabs a mod hid;
                                // emit `label` only when we observed a real string so the
                                // frontend's "Deck N" fallback can take over for hidden slots.
                                if (
                                    localizedLabels != null
                                    && idx < localizedLabels.Length
                                    && !string.IsNullOrEmpty(localizedLabels[idx])
                                )
                                    deckObj.Add("label", localizedLabels[idx]);
                                deckObj.AddArray(
                                    "cards",
                                    darr =>
                                    {
                                        if (rawCards == null)
                                            return;
                                        var counts = new Dictionary<string, int>();
                                        var firstSeen =
                                            new Dictionary<string, LOR_DiceSystem.DiceCardXmlInfo>();
                                        var order = new List<string>();
                                        foreach (var xml in rawCards)
                                        {
                                            if (xml == null)
                                                continue;
                                            var key = xml._id + "_" + xml.workshopID;
                                            if (!counts.ContainsKey(key))
                                            {
                                                counts[key] = 0;
                                                firstSeen[key] = xml;
                                                order.Add(key);
                                            }
                                            counts[key]++;
                                        }
                                        foreach (var key in order)
                                        {
                                            var xml = firstSeen[key];
                                            var spec = xml.Spec;
                                            darr.AddObject(c =>
                                            {
                                                AddLorId(c, "cardId", xml.id);
                                                c.Add("name", xml.Name)
                                                    .Add("cost", spec.Cost)
                                                    .Add("range", spec.Ranged.ToString())
                                                    .Add("rarity", xml.Rarity.ToString())
                                                    .Add("count", counts[key]);
                                                WriteRarityColorOverrides(
                                                    c,
                                                    xml.id?.packageId,
                                                    xml.Rarity,
                                                    xml
                                                );

                                                WriteDiceBehaviours(
                                                    c,
                                                    xml.DiceBehaviourList,
                                                    abilityDescList
                                                );

                                                var abilityDesc =
                                                    abilityDescList?.GetAbilityDescString(xml) ?? "";
                                                if (!string.IsNullOrEmpty(abilityDesc))
                                                    c.Add("abilityDesc", abilityDesc);
                                            });
                                        }
                                    }
                                );
                            });
                        }
                    }
                    finally
                    {
                        if (isMulti && book.GetCurrentDeckIndex() != prevIdx)
                            book.ChangeDeck(prevIdx);
                    }
                }
            );
        }

        /// <summary>
        /// Appends page-exclusive (OnlyPage) cards belonging to this book that are
        /// currently in inventory ("onlyCards"), so the deck editor can surface them
        /// first in the add-cards list.
        /// </summary>
        private static void WriteLibrarianOnlyCards(
            JsonWriter o,
            BookModel book,
            BattleCardAbilityDescXmlList abilityDescList
        )
        {
            var onlyCardIds = book.ClassInfo.EquipEffect?.OnlyCard;
            var pageInventory = Singleton<InventoryModel>.Instance;
            o.AddArray(
                "onlyCards",
                oArr =>
                {
                    if (pageInventory == null || onlyCardIds == null)
                        return;
                    foreach (var id in onlyCardIds)
                    {
                        var lorId = new LorId(id);
                        var count = pageInventory.GetCardCount(lorId);
                        if (count <= 0)
                            continue;
                        var xml = ItemXmlDataList.instance.GetCardItem(lorId);
                        if (xml == null)
                            continue;
                        var spec = xml.Spec;
                        oArr.AddObject(c =>
                        {
                            AddLorId(c, "cardId", xml.id);
                            c.Add("name", xml.Name)
                                .Add("cost", spec.Cost)
                                .Add("range", spec.Ranged.ToString())
                                .Add("rarity", xml.Rarity.ToString())
                                .Add("count", count)
                                .Add("chapter", xml.Chapter);
                            WriteRarityColorOverrides(c, xml.id?.packageId, xml.Rarity, xml);
                            var abilityDesc = abilityDescList?.GetAbilityDescString(xml) ?? "";
                            if (!string.IsNullOrEmpty(abilityDesc))
                                c.Add("abilityDesc", abilityDesc);
                            WriteDiceBehaviours(c, xml.DiceBehaviourList, abilityDescList);
                        });
                    }
                }
            );
        }

        /// <summary>
        /// Appends appearance customization fields ("appearance"). Always serialized so
        /// the UI can pre-fill current values; emits defaults when the unit has no
        /// customizeData (non-customizable unit).
        /// </summary>
        private static void WriteLibrarianAppearance(JsonWriter o, UnitDataModel unit)
        {
            var cd = unit.customizeData;
            Color32 hair = cd != null ? (Color32)cd.hairColor : new Color32(13, 13, 13, 255);
            Color32 skin = cd != null ? (Color32)cd.skinColor : new Color32(224, 188, 157, 255);
            Color32 eyeC = cd != null ? (Color32)cd.eyeColor : new Color32(13, 13, 13, 255);
            // Patron librarians use SpecialCustomizedAppearance prefabs with unique head
            // sprites. Only emit patronHeadId for IDs that the game recognizes as patron
            // heads -- regular librarians have specialCustomIDs (11-20) without prefabs.
            int patronId = 0;
            if (cd?.specialCustomID != null)
            {
                int sid = cd.specialCustomID.id;
                bool isPatron =
                    (cd.specialCustomID.IsBasic() && sid >= 1 && sid <= 10)
                    || sid == 9070402
                    || sid == 1309021
                    || sid == 9100501;
                if (isPatron)
                    patronId = sid;
            }

            o.AddObject(
                "appearance",
                a =>
                {
                    a.Add("frontHairID", cd?.frontHairID ?? 0)
                        .Add("backHairID", cd?.backHairID ?? 0)
                        .Add("eyeID", cd?.eyeID ?? 0)
                        .Add("browID", cd?.browID ?? 0)
                        .Add("mouthID", cd?.mouthID ?? 0)
                        .Add("headID", cd?.headID ?? 0)
                        .Add("height", cd?.height ?? 175)
                        .AddArray(
                            "hairColor",
                            cArr => cArr.AddInt(hair.r).AddInt(hair.g).AddInt(hair.b)
                        )
                        .AddArray(
                            "skinColor",
                            cArr => cArr.AddInt(skin.r).AddInt(skin.g).AddInt(skin.b)
                        )
                        .AddArray(
                            "eyeColor",
                            cArr => cArr.AddInt(eyeC.r).AddInt(eyeC.g).AddInt(eyeC.b)
                        );
                    if (patronId > 0)
                        a.Add("patronHeadId", patronId);
                }
            );
        }

        /// <summary>
        /// Appends per-type custom battle dialogue text ("dialogue"). Only serialized
        /// when the librarian has a dialogue model (a customizable librarian, not a
        /// Sephirah boss).
        /// </summary>
        private static void WriteLibrarianDialogue(JsonWriter o, UnitDataModel unit)
        {
            var dlgModel = unit.battleDialogModel;
            if (dlgModel == null)
                return;

            var dlgTypes = new[]
            {
                LOR_XML.DialogType.START_BATTLE,
                LOR_XML.DialogType.BATTLE_VICTORY,
                LOR_XML.DialogType.DEATH,
                LOR_XML.DialogType.COLLEAGUE_DEATH,
                LOR_XML.DialogType.KILLS_OPPONENT,
            };
            var dlgKeys = new[]
            {
                "startBattle",
                "victory",
                "death",
                "colleagueDeath",
                "killsOpponent",
            };
            o.AddObject(
                "dialogue",
                d =>
                {
                    for (int di = 0; di < dlgTypes.Length; di++)
                    {
                        var dlgData = dlgModel.GetDialogData(dlgTypes[di]);
                        // Prefer custom text; fall back to the currently-active preset text
                        // so the UI can pre-fill the field even when no explicit
                        // customization has been made yet.
                        string text = dlgData?.customText;
                        if (string.IsNullOrEmpty(text))
                            text = dlgData?.xmlData?.dialogContent;
                        d.Add(dlgKeys[di], string.IsNullOrEmpty(text) ? null : text);
                    }
                }
            );
        }

        /// <summary>
        /// Appends remaining cosmetic fields: title gift ids, the active custom core book
        /// ("customBookId"), workshop skin, Sephirah flag, body type, and the active
        /// skin's gender (when not neutral).
        /// </summary>
        private static void WriteLibrarianCosmetics(JsonWriter o, UnitDataModel unit, BookModel book)
        {
            // Title prefix/suffix gift IDs.
            o.AddObject(
                "titles",
                t => t.Add("prefixID", unit.prefixID).Add("postfixID", unit.postfixID)
            );

            // Fashion projection: which custom core book is active (-1 = none).
            var customBook = unit.GetCustomBookItemData();
            o.Add("customBookId", customBook != null ? customBook.GetBookClassInfoId().id : -1);
            // Workshop books carry a packageId; include it so the frontend can identify
            // them unambiguously.
            if (customBook != null)
            {
                var cbPkg = customBook.GetBookClassInfoId().packageId;
                if (!string.IsNullOrEmpty(cbPkg))
                    o.Add("customBookPackageId", cbPkg);
            }

            // Workshop skin: cloth overlay equipped via the workshop skin system
            // (contentFolderIdx string, "" when none).
            if (!string.IsNullOrEmpty(unit.workshopSkin))
                o.Add("workshopSkin", unit.workshopSkin);

            // Patron (sephirah) librarians have restricted customization: no name editing,
            // no face/hair (uses SpecialCustomizedAppearance), no dialogue.
            if (unit.isSephirah)
                o.Add("isSephirah", true);

            // Body type: the Gender enum variant controlling which body prefab
            // (_F/_M/_N suffix) is used in-game.
            o.Add("appearanceType", unit.appearanceType.ToString());

            // The active skin's SkinGender determines whether the body type toggle should
            // be enabled in the frontend.
            var activeSkinInfo = customBook?.ClassInfo ?? book.ClassInfo;
            if (activeSkinInfo.gender != Gender.N)
                o.Add("skinGender", activeSkinInfo.gender.ToString());
        }

        /// <summary>
        /// Appends gift accessories ("gifts") -- the equipped list as a 9-slot array (one
        /// per GiftPosition, null when empty) plus the available-for-equipping list.
        /// </summary>
        private static void WriteLibrarianGifts(JsonWriter o, UnitDataModel unit)
        {
            var inv = unit.giftInventory;
            var equippedGifts = inv.GetEquippedList();
            var unequippedGifts = inv.GetUnequippedList();

            // Build a slot-indexed array of 9 entries (one per GiftPosition).
            // Null means nothing is equipped in that slot.
            var equippedBySlot = new GiftModel[9];
            foreach (var g in equippedGifts)
                equippedBySlot[(int)g.ClassInfo.Position] = g;

            o.AddObject(
                "gifts",
                gifts =>
                {
                    gifts.AddArray(
                        "equipped",
                        eqArr =>
                        {
                            for (int si = 0; si < equippedBySlot.Length; si++)
                            {
                                var g = equippedBySlot[si];
                                if (g == null)
                                {
                                    eqArr.AddNull();
                                }
                                else
                                {
                                    eqArr.AddObject(go =>
                                    {
                                        go.Add("id", g.GetGiftClassInfoId())
                                            .Add("name", g.GetName())
                                            .Add("desc", g.GiftDesc)
                                            .Add("position", g.ClassInfo.Position.ToString());
                                        WriteGiftStat(go, g.ClassInfo.Stat);
                                        go.Add("visible", g.isShowEquipGift);
                                    });
                                }
                            }
                        }
                    );

                    gifts.AddArray(
                        "available",
                        avArr =>
                        {
                            foreach (var g in unequippedGifts)
                            {
                                // Skip gifts hidden from the appearance UI.
                                if (g.ClassInfo.NoAppear)
                                    continue;
                                avArr.AddObject(go =>
                                {
                                    go.Add("id", g.GetGiftClassInfoId())
                                        .Add("name", g.GetName())
                                        .Add("desc", g.GiftDesc)
                                        .Add("position", g.ClassInfo.Position.ToString());
                                    WriteGiftStat(go, g.ClassInfo.Stat);
                                });
                            }
                        }
                    );
                }
            );
        }

        /// <summary>Serializes a GiftStatEffect as a nested "stat" object.</summary>
        private static void WriteGiftStat(JsonWriter w, GiftStatEffect stat)
        {
            w.AddObject(
                "stat",
                s =>
                    s.Add("hp", stat.Hp)
                        .Add("breakGauge", stat.Break)
                        .Add("breakRecover", stat.BreakRecover)
                        .Add("tune", stat.Tune)
                        .Add("amp", stat.Amp)
            );
        }

        /// <summary>
        /// Writes a librarian-context key page object onto <paramref name="o"/>
        /// under <paramref name="fieldName"/>. Shared between the equipped
        /// <c>keyPage</c> and the origin <c>baseKeyPage</c> so the two stay
        /// structurally identical and the frontend can compare them by
        /// <c>instanceId</c> to detect the "on base" state.
        /// <paramref name="hp"/> is passed in because each page has its own
        /// raw HP value; gift bonuses are unit-wide and applied by the caller.
        /// </summary>
        private static void WriteLibrarianKeyPage(
            JsonWriter o,
            string fieldName,
            BookModel book,
            int hp
        )
        {
            o.AddObject(
                fieldName,
                k =>
                {
                    k.Add("instanceId", book.instanceId).Add("bookId", book.GetBookClassInfoId().id);
                    var pkg = book.GetBookClassInfoId().packageId;
                    if (!string.IsNullOrEmpty(pkg))
                        k.Add("bookPackageId", pkg);
                    k.Add("name", book.Name)
                        .Add("speedMin", book.SpeedMin)
                        .Add("speedMax", book.SpeedMax)
                        .Add("hp", hp)
                        .Add("breakGauge", book.Break)
                        .Add("equipRangeType", book.ClassInfo.RangeType.ToString())
                        // Rarity is emitted only on librarian-owned key pages so customization
                        // surfaces can render the colored outline. Battle-context emission sites
                        // omit this field by calling their own writers.
                        .Add("rarity", book.ClassInfo.Rarity.ToString())
                        // Multi-deck signal — true when the key page has the BookOption.MultiDeck
                        // flag (e.g. The Purple Tear). Drives the editor's tab strip.
                        .Add("isMultiDeck", book.IsMultiDeck());
                    WriteRarityColorOverrides(
                        k,
                        book.ClassInfo.id?.packageId,
                        book.ClassInfo.Rarity,
                        book.ClassInfo
                    );
                    k.AddObject(
                            "resistances",
                            r =>
                                r.Add("slashHp", book.sHpResist.ToString())
                                    .Add("pierceHp", book.pHpResist.ToString())
                                    .Add("bluntHp", book.hHpResist.ToString())
                                    .Add("slashBp", book.sBpResist.ToString())
                                    .Add("pierceBp", book.pBpResist.ToString())
                                    .Add("bluntBp", book.hBpResist.ToString())
                        );
                }
            );
        }
    }
}
