namespace PlayLoRWithMe
{
    /// <summary>
    /// Main-library scene writers: the floor roster (per-Sephirah realization, EGO and
    /// abnormality pages) and the BattleSetting formation previews, which read
    /// pre-battle data models because the battle scene has not loaded yet.
    /// </summary>
    public static partial class GameStateSerializer
    {
        /// <summary>
        /// Serializes the main library/floor/librarian management scene.
        /// During <c>BattleSetting</c> phase also emits pre-battle ally/enemy
        /// previews so the frontend can render the formation screen.
        /// </summary>
        private static void WriteMainScene(JsonWriter w)
        {
            var uic = UI.UIController.Instance;
            if (uic != null)
                w.Add("uiPhase", uic.CurrentUIPhase.ToString());

            if (uic?.CurrentUIPhase == UI.UIPhase.BattleSetting)
                WriteBattleSettingData(w);
            else
            {
                WriteFloors(w);
                // WriteLibraryInventory calls WriteCustomizeOptions internally.
                WriteLibraryInventory(w);
            }
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

            var sephirahs = Sephirahs;

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

                            WriteFloorEgoCards(
                                floorObj,
                                sephirah,
                                floor,
                                egoCardList,
                                abilityDescList
                            );

                            WriteFloorEmotionCards(
                                floorObj,
                                sephirah,
                                floor,
                                emotionCardList,
                                cardDescList
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
                                            WriteLibrarian(
                                                o,
                                                unit,
                                                book,
                                                floorIdx,
                                                unitIdx,
                                                abilityDescList
                                            )
                                        );
                                    }
                                }
                            );
                        });
                    }
                }
            );
        }

        /// <summary>
        /// Appends the floor's EGO page list ("egoCards"). EGO pages are only available
        /// at max realization (level 6) -- this matches UIEgoCardPanel.SetData, which
        /// empties all slots when floor.Level &lt; 6.
        /// </summary>
        private static void WriteFloorEgoCards(
            JsonWriter floorObj,
            SephirahType sephirah,
            LibraryFloorModel floor,
            EmotionEgoXmlList egoCardList,
            BattleCardAbilityDescXmlList abilityDescList
        )
        {
            var egoCards = new System.Collections.Generic.List<LOR_DiceSystem.DiceCardXmlInfo>();
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
                            WriteRarityColorOverrides(c, xml.id?.packageId, xml.Rarity, xml);

                            WriteDiceBehaviours(c, xml.DiceBehaviourList, abilityDescList);

                            var abilityDesc = abilityDescList?.GetAbilityDescString(xml) ?? "";
                            if (!string.IsNullOrEmpty(abilityDesc))
                                c.Add("abilityDesc", abilityDesc);
                        });
                    }
                }
            );
        }

        /// <summary>
        /// Appends the floor's abnormality page list ("emotionCards") -- the
        /// Awakening/Breakdown pages available at or below the current realization level.
        /// Sorted by unlock level ascending, then positive-before-negative, so the
        /// frontend can group them cleanly by abnormality encounter.
        /// </summary>
        private static void WriteFloorEmotionCards(
            JsonWriter floorObj,
            SephirahType sephirah,
            LibraryFloorModel floor,
            EmotionCardXmlList emotionCardList,
            AbnormalityCardDescXmlList cardDescList
        )
        {
            var positiveCards =
                emotionCardList?.GetDataList(sephirah, floor.Level, MentalState.Positive)
                ?? new System.Collections.Generic.List<EmotionCardXmlInfo>();
            var negativeCards =
                emotionCardList?.GetDataList(sephirah, floor.Level, MentalState.Negative)
                ?? new System.Collections.Generic.List<EmotionCardXmlInfo>();

            var allEmotionCards = new System.Collections.Generic.List<EmotionCardXmlInfo>();
            allEmotionCards.AddRange(positiveCards);
            allEmotionCards.AddRange(negativeCards);
            // Sort ascending by unlock level; within a level, positive first.
            allEmotionCards.Sort(
                (a, b) =>
                    a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.State.CompareTo(b.State)
            );

            floorObj.AddArray(
                "emotionCards",
                ecArr =>
                {
                    foreach (var ec in allEmotionCards)
                        ecArr.AddObject(eo =>
                        {
                            // Use the same text source as the battle-selection overlay and
                            // EmotionPassiveCardUI.SetTexts: AbnormalityCardDescXmlList keyed by
                            // ec.Name, which is the XML ID attribute used as the dict key.
                            var desc = cardDescList?.GetAbnormalityCard(ec.Name);
                            var localizedName = desc?.cardName;
                            if (string.IsNullOrEmpty(localizedName) || localizedName == "Not found")
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

                // Key page: speed range and resistances (no dice count — display uses range only).
                // Rarity is intentionally omitted here so the BattleSetting preview shows no
                // rarity outline — rarity is a customization concern, not a tactical one.
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
                                    var passiveXml = Singleton<PassiveXmlList>.Instance?.GetData(
                                        p.id
                                    );
                                    if (passiveXml != null)
                                        po.Add("cost", passiveXml.cost);
                                    WriteRarityColorOverrides(po, p.id?.packageId, p.rare, passiveXml);
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
                                    WriteRarityColorOverrides(
                                        c,
                                        xml?.id?.packageId,
                                        card.GetRarity(),
                                        xml
                                    );

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
    }
}
