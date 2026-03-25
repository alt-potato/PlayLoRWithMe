using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LOR_DiceSystem;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Serializes the current Library of Ruina game state to JSON.
    /// </summary>
    public static class GameStateSerializer
    {
        /// <summary>
        /// Serializes the full unfiltered game state. Used by the SSE path and as the
        /// baseline for delta diffing.
        /// </summary>
        public static string Serialize() => BuildJsonSafe(ownedUnitIds: null);

        /// <summary>
        /// Serializes game state filtered for one session's owned units. Unowned allies
        /// expose only <c>handCount</c>, <c>deckCount</c>, and <c>egoCount</c> instead
        /// of the full hand/deck/ego arrays, so each client only sees their own
        /// librarians' private card data.
        /// </summary>
        public static string SerializeForSession(
            System.Collections.Generic.HashSet<int> ownedUnitIds
        ) => BuildJsonSafe(ownedUnitIds);

        private static string BuildJsonSafe(System.Collections.Generic.HashSet<int> ownedUnitIds)
        {
            try
            {
                return BuildJson(ownedUnitIds);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PlayLoRWithMe] GameStateSerializer error: {ex}");
                return new JsonWriter().Add("scene", "error").Build();
            }
        }

        // -------------------------------------------------------------------------
        // Scene dispatch
        // -------------------------------------------------------------------------

        /// <summary>
        /// Builds a JSON object representing the current game state.
        /// </summary>
        private static string BuildJson(System.Collections.Generic.HashSet<int> ownedUnitIds)
        {
            var gsm = GameSceneManager.Instance;
            if (gsm == null)
                return new JsonWriter().Add("scene", "loading").Build();

            if (gsm.battleScene != null && gsm.battleScene.gameObject.activeSelf)
                return BuildBattleJson(ownedUnitIds);

            if (gsm.uIController != null && gsm.uIController.gameObject.activeSelf)
                return BuildMainJson();

            if (gsm.storyRoot != null && gsm.storyRoot.gameObject.activeSelf)
                return new JsonWriter().Add("scene", "story").Build();

            if (gsm.titleScene != null && gsm.titleScene.gameObject.activeSelf)
                return new JsonWriter().Add("scene", "title").Build();

            return new JsonWriter().Add("scene", "transition").Build();
        }

        /// <summary>
        /// Builds a JSON representing the current main menu page.
        /// During <c>BattleSetting</c> phase also emits pre-battle ally/enemy
        /// previews so the frontend can render the formation screen.
        /// </summary>
        private static string BuildMainJson()
        {
            var w = new JsonWriter().Add("scene", "main");
            var uic = UI.UIController.Instance;
            if (uic != null)
                w.Add("uiPhase", uic.CurrentUIPhase.ToString());

            if (uic?.CurrentUIPhase == UI.UIPhase.BattleSetting)
                WriteBattleSettingData(w);
            else
            {
                WriteFloors(w);
                WriteLibraryInventory(w);
                WriteCustomizeOptions(w);
            }

            return w.Build();
        }

        /// <summary>
        /// Appends stage info and pre-battle unit previews for the BattleSetting phase.
        /// Uses <c>UnitBattleDataModel</c> rather than <c>BattleUnitModel</c> because
        /// the battle scene has not loaded yet, so only formation and key page data is available.
        /// </summary>
        private static void WriteBattleSettingData(JsonWriter w)
        {
            var sc = Singleton<StageController>.Instance;
            if (sc == null)
                return;

            w.AddObject(
                "stage",
                s =>
                {
                    s.Add("wave", sc.CurrentWave).Add("floor", sc.CurrentFloor.ToString());
                    var stageModel = sc.GetStageModel();
                    if (stageModel?.ClassInfo == null)
                        return;

                    s.Add("chapter", stageModel.ClassInfo.chapter);

                    // Prefer the localized name from StageNameXmlList; falls back to
                    // stageName (raw XML) for workshop/mod stages that have no entry.
                    var localizedName = Singleton<StageNameXmlList>.Instance?.GetName(
                        stageModel.ClassInfo
                    );
                    if (!string.IsNullOrEmpty(localizedName) && localizedName != "Unknown")
                        s.Add("name", localizedName);
                    else if (!string.IsNullOrEmpty(stageModel.ClassInfo.stageName))
                        s.Add("name", stageModel.ClassInfo.stageName);

                    // Extract the story-chapter icon pair used by the in-game BattleSetting
                    // screen (same sprites UISpriteDataManager serves to img_enemyTitleIcon
                    // and img_enemyTitleIconBg). The glow is rendered behind the icon.
                    var spriteDataMgr = UI.UISpriteDataManager.instance;
                    if (spriteDataMgr != null)
                    {
                        var iconSet = spriteDataMgr.GetStoryIcon(stageModel.ClassInfo.storyType);
                        if (iconSet?.icon != null)
                        {
                            var iconId = IconCache.EnsureStageIcon(iconSet.icon);
                            if (iconId != null)
                                s.Add("icon", iconId);
                        }
                        if (iconSet?.iconGlow != null)
                        {
                            var glowId = IconCache.EnsureStageIcon(iconSet.iconGlow);
                            if (glowId != null)
                                s.Add("iconGlow", glowId);
                        }
                    }
                }
            );

            // Allied librarians selected for this battle — GetUnitAddedBattleDataList
            // filters by IsAddedBattle, which reflects the actual per-unit selection
            // rather than the full floor roster.
            var floor = sc.GetCurrentStageFloorModel();
            if (floor != null)
                w.AddArray(
                    "allies",
                    arr =>
                    {
                        var units = floor.GetUnitAddedBattleDataList();
                        for (int i = 0; i < units.Count; i++)
                            WriteUnitBattleData(arr, i, units[i]);
                    }
                );

            // Enemies in the current wave
            var wave = sc.GetCurrentWaveModel();
            if (wave != null)
                w.AddArray(
                    "enemies",
                    arr =>
                    {
                        var units = wave.GetUnitBattleDataList();
                        for (int i = 0; i < units.Count; i++)
                            WriteUnitBattleData(arr, i, units[i]);
                    }
                );
        }

        /// <summary>
        /// Appends floor-level data (official name, realization level, EGO page, emotion cards,
        /// and nested librarian roster) for every opened Sephirah floor.
        /// Called from <c>BuildMainJson</c> for any main-scene phase other than BattleSetting.
        /// </summary>
        private static void WriteFloors(JsonWriter w)
        {
            var lib = LibraryModel.Instance;
            if (lib == null)
                return;

            // The 10 named Sephirah floors in canonical order; index becomes floorIndex.
            var sephirahs = new[]
            {
                SephirahType.Malkuth,
                SephirahType.Yesod,
                SephirahType.Hod,
                SephirahType.Netzach,
                SephirahType.Tiphereth,
                SephirahType.Gebura,
                SephirahType.Chesed,
                SephirahType.Binah,
                SephirahType.Hokma,
                SephirahType.Keter,
            };

            var abilityDescList = Singleton<BattleCardAbilityDescXmlList>.Instance;
            var emotionCardList = Singleton<EmotionCardXmlList>.Instance;
            var egoCardList = Singleton<EmotionEgoXmlList>.Instance;
            // Same text source as the battle-selection overlay (AbnormalityPicker):
            // keyed by script name, provides localized cardName, abilityDesc, and flavorText.
            var cardDescList = Singleton<AbnormalityCardDescXmlList>.Instance;

            w.AddArray(
                "floors",
                arr =>
                {
                    for (int fi = 0; fi < sephirahs.Length; fi++)
                    {
                        var sephirah = sephirahs[fi];

                        // Skip floors not yet opened in this playthrough.
                        if (!lib.IsOpenedSephirah(sephirah))
                            continue;

                        var floor = lib.GetFloor(sephirah);
                        if (floor == null)
                            continue;

                        var units = floor.GetUnitDataList();
                        int floorIdx = fi;

                        arr.AddObject(floorObj =>
                        {
                            // Floor-level identity and progression.
                            floorObj
                                .Add("floorIndex", floorIdx)
                                .Add(
                                    "officialName",
                                    TextDataModel.GetText(
                                        SephirahLocalizeText.GetSephirahLocalizeTextByType(sephirah)
                                    )
                                )
                                .Add("realizationLevel", floor.Level);

                            // EGO pages are only available at max realization (level 6).
                            // This matches UIEgoCardPanel.SetData which empties all slots
                            // when floor.Level < 6.
                            var egoCards =
                                new System.Collections.Generic.List<LOR_DiceSystem.DiceCardXmlInfo>();
                            if (floor.Level >= 6 && egoCardList != null)
                                egoCards = egoCardList.GetEgoCardList(sephirah);
                            floorObj.AddArray(
                                "egoCards",
                                egoArr =>
                                {
                                    foreach (var xml in egoCards)
                                    {
                                        if (xml == null)
                                            continue;
                                        var spec = xml.Spec;
                                        egoArr.AddObject(c =>
                                        {
                                            c.Add("name", xml.Name)
                                                .Add("cost", spec.Cost)
                                                .Add("range", spec.Ranged.ToString())
                                                .Add("rarity", xml.Rarity.ToString())
                                                .Add("count", 1);

                                            WriteDiceBehaviours(c, xml.DiceBehaviourList, abilityDescList);

                                            var abilityDesc =
                                                abilityDescList?.GetAbilityDescString(xml) ?? "";
                                            if (!string.IsNullOrEmpty(abilityDesc))
                                                c.Add("abilityDesc", abilityDesc);
                                        });
                                    }
                                }
                            );

                            // Abnormality pages (Awakening/Breakdown) available at or below the
                            // current realization level. Each unlock level corresponds to one
                            // abnormality encounter on this floor.
                            // Sorted by unlock level ascending, then positive-before-negative,
                            // so the frontend can group them cleanly by abnormality encounter.
                            var positiveCards =
                                emotionCardList?.GetDataList(
                                    sephirah,
                                    floor.Level,
                                    MentalState.Positive
                                ) ?? new System.Collections.Generic.List<EmotionCardXmlInfo>();
                            var negativeCards =
                                emotionCardList?.GetDataList(
                                    sephirah,
                                    floor.Level,
                                    MentalState.Negative
                                ) ?? new System.Collections.Generic.List<EmotionCardXmlInfo>();

                            var allEmotionCards =
                                new System.Collections.Generic.List<EmotionCardXmlInfo>();
                            allEmotionCards.AddRange(positiveCards);
                            allEmotionCards.AddRange(negativeCards);
                            // Sort ascending by unlock level; within a level, positive first.
                            allEmotionCards.Sort(
                                (a, b) =>
                                    a.Level != b.Level
                                        ? a.Level.CompareTo(b.Level)
                                        : a.State.CompareTo(b.State)
                            );

                            floorObj.AddArray(
                                "emotionCards",
                                ecArr =>
                                {
                                    foreach (var ec in allEmotionCards)
                                        ecArr.AddObject(eo =>
                                        {
                                            // Use the same text source as the battle-selection
                                            // overlay and EmotionPassiveCardUI.SetTexts:
                                            // AbnormalityCardDescXmlList keyed by ec.Name,
                                            // which is the XML ID attribute used as the dict key.
                                            var desc = cardDescList?.GetAbnormalityCard(ec.Name);
                                            var localizedName = desc?.cardName;
                                            if (
                                                string.IsNullOrEmpty(localizedName)
                                                || localizedName == "Not found"
                                            )
                                                localizedName = ec.Name;

                                            eo.Add("level", ec.Level)
                                                .Add("name", localizedName)
                                                .Add("state", ec.State.ToString())
                                                .Add("targetType", ec.TargetType.ToString())
                                                .Add("emotionLevel", ec.EmotionLevel);

                                            if (
                                                !string.IsNullOrEmpty(desc?.abnormalityName)
                                                && desc.abnormalityName != "Not found"
                                            )
                                                eo.Add("abnormalityName", desc.abnormalityName);

                                            if (
                                                !string.IsNullOrEmpty(desc?.abilityDesc)
                                                && desc.abilityDesc != "Not found"
                                            )
                                                eo.Add("desc", desc.abilityDesc);

                                            if (
                                                !string.IsNullOrEmpty(desc?.flavorText)
                                                && desc.flavorText != "Not found"
                                            )
                                                eo.Add("flavorText", desc.flavorText);
                                        });
                                }
                            );

                            // Per-librarian data nested within the floor object.
                            floorObj.AddArray(
                                "librarians",
                                libArr =>
                                {
                                    for (int ui = 0; ui < units.Count; ui++)
                                    {
                                        var unit = units[ui];
                                        if (unit == null)
                                            continue;

                                        var book = unit.bookItem;
                                        if (book == null)
                                            continue;

                                        int unitIdx = ui;
                                        libArr.AddObject(o =>
                                        {
                                            o.Add("floorIndex", floorIdx)
                                                .Add("unitIndex", unitIdx)
                                                .Add("name", unit.name);

                                            // Emit the display name of whoever holds the edit lock,
                                            // so the UI can show a "being edited by X" badge.
                                            var lockerName =
                                                Server.Instance?.SessionManager?.GetLibrarianLockerName(
                                                    floorIdx + ":" + unitIdx
                                                );
                                            if (!string.IsNullOrEmpty(lockerName))
                                                o.Add("lockedBy", lockerName);

                                            // Key page — reads base values directly from BookModel (no battle
                                            // buffs applied outside of combat).
                                            o.AddObject(
                                                "keyPage",
                                                k =>
                                                    k.Add("instanceId", book.instanceId)
                                                        .Add("name", book.Name)
                                                        .Add("speedMin", book.SpeedMin)
                                                        .Add("speedMax", book.SpeedMax)
                                                        // unit.MaxHp includes gift/bonus HP consistent
                                                        // with the BattleSetting preview.
                                                        .Add("hp", unit.MaxHp)
                                                        .Add("breakGauge", book.Break)
                                                        .Add("equipRangeType", book.ClassInfo.RangeType.ToString())
                                                        .AddObject(
                                                            "resistances",
                                                            r =>
                                                                r.Add(
                                                                        "slashHp",
                                                                        book.sHpResist.ToString()
                                                                    )
                                                                    .Add(
                                                                        "pierceHp",
                                                                        book.pHpResist.ToString()
                                                                    )
                                                                    .Add(
                                                                        "bluntHp",
                                                                        book.hHpResist.ToString()
                                                                    )
                                                                    .Add(
                                                                        "slashBp",
                                                                        book.sBpResist.ToString()
                                                                    )
                                                                    .Add(
                                                                        "pierceBp",
                                                                        book.pBpResist.ToString()
                                                                    )
                                                                    .Add(
                                                                        "bluntBp",
                                                                        book.hBpResist.ToString()
                                                                    )
                                                        )
                                            );

                                            // Passives via CreatePassiveList — covers built-in key-page
                                            // passives and equipped passive books.
                                            var passiveList = book.CreatePassiveList();
                                            o.AddArray(
                                                "passives",
                                                parr =>
                                                {
                                                    if (passiveList == null)
                                                        return;
                                                    foreach (var p in passiveList)
                                                    {
                                                        if (
                                                            p == null
                                                            || p.isHide
                                                            || string.IsNullOrEmpty(p.name)
                                                        )
                                                            continue;
                                                        parr.AddObject(po =>
                                                        {
                                                            AddLorId(po, "id", p.id);
                                                            po.Add("name", p.name)
                                                                .Add("rare", p.rare.ToString())
                                                                .Add("isNegative", p.isNegative);
                                                            if (!string.IsNullOrEmpty(p.desc))
                                                                po.Add("desc", p.desc);
                                                        });
                                                    }
                                                }
                                            );

                                            // Deck preview — collapse duplicate copies into one entry
                                            // with a count field.  GetCardListFromCurrentDeck returns one
                                            // entry per copy, so we aggregate here.
                                            var rawCards = book.GetCardListFromCurrentDeck();
                                            o.AddArray(
                                                "deckPreview",
                                                darr =>
                                                {
                                                    if (rawCards == null)
                                                        return;
                                                    var counts = new Dictionary<string, int>();
                                                    var firstSeen =
                                                        new Dictionary<
                                                            string,
                                                            LOR_DiceSystem.DiceCardXmlInfo
                                                        >();
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
                                                            // cardId lets the frontend identify the
                                                            // card for deck-editing actions.
                                                            AddLorId(c, "cardId", xml.id);
                                                            c.Add("name", xml.Name)
                                                                .Add("cost", spec.Cost)
                                                                .Add(
                                                                    "range",
                                                                    spec.Ranged.ToString()
                                                                )
                                                                .Add(
                                                                    "rarity",
                                                                    xml.Rarity.ToString()
                                                                )
                                                                .Add("count", counts[key]);

                                                            WriteDiceBehaviours(c, xml.DiceBehaviourList, abilityDescList);

                                                            var abilityDesc =
                                                                abilityDescList?.GetAbilityDescString(
                                                                    xml
                                                                ) ?? "";
                                                            if (!string.IsNullOrEmpty(abilityDesc))
                                                                c.Add("abilityDesc", abilityDesc);
                                                        });
                                                    }
                                                }
                                            );

                                            // Page-exclusive (OnlyPage) cards belonging to this book
                                            // that are currently in inventory. Serialized separately so
                                            // the deck editor can surface them first in the add-cards list.
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
                                                            var abilityDesc =
                                                                abilityDescList?.GetAbilityDescString(xml) ?? "";
                                                            if (!string.IsNullOrEmpty(abilityDesc))
                                                                c.Add("abilityDesc", abilityDesc);
                                                            WriteDiceBehaviours(c, xml.DiceBehaviourList, abilityDescList);
                                                        });
                                                    }
                                                }
                                            );

                                            // Appearance customization fields.
                                            // Always serialize so the UI can pre-fill current values.
                                            // If customizeData is null (non-customizable unit), emit zeros.
                                            var cd = unit.customizeData;
                                            Color32 hair = cd != null ? (Color32)cd.hairColor : new Color32(13, 13, 13, 255);
                                            Color32 skin = cd != null ? (Color32)cd.skinColor : new Color32(224, 188, 157, 255);
                                            Color32 eyeC = cd != null ? (Color32)cd.eyeColor  : new Color32(13, 13, 13, 255);
                                            o.AddObject("appearance", a =>
                                                a.Add("frontHairID", cd?.frontHairID ?? 0)
                                                    .Add("backHairID", cd?.backHairID ?? 0)
                                                    .Add("eyeID",       cd?.eyeID      ?? 0)
                                                    .Add("browID",      cd?.browID     ?? 0)
                                                    .Add("mouthID",     cd?.mouthID    ?? 0)
                                                    .Add("headID",      cd?.headID     ?? 0)
                                                    .Add("height",      cd?.height     ?? 175)
                                                    .AddArray("hairColor", cArr =>
                                                        cArr.AddInt(hair.r).AddInt(hair.g).AddInt(hair.b))
                                                    .AddArray("skinColor", cArr =>
                                                        cArr.AddInt(skin.r).AddInt(skin.g).AddInt(skin.b))
                                                    .AddArray("eyeColor", cArr =>
                                                        cArr.AddInt(eyeC.r).AddInt(eyeC.g).AddInt(eyeC.b))
                                            );

                                            // Per-type custom battle dialogue text.
                                            // Only serialized when the librarian has a dialogue model
                                            // (i.e. is a customizable librarian, not a Sephirah boss).
                                            var dlgModel = unit.battleDialogModel;
                                            if (dlgModel != null)
                                            {
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
                                                o.AddObject("dialogue", d =>
                                                {
                                                    for (int di = 0; di < dlgTypes.Length; di++)
                                                    {
                                                        var dlgData = dlgModel.GetDialogData(dlgTypes[di]);
                                                        // Prefer custom text; fall back to the currently-active
                                                        // preset text so the UI can pre-fill the field even when
                                                        // no explicit customization has been made yet.
                                                        string text = dlgData?.customText;
                                                        if (string.IsNullOrEmpty(text))
                                                            text = dlgData?.xmlData?.dialogContent;
                                                        d.Add(
                                                            dlgKeys[di],
                                                            string.IsNullOrEmpty(text) ? null : text
                                                        );
                                                    }
                                                });
                                            }

                                            // Title prefix/suffix gift IDs.
                                            o.AddObject("titles", t =>
                                                t.Add("prefixID", unit.prefixID)
                                                    .Add("postfixID", unit.postfixID)
                                            );

                                            // Fashion projection: which custom core book is active (-1 = none).
                                            var customBook = unit.GetCustomBookItemData();
                                            o.Add(
                                                "customBookId",
                                                customBook != null
                                                    ? customBook.GetBookClassInfoId().id
                                                    : -1
                                            );
                                        });
                                    }
                                }
                            );
                        });
                    }
                }
            );
        }

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

            // Replicate UISettingEquipPageScrollList.SetData sort + Reverse:
            //   primary  : chapter ascending
            //   secondary: workshopId descending (larger packageId first)
            //   tertiary : UIStoryLine enum value ascending
            // After Reverse → chapter descending, workshopId ascending, storyLine descending.
            availableBooks.Sort(
                (x, y) =>
                {
                    if (x.ClassInfo.Chapter != y.ClassInfo.Chapter)
                        return x.ClassInfo.Chapter.CompareTo(y.ClassInfo.Chapter);
                    int wsComp = x.ClassInfo.workshopID.CompareTo(y.ClassInfo.workshopID);
                    if (wsComp != 0)
                        return wsComp > 0 ? -1 : 1; // descending before Reverse
                    return GetStoryLineInt(x).CompareTo(GetStoryLineInt(y));
                }
            );
            availableBooks.Reverse();

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
                                .Add("hp", book.HP)
                                .Add("breakGauge", book.Break)
                                .Add("equipRangeType", book.ClassInfo.RangeType.ToString())
                                .AddObject(
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
                            var inventoryPassiveList = book.CreatePassiveList();
                            o.AddArray(
                                "passives",
                                parr =>
                                {
                                    if (inventoryPassiveList == null)
                                        return;
                                    foreach (var p in inventoryPassiveList)
                                    {
                                        if (
                                            p == null
                                            || p.isHide
                                            || string.IsNullOrEmpty(p.name)
                                        )
                                            continue;
                                        parr.AddObject(po =>
                                        {
                                            AddLorId(po, "id", p.id);
                                            po.Add("name", p.name)
                                                .Add("rare", p.rare.ToString())
                                                .Add("isNegative", p.isNegative);
                                            if (!string.IsNullOrEmpty(p.desc))
                                                po.Add("desc", p.desc);
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
            w.AddObject("customizeOptions", o =>
            {
                // Suggested name pool via reflection (LibrariansNameXmlList._dictionary is private).
                var nameXml = Singleton<LibrariansNameXmlList>.Instance;
                var nameDict =
                    typeof(LibrariansNameXmlList)
                        .GetField(
                            "_dictionary",
                            System.Reflection.BindingFlags.NonPublic
                                | System.Reflection.BindingFlags.Instance
                        )
                        ?.GetValue(nameXml)
                    as Dictionary<int, string>;

                o.AddArray("suggestedNames", arr =>
                {
                    if (nameDict == null)
                        return;
                    foreach (var name in nameDict.Values)
                        arr.AddString(name);
                });

                // Gift-based prefix and suffix titles.
                // prefixID / postfixID in UnitDataModel reference GiftXmlInfo IDs whose
                // text is looked up via GiftXmlList.GetPrefix / GetPostfix.
                var giftList = Singleton<GiftXmlList>.Instance?.GetAvailableList()
                    ?? new System.Collections.Generic.List<GiftXmlInfo>();

                o.AddArray("prefixTitles", arr =>
                {
                    foreach (var gift in giftList)
                    {
                        var text = Singleton<GiftXmlList>.Instance?.GetPrefix(gift.id) ?? "";
                        if (
                            string.IsNullOrEmpty(text)
                            || text == "Unknown Gift Prefix"
                        )
                            continue;
                        arr.AddObject(t =>
                            t.Add("id", gift.id).Add("text", text)
                        );
                    }
                });

                o.AddArray("suffixTitles", arr =>
                {
                    foreach (var gift in giftList)
                    {
                        var text = Singleton<GiftXmlList>.Instance?.GetPostfix(gift.id) ?? "";
                        if (
                            string.IsNullOrEmpty(text)
                            || text == "Unknown Gift Posfix"
                        )
                            continue;
                        arr.AddObject(t =>
                            t.Add("id", gift.id).Add("text", text)
                        );
                    }
                });

                // Fashion books: custom core books the player has unlocked and can use as
                // appearance projections. Each entry carries rangeType and skinType so the
                // frontend can filter by range-compatibility and show when the full head is
                // replaced (skinType != "Lor" means the fashion skin overrides the head).
                var ccbm = Singleton<CustomCoreBookInventoryModel>.Instance;
                var fashionIds = ccbm?.GetBookIdList_CustomCoreBook(SephirahType.None, false)
                    ?? new System.Collections.Generic.List<int>();
                o.AddArray("fashionBooks", arr =>
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
                            // Optional per-book appearance metadata from AppearanceCache.
                            if (AppearanceCache.FashionMeta.TryGetValue(bid, out var meta))
                            {
                                // Head tilt and pivot: omitted when tilt is zero.
                                if (Mathf.Abs(meta.TiltDeg) > 0.05f)
                                    fb.Add("headTiltDeg", meta.TiltDeg)
                                        .Add("pivotFracX", meta.PivotFracX)
                                        .Add("pivotFracY", meta.PivotFracY);
                                // Front layer: some body sprites render in front of the face overlay.
                                if (meta.HasFrontLayer)
                                    fb.Add("hasFrontLayer", true);
                            }
                        });
                    }
                });

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
                o.AddObject("dialoguePresets", dp =>
                {
                    foreach (var (dlgType, key) in dlgTypeMap)
                    {
                        dp.AddArray(key, arr =>
                        {
                            if (dlgXml == null)
                                return;
                            try
                            {
                                var presets = dlgXml.GetDialogPresetByType(dlgType);
                                foreach (var p in presets)
                                    arr.AddString(p.dialogContent);
                            }
                            catch { /* "Librarian" group may not exist for non-standard saves */ }
                        });
                    }
                });
            });
        }

        /// <summary>
        /// Serializes a pre-battle unit preview from a <c>UnitBattleDataModel</c>.
        /// Emits: name, HP, max stagger, key page (speed range + resistances),
        /// passives, deck card preview, and enabled status.
        /// Battle-specific fields (speedDice, slottedCards, buffs, etc.) are omitted
        /// because the battle scene has not loaded yet.
        /// </summary>
        private static void WriteUnitBattleData(
            JsonArrayWriter arr,
            int index,
            UnitBattleDataModel unit
        )
        {
            if (unit?.unitData == null)
                return;

            var book = unit.unitData.bookItem;
            arr.AddObject(o =>
            {
                o.Add("id", index)
                    .Add("name", unit.unitData.name)
                    .Add("hp", (int)unit.hp)
                    .Add("maxHp", unit.unitData.MaxHp)
                    .Add("maxStaggerGauge", book?.Break ?? 0)
                    // enabled: false when dead or locked — used by the frontend to
                    // dim unavailable units in the formation screen
                    .Add("enabled", !unit.isDead && !unit.isLocked);

                if (book == null)
                    return;

                // Key page: speed range and resistances (no dice count — display uses range only)
                o.AddObject(
                    "keyPage",
                    k =>
                        k.Add("name", book.Name)
                            .Add("speedMin", book.SpeedMin)
                            .Add("speedMax", book.SpeedMax)
                            .AddObject(
                                "resistances",
                                r =>
                                    r.Add("slashHp", book.sHpResist.ToString())
                                        .Add("pierceHp", book.pHpResist.ToString())
                                        .Add("bluntHp", book.hHpResist.ToString())
                                        .Add("slashBp", book.sBpResist.ToString())
                                        .Add("pierceBp", book.pBpResist.ToString())
                                        .Add("bluntBp", book.hBpResist.ToString())
                            )
                );

                // Passives from CreatePassiveList — covers both key-page built-in passives
                // and the librarian's equipped floor passive deck (equipedBookIdListInPassive),
                // matching what the battle phase serialises via passiveDetail.PassiveList.
                var passiveList = book.CreatePassiveList();
                if (passiveList != null && passiveList.Count > 0)
                {
                    o.AddArray(
                        "passives",
                        arr2 =>
                        {
                            foreach (var p in passiveList)
                            {
                                if (p == null || p.isHide || string.IsNullOrEmpty(p.name))
                                    continue;
                                arr2.AddObject(po =>
                                {
                                    AddLorId(po, "id", p.id);
                                    po.Add("name", p.name)
                                        .Add("rare", p.rare.ToString())
                                        .Add("isNegative", p.isNegative);
                                    if (!string.IsNullOrEmpty(p.desc))
                                        po.Add("desc", p.desc);
                                });
                            }
                        }
                    );
                }

                // Deck card preview — grouped by card type, each entry carries a count,
                // dice behaviour list, and ability description so the frontend can render
                // the same HandCard tile used during battle.
                var deckCards = book.GetDeckCardModelAll();
                if (deckCards != null && deckCards.Count > 0)
                {
                    var abilityDescList = Singleton<BattleCardAbilityDescXmlList>.Instance;
                    o.AddArray(
                        "deckPreview",
                        arr2 =>
                        {
                            foreach (var card in deckCards)
                            {
                                if (card == null)
                                    continue;
                                var spec = card.GetSpec();
                                var xml = card.ClassInfo;
                                arr2.AddObject(c =>
                                {
                                    c.Add("name", card.GetName())
                                        .Add("cost", spec.Cost)
                                        .Add("range", spec.Ranged.ToString())
                                        .Add("rarity", card.GetRarity().ToString())
                                        .Add("count", card.num);

                                    if (
                                        xml?.DiceBehaviourList != null
                                        && xml.DiceBehaviourList.Count > 0
                                    )
                                        c.AddArray(
                                            "dice",
                                            diceArr =>
                                            {
                                                foreach (var d in xml.DiceBehaviourList)
                                                    diceArr.AddObject(die =>
                                                    {
                                                        die.Add("type", d.Type.ToString())
                                                            .Add("detail", d.Detail.ToString())
                                                            .Add("min", d.Min)
                                                            .Add("max", d.Dice);
                                                        var desc =
                                                            abilityDescList?.GetAbilityDesc(d)
                                                            ?? "";
                                                        if (string.IsNullOrEmpty(desc))
                                                            desc = d.Desc ?? "";
                                                        if (!string.IsNullOrEmpty(desc))
                                                            die.Add("desc", desc);
                                                    });
                                            }
                                        );

                                    var abilityDesc =
                                        abilityDescList?.GetAbilityDescString(xml) ?? "";
                                    if (!string.IsNullOrEmpty(abilityDesc))
                                        c.Add("abilityDesc", abilityDesc);
                                });
                            }
                        }
                    );
                }
            });
        }

        // -------------------------------------------------------------------------
        // Battle state
        // -------------------------------------------------------------------------

        /// <summary>
        /// Builds a JSON representing the current battle state.
        /// </summary>
        private static string BuildBattleJson(System.Collections.Generic.HashSet<int> ownedUnitIds)
        {
            var w = new JsonWriter().Add("scene", "battle");

            var sc = Singleton<StageController>.Instance;
            if (sc != null)
            {
                w.Add("stageState", sc.State.ToString())
                    .Add("battleState", sc.battleState.ToString())
                    .Add("phase", sc.Phase.ToString())
                    .AddObject(
                        "stage",
                        s =>
                        {
                            s.Add("wave", sc.CurrentWave)
                                .Add("round", sc.RoundTurn)
                                .Add("floor", sc.CurrentFloor.ToString());
                            var stageModel = sc.GetStageModel();
                            if (stageModel?.ClassInfo != null)
                                s.Add("chapter", stageModel.ClassInfo.chapter);
                        }
                    );
            }

            var bom = BattleObjectManager.instance;
            if (bom != null)
            {
                w.AddArray(
                    "allies",
                    arr =>
                    {
                        foreach (var unit in bom.GetList(Faction.Player))
                        {
                            // ownedUnitIds == null means no filtering (SSE / full-state path).
                            bool isOwned = ownedUnitIds == null || ownedUnitIds.Contains(unit.id);
                            WriteUnit(arr, unit, isAlly: true, isOwned: isOwned);
                        }
                    }
                );
                w.AddArray(
                    "enemies",
                    arr =>
                    {
                        foreach (var unit in bom.GetList(Faction.Enemy))
                            WriteUnit(arr, unit, isAlly: false, isOwned: false);
                    }
                );
            }

            if (AbnormalitySelectionState.IsActive && AbnormalitySelectionState.Choices != null)
            {
                var descList = Singleton<AbnormalityCardDescXmlList>.Instance;
                w.AddObject(
                    "abnormalitySelection",
                    sel =>
                    {
                        sel.AddArray(
                            "choices",
                            arr =>
                            {
                                foreach (var card in AbnormalitySelectionState.Choices)
                                {
                                    if (card == null)
                                        continue;
                                    // Key is the script-name string (e.g. "bigbird1"), not the int id.
                                    // Matches EmotionPassiveCardUI.SetTexts which calls GetAbnormalityCard(card.Name).
                                    var desc = descList?.GetAbnormalityCard(card.Name);
                                    arr.AddObject(o =>
                                    {
                                        o.Add("id", card.id)
                                            .Add("name", desc?.cardName ?? card.Name)
                                            .Add("emotionLevel", card.EmotionLevel)
                                            .Add("targetType", card.TargetType.ToString())
                                            .Add("state", card.State.ToString());
                                        if (
                                            !string.IsNullOrEmpty(desc?.abilityDesc)
                                            && desc.abilityDesc != "Not found"
                                        )
                                            o.Add("desc", desc.abilityDesc);
                                        if (
                                            !string.IsNullOrEmpty(desc?.flavorText)
                                            && desc.flavorText != "Not found"
                                        )
                                            o.Add("flavorText", desc.flavorText);
                                    });
                                }
                            }
                        );

                        // Team emotion state for the selection header
                        var floor = AbnormalitySelectionState.Floor;
                        if (floor != null)
                        {
                            var team = floor.team;
                            sel.Add("teamEmotionLevel", team.emotionLevel)
                                .Add("teamCoin", team.emotionCoinNumber)
                                .Add("teamCoinMax", team.currentLevelNeedEmotionMaxCoin);

                            // Sum positive/negative coins from alive allies
                            int pos = 0,
                                neg = 0;
                            var bomInner = BattleObjectManager.instance;
                            if (bomInner != null)
                                foreach (var u in bomInner.GetAliveList(Faction.Player))
                                {
                                    var ed = u?.emotionDetail;
                                    if (ed == null)
                                        continue;
                                    pos += ed.PositiveCoins.Count;
                                    neg += ed.NegativeCoins.Count;
                                }
                            sel.Add("teamPositiveCoins", pos).Add("teamNegativeCoins", neg);
                        }
                    }
                );
            }

            return w.Build();
        }

        /// <summary>
        /// Writes a JSON object representing a unit in battle.
        /// </summary>
        private static void WriteUnit(
            JsonArrayWriter aw,
            BattleUnitModel unit,
            bool isAlly,
            bool isOwned
        )
        {
            if (unit == null)
                return;
            aw.AddObject(w =>
            {
                w.Add("id", unit.id)
                    .Add("index", unit.index)
                    .Add("name", unit.UnitData?.unitData?.name)
                    .Add("turnState", unit.turnState.ToString())
                    .Add("hp", (int)unit.hp)
                    .Add("maxHp", unit.MaxHp)
                    .Add("staggerGauge", unit.breakDetail.breakGauge)
                    .Add("maxStaggerGauge", unit.breakDetail.GetDefaultBreakGauge())
                    .Add("staggerThreshold", unit.breakDetail.breakLife)
                    .Add("targetable", unit.IsTargetable(null))
                    .Add("playPoint", unit.PlayPoint)
                    .Add("maxPlayPoint", unit.MaxPlayPoint)
                    .Add("reservedPlayPoint", unit.cardSlotDetail?.ReservedPlayPoint ?? 0);

                if (unit.Book != null)
                    WriteKeyPage(w, unit);

                WriteSpeedDice(w, unit);
                WriteSlottedCards(w, unit);
                WritePassives(w, unit);
                WriteBuffs(w, unit);
                WriteEmotion(w, unit);

                if (isAlly)
                {
                    if (isOwned)
                        WriteAllyCards(w, unit);
                    else
                        WriteAllyCardCounts(w, unit);
                }
            });
        }

        /// <summary>
        /// Writes a JSON object representing an equipped key page.
        /// </summary>
        private static void WriteKeyPage(JsonWriter w, BattleUnitModel unit)
        {
            var book = unit.Book;
            var bufs = unit.bufListDetail;
            w.AddObject(
                "keyPage",
                k =>
                {
                    if (book.ClassInfo != null)
                        AddLorId(k, "id", book.ClassInfo.id);

                    k.Add("name", book.Name)
                        .Add("speedDiceCount", book.SpeedDiceNum)
                        .Add("speedMin", book.equipeffect.SpeedMin)
                        .Add("speedMax", book.equipeffect.Speed)
                        .AddObject(
                            "resistances",
                            r =>
                            {
                                // Route through bufListDetail so mid-battle resistance changes
                                // (e.g. from abnormality page buffs like Blessing) are reflected.
                                r.Add(
                                        "slashHp",
                                        bufs.GetResistHP(book.sHpResist, BehaviourDetail.Slash)
                                            .ToString()
                                    )
                                    .Add(
                                        "pierceHp",
                                        bufs.GetResistHP(book.pHpResist, BehaviourDetail.Penetrate)
                                            .ToString()
                                    )
                                    .Add(
                                        "bluntHp",
                                        bufs.GetResistHP(book.hHpResist, BehaviourDetail.Hit)
                                            .ToString()
                                    )
                                    .Add(
                                        "slashBp",
                                        bufs.GetResistBP(book.sBpResist, BehaviourDetail.Slash)
                                            .ToString()
                                    )
                                    .Add(
                                        "pierceBp",
                                        bufs.GetResistBP(book.pBpResist, BehaviourDetail.Penetrate)
                                            .ToString()
                                    )
                                    .Add(
                                        "bluntBp",
                                        bufs.GetResistBP(book.hBpResist, BehaviourDetail.Hit)
                                            .ToString()
                                    );
                            }
                        );
                }
            );
        }

        /// <summary>
        /// Writes a JSON object representing speed dice on a unit in battle.
        /// </summary>
        private static void WriteSpeedDice(JsonWriter w, BattleUnitModel unit)
        {
            w.AddArray(
                "speedDice",
                arr =>
                {
                    var dice = unit.speedDiceResult;
                    if (dice != null)
                    {
                        for (int i = 0; i < dice.Count; i++)
                        {
                            var d = dice[i];
                            arr.AddObject(o =>
                                o.Add("slot", i).Add("value", d.value).Add("staggered", d.breaked)
                            );
                        }
                        return;
                    }

                    // Dice not yet rolled — emit placeholder slots so the frontend can
                    // render them as invalid/empty rather than showing no dice at all.
                    // Use GetSpeedDiceRule so passive/buff break adders are reflected (e.g. Yujin's
                    // first die starts broken), matching what RollSpeedDice will produce.
                    var rule = unit.Book?.GetSpeedDiceRule(unit);
                    if (rule == null)
                        return;
                    for (int i = 0; i < rule.speedDiceList.Count; i++)
                    {
                        var d = rule.speedDiceList[i];
                        arr.AddObject(o =>
                            o.Add("slot", i).Add("value", 0).Add("staggered", d.breaked)
                        );
                    }
                }
            );
        }

        /// <summary>
        /// Writes a JSON object representing a unit's slotted cards.<para/>
        ///
        /// ie. cards assigned to speed dice before the combat phase starts.
        /// </summary>
        /// <param name="w"></param>
        /// <param name="unit"></param>
        private static void WriteSlottedCards(JsonWriter w, BattleUnitModel unit)
        {
            w.AddArray(
                "slottedCards",
                arr =>
                {
                    var slots = unit.cardSlotDetail?.cardAry;
                    if (slots == null)
                        return;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        var slot = slots[i];
                        if (slot?.card == null)
                            continue;
                        int slotIdx = i;
                        arr.AddObject(o =>
                        {
                            o.Add("slot", slotIdx);
                            AddLorId(o, "cardId", slot.card.GetID());
                            o.Add("name", slot.card.GetName())
                                .Add("cost", slot.card.GetCost())
                                .Add("range", slot.card.GetSpec().Ranged.ToString());
                            WriteCardFields(o, slot.card);
                            if (slot.target != null)
                            {
                                // Mirror of the in-game clash check in UpdateTargetListData:
                                // A[slotIdx] -> B[targetSlotOrder] is a clash iff B[targetSlotOrder] -> A[slotIdx].
                                var opposing = slot.target.cardSlotDetail?.cardAry;
                                bool isClash =
                                    opposing != null
                                    && slot.targetSlotOrder < opposing.Count
                                    && opposing[slot.targetSlotOrder]?.card != null
                                    && opposing[slot.targetSlotOrder].target == unit
                                    && opposing[slot.targetSlotOrder].targetSlotOrder == slotIdx;
                                o.Add("targetUnitId", slot.target.id)
                                    .Add("targetSlot", slot.targetSlotOrder)
                                    .Add("clash", isClash);
                                if (slot.subTargets != null && slot.subTargets.Count > 0)
                                {
                                    o.AddArray(
                                        "subTargets",
                                        arr2 =>
                                        {
                                            foreach (var st in slot.subTargets)
                                            {
                                                if (st?.target == null)
                                                    continue;
                                                arr2.AddObject(o2 =>
                                                    o2.Add("targetUnitId", st.target.id)
                                                        .Add("targetSlot", st.targetSlotOrder)
                                                );
                                            }
                                        }
                                    );
                                }
                            }
                        });
                    }
                }
            );
        }

        /// <summary>
        /// Writes a JSON object representing a unit in battle's passives.
        /// </summary>
        /// <param name="w"></param>
        /// <param name="unit"></param>
        private static void WritePassives(JsonWriter w, BattleUnitModel unit)
        {
            w.AddArray(
                "passives",
                arr =>
                {
                    var list = unit.passiveDetail?.PassiveList;
                    if (list == null)
                        return;
                    foreach (var p in list.Where(p => p != null && !p.destroyed && !p.isHide))
                    {
                        arr.AddObject(o =>
                        {
                            AddLorId(o, "id", p.id);
                            o.Add("name", p.name)
                                .Add("desc", p.desc)
                                .Add("rare", p.rare.ToString())
                                .Add("disabled", p.disabled)
                                .Add("isNegative", p.isNegative);
                        });
                    }
                }
            );
        }

        /// <summary>
        /// Writes a JSON object representing a unit in battle's buffs/status effects.
        /// </summary>
        private static void WriteBuffs(JsonWriter w, BattleUnitModel unit)
        {
            w.AddArray(
                "buffs",
                arr =>
                {
                    var list = unit.bufListDetail?.GetActivatedBufList();
                    if (list == null)
                        return;
                    foreach (var buf in list)
                    {
                        if (buf == null || buf.Hide)
                            continue;
                        var kwType = buf.bufType;
                        string typeName =
                            kwType != KeywordBuf.None
                                ? kwType.ToString()
                                : buf.GetType().Name.Replace("BattleUnitBuf_", "");

                        var name = buf.bufActivatedName;
                        var iconId = IconCache.EnsureIcon(buf.GetBufIcon());
                        var desc = buf.bufActivatedText;

                        // Skip internal buffs with no displayable identity
                        if (string.IsNullOrEmpty(name) && iconId == null)
                            continue;

                        arr.AddObject(o =>
                        {
                            o.Add("type", typeName).Add("stacks", buf.stack);
                            if (!string.IsNullOrEmpty(name))
                                o.Add("name", name);
                            if (iconId != null)
                                o.Add("icon", iconId);
                            if (!string.IsNullOrEmpty(desc))
                                o.Add("desc", desc);
                            o.Add("positive", buf.positiveType.ToString());
                        });
                    }
                }
            );
        }

        /// <summary>
        /// Writes a JSON object representing a unit's emotion level and abnormality pages.
        /// </summary>
        private static void WriteEmotion(JsonWriter w, BattleUnitModel unit)
        {
            var ed = unit.emotionDetail;
            if (ed == null)
                return;

            w.Add("emotionLevel", ed.EmotionLevel)
                .Add("maxEmotionLevel", ed.MaximumEmotionLevel)
                .AddObject(
                    "emotionCoins",
                    c =>
                    {
                        c.Add("positive", ed.PositiveCoins.Count)
                            .Add("negative", ed.NegativeCoins.Count)
                            .Add("total", ed.AllEmotionCoins.Count)
                            .Add("max", ed.MaximumCoinNumber);
                    }
                )
                .AddArray(
                    "abnormalities",
                    arr =>
                    {
                        var passiveList = ed.PassiveList;
                        if (passiveList == null)
                            return;
                        foreach (var ab in passiveList)
                        {
                            if (ab?.XmlInfo == null)
                                continue;
                            arr.AddObject(o =>
                                o.Add("id", ab.XmlInfo.id)
                                    .Add("name", ab.XmlInfo.Name)
                                    .Add("emotionLevel", ab.XmlInfo.EmotionLevel)
                            );
                        }
                    }
                );
        }

        /// <summary>
        /// Writes a JSON object representing an ally unit's available cards.<para/>
        ///
        /// This includes personal hand, deck, and personal/abnormality EGO pages.
        /// </summary>
        private static void WriteAllyCards(JsonWriter w, BattleUnitModel unit)
        {
            // Personal hand and deck
            WriteCardList(w, "hand", unit.allyCardDetail?.GetHand(), unit);
            WriteCardList(w, "deck", unit.allyCardDetail?.GetDeck());

            // Personal EGO pages (available = in GetHand, unavailable = in use/cooldown)
            var egoAll = unit.personalEgoDetail?.GetCardAll();
            var egoHand = unit.personalEgoDetail?.GetHand();
            if (egoAll != null)
            {
                w.AddArray(
                    "ego",
                    arr =>
                    {
                        foreach (var card in egoAll)
                        {
                            if (card == null)
                                continue;
                            bool available = egoHand != null && egoHand.Contains(card);
                            arr.AddObject(o =>
                            {
                                AddLorId(o, "id", card.GetID());
                                o.Add("name", card.GetName())
                                    .Add("cost", card.GetCost())
                                    .Add("range", card.GetSpec().Ranged.ToString())
                                    .Add("allyTarget", card.IsOnlyAllyUnit())
                                    .Add("available", available)
                                    .Add(
                                        "canUse",
                                        available && unit.CheckCardAvailableForPlayer(card)
                                    );
                                WriteCardFields(o, card);
                            });
                        }
                    }
                );
            }
        }

        /// <summary>
        /// Writes a JSON object representing a list of cards associated with a given key.
        ///
        /// If a user is included, the availability of each card is also included, ie. whether it is not disabled.
        /// It does not check light cost.
        /// </summary>
        /// <summary>
        /// Written for unowned allies: exposes card counts without revealing identities.
        /// The frontend shows these as "N cards" summaries.
        /// </summary>
        private static void WriteAllyCardCounts(JsonWriter w, BattleUnitModel unit)
        {
            w.Add("handCount", unit.allyCardDetail?.GetHand()?.Count ?? 0)
                .Add("deckCount", unit.allyCardDetail?.GetDeck()?.Count ?? 0)
                .Add("egoCount", unit.personalEgoDetail?.GetCardAll()?.Count ?? 0);
        }

        private static void WriteCardList(
            JsonWriter w,
            string key,
            List<BattleDiceCardModel> cards,
            BattleUnitModel unit = null
        )
        {
            w.AddArray(
                key,
                arr =>
                {
                    if (cards == null)
                        return;
                    foreach (var card in cards)
                    {
                        if (card == null)
                            continue;
                        arr.AddObject(o =>
                        {
                            AddLorId(o, "id", card.GetID());
                            o.Add("name", card.GetName())
                                .Add("cost", card.GetCost())
                                .Add("range", card.GetSpec().Ranged.ToString())
                                .Add("allyTarget", card.IsOnlyAllyUnit());
                            if (unit != null)
                                o.Add("canUse", unit.CheckCardAvailableForPlayer(card));
                            WriteCardFields(o, card);
                        });
                    }
                }
            );
        }

        /// <summary>
        /// A helper that writes JSON fields common to all cards.<para/>
        ///
        /// Also includes tokens on cards, eg. Black Silence passive, Index unlock passive, Matchlight abnormality page.
        /// </summary>
        private static void WriteCardFields(JsonWriter o, BattleDiceCardModel card)
        {
            var xml = card.XmlData;
            var abilityDescList = Singleton<BattleCardAbilityDescXmlList>.Instance;

            o.Add("rarity", card.GetRarity().ToString())
                .Add("emotionLimit", card.GetSpec().emotionLimit)
                .Add("baseCost", card.GetSpec().Cost);

            // Card tokens (placed by passives, abnormalities, or special card abilities)
            var bufs = card.GetBufList();
            if (bufs != null && bufs.Count > 0)
            {
                o.AddArray(
                    "bufs",
                    arr =>
                    {
                        foreach (var buf in bufs)
                        {
                            if (buf == null || buf.GetBufIcon() == null)
                                continue;
                            var label = buf.bufActivatedText;
                            if (string.IsNullOrEmpty(label))
                                label =
                                    buf.bufType != DiceCardBufType.None
                                        ? buf.bufType.ToString()
                                        : buf.GetType()
                                            .Name.Replace("BattleDiceCardBuf_", "")
                                            .Replace("CardBuf", "");
                            var iconId = IconCache.EnsureCardIcon(buf.GetBufIcon());
                            arr.AddObject(o2 =>
                            {
                                o2.Add("label", label);
                                if (buf.Stack > 0)
                                    o2.Add("stack", buf.Stack);
                                if (iconId != null)
                                    o2.Add("icon", iconId);
                            });
                        }
                    }
                );
            }

            // Options (EGO, ExhaustOnUse, Personal, etc.)
            if (xml.optionList != null && xml.optionList.Count > 0)
                o.AddArray(
                    "options",
                    arr =>
                    {
                        foreach (var opt in xml.optionList)
                            arr.AddString(opt.ToString());
                    }
                );

            // Card-level ability text — mirrors BattleDiceCardUI which uses
            // BattleCardAbilityDescXmlList keyed by Script name (not the old BattleCardDescXmlList).
            // GetAbilityDescString also prepends default text for FarArea / ExhaustOnUse.
            var abilityDesc = abilityDescList?.GetAbilityDescString(xml) ?? "";
            if (!string.IsNullOrEmpty(abilityDesc))
                o.Add("abilityDesc", abilityDesc);

            // Dice behaviours
            WriteDiceBehaviours(o, xml.DiceBehaviourList, abilityDescList);
        }

        /// <summary>
        /// Writes a "dice" array for a card's DiceBehaviourList.
        /// Skips the array entirely when the list is null or empty.
        /// </summary>
        private static void WriteDiceBehaviours(
            JsonWriter o,
            IEnumerable<DiceBehaviour> behaviours,
            BattleCardAbilityDescXmlList abilityDescList
        )
        {
            if (behaviours == null)
                return;
            var list =
                behaviours as IList<DiceBehaviour>
                ?? new List<DiceBehaviour>(behaviours);
            if (list.Count == 0)
                return;
            o.AddArray(
                "dice",
                arr =>
                {
                    foreach (var d in list)
                        arr.AddObject(die =>
                        {
                            die.Add("type", d.Type.ToString())
                                .Add("detail", d.Detail.ToString())
                                .Add("min", d.Min)
                                .Add("max", d.Dice);
                            var desc = abilityDescList?.GetAbilityDesc(d) ?? "";
                            if (string.IsNullOrEmpty(desc))
                                desc = d.Desc ?? "";
                            if (!string.IsNullOrEmpty(desc))
                                die.Add("desc", desc);
                        });
                }
            );
        }

        /// <summary>
        /// A helper that adds the package ID to a JSON object for a specific key.<para/>
        ///
        /// Useful for workshop cards.
        /// </summary>
        private static void AddLorId(JsonWriter w, string key, LorId lorId)
        {
            w.AddObject(
                key,
                o => o.Add("id", lorId?.id ?? -1).Add("packageId", lorId?.packageId ?? "")
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
    }
}
