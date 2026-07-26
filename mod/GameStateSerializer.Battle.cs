using LOR_DiceSystem;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Battle-scene writers: units, key pages, speed dice, slotted cards, passives,
    /// buffs, emotion, ally hands, and the emotion level-up selection payloads.
    /// </summary>
    public static partial class GameStateSerializer
    {
        // -------------------------------------------------------------------------
        // Battle state
        // -------------------------------------------------------------------------

        /// <summary>
        /// Serializes the full battle state including units, slotted cards,
        /// and abnormality selection.
        /// </summary>
        private static void WriteBattleScene(JsonWriter w)
        {
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

            // Mirror vanilla `BattleUnitTargetArrowManagerUI.Show{Enemy,Parrying}Arrow`,
            // which short-circuit when `StageController.IsVisibleEnemyTarget()` is false
            // (e.g. The Crying Children's Page encounter while `PassiveAbility_240428`
            // Unhearing Child is alive and undestroyed). When the gate is closed the
            // base game hides every enemy outgoing arrow and every parrying/clash arrow;
            // we mirror that by suppressing enemy slottedCards target fields and forcing
            // ally `clash: false`. Fail-open (gate emits fields) on any error, matching
            // vanilla's `try/catch` fallback inside `IsVisibleEnemyTarget`.
            bool enemyTargetsHidden = false;
            try
            {
                if (sc != null && !sc.IsVisibleEnemyTarget())
                    enemyTargetsHidden = true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PRWM] IsVisibleEnemyTarget probe failed: {ex}");
            }

            // Mirror vanilla `BattleUnitProfileInfoUI`'s enemy-card preview, which calls
            // `BattleDiceCardUI.SetCard(card, Option.HideDiceAbilityInfo)` when
            // `StageController.IsHideEnemyDiceAbilityInfo()` is true (Crying Children's
            // Page encounter, passive `PassiveAbility_240328` Unseeing Child). That option
            // routes through `BattleDiceCard_BehaviourDescUI.SetBehaviourInfo` and replaces
            // every per-die description text with the literal `"???"`. We mirror that
            // exactly by masking the per-die `desc` of every enemy-owned slotted card
            // when the gate is closed; card name / cost / range / ability text / dice
            // type / dice values are untouched, matching the in-game behavior.
            // Fail-open on any probe error.
            bool dieDescriptionsHidden = false;
            try
            {
                if (sc != null && sc.IsHideEnemyDiceAbilityInfo())
                    dieDescriptionsHidden = true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PRWM] IsHideEnemyDiceAbilityInfo probe failed: {ex}");
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
                            WriteUnit(
                                arr,
                                unit,
                                isAlly: true,
                                enemyTargetsHidden: enemyTargetsHidden,
                                dieDescriptionsHidden: dieDescriptionsHidden
                            );
                        }
                    }
                );
                w.AddArray(
                    "enemies",
                    arr =>
                    {
                        foreach (var unit in bom.GetList(Faction.Enemy))
                            WriteUnit(
                                arr,
                                unit,
                                isAlly: false,
                                enemyTargetsHidden: enemyTargetsHidden,
                                dieDescriptionsHidden: dieDescriptionsHidden
                            );
                    }
                );
            }

            if (EgoSelectionState.IsActive && EgoSelectionState.Choices != null)
            {
                WriteEgoSelection(w);
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
        }

        /// <summary>
        /// Writes the <c>egoSelection</c> field for the level-up UI's EGO branch.
        /// Resolves each <see cref="EmotionEgoXmlInfo"/> to its <see cref="DiceCardXmlInfo"/>
        /// via <see cref="ItemXmlDataList"/> and surfaces enough metadata for the picker tile
        /// to render an informed pick (name, cost, range, rarity, dice, ability description).
        /// Mirrors the team-emotion header the abnormality block emits.
        /// </summary>
        private static void WriteEgoSelection(JsonWriter w)
        {
            var abilityDescList = Singleton<BattleCardAbilityDescXmlList>.Instance;
            w.AddObject(
                "egoSelection",
                sel =>
                {
                    sel.AddArray(
                        "choices",
                        arr =>
                        {
                            foreach (var ego in EgoSelectionState.Choices)
                            {
                                if (ego == null)
                                    continue;
                                var xml = ItemXmlDataList.instance.GetCardItem(ego.CardId);
                                if (xml == null)
                                    continue;
                                arr.AddObject(o =>
                                {
                                    o.Add("id", ego.id);
                                    AddLorId(o, "cardId", xml.id);
                                    // Base spec cost — these EGO cards aren't owned by any unit
                                    // at selection time, so per-owner cost reductions don't apply.
                                    o.Add("name", xml.Name)
                                        .Add("cost", xml.Spec.Cost)
                                        .Add("range", xml.Spec.Ranged.ToString())
                                        .Add("rarity", xml.Rarity.ToString())
                                        .Add("sephirah", ego.Sephirah.ToString());
                                    WriteRarityColorOverrides(o, xml.id?.packageId, xml.Rarity, xml);
                                    var abilityDesc =
                                        abilityDescList?.GetAbilityDescString(xml) ?? "";
                                    if (
                                        !string.IsNullOrEmpty(abilityDesc)
                                        && abilityDesc != "Not found"
                                    )
                                        o.Add("desc", abilityDesc);
                                    WriteDiceBehaviours(o, xml.DiceBehaviourList, abilityDescList);
                                });
                            }
                        }
                    );

                    // Team emotion state for the selection header — same shape the
                    // abnormality selection emits so the frontend chrome is consistent.
                    var floor = EgoSelectionState.Floor;
                    if (floor != null)
                    {
                        var team = floor.team;
                        sel.Add("teamEmotionLevel", team.emotionLevel)
                            .Add("teamCoin", team.emotionCoinNumber)
                            .Add("teamCoinMax", team.currentLevelNeedEmotionMaxCoin);

                        int pos = 0,
                            neg = 0;
                        var bom = BattleObjectManager.instance;
                        if (bom != null)
                            foreach (var u in bom.GetAliveList(Faction.Player))
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

        /// <summary>
        /// Writes a JSON object representing a unit in battle.
        /// </summary>
        private static void WriteUnit(
            JsonArrayWriter aw,
            BattleUnitModel unit,
            bool isAlly,
            bool enemyTargetsHidden = false,
            bool dieDescriptionsHidden = false
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
                    .Add("light", unit.PlayPoint)
                    .Add("maxLight", unit.MaxPlayPoint)
                    .Add("reservedLight", unit.cardSlotDetail?.ReservedPlayPoint ?? 0);

                // Emit only when controllability is denied (mind-control / charm
                // buffs that override IsControllable). Vanilla doesn't draw any
                // overlay for these — the unit just acts on its own — so the
                // frontend treats this flag the same way it treats an unclaimed
                // unit: dimmed dice, no beckon, action gating. Default-true
                // (omitted) keeps the payload lean.
                if (unit.bufListDetail != null && !unit.bufListDetail.IsControlable())
                    w.Add("controllable", false);

                // Per-actor target restriction (e.g. BigBird_Eye's "Stared At" — the
                // affected unit may only target the inflicter). Mirrors the in-game
                // BattleUnitCardsInHandUI.BlockOtherUnitsDice path that consults
                // `selectedUnit.GetFixedTargets()` at die-tap time: when this list is
                // non-empty, every other valid target is dimmed while a die on this
                // unit is selected. Omitted when empty (the common case) to keep
                // the payload small.
                var fixedTargets = unit.GetFixedTargets();
                if (fixedTargets != null && fixedTargets.Count > 0)
                    w.AddArray(
                        "fixedTargets",
                        arr =>
                        {
                            foreach (var t in fixedTargets)
                                if (t != null)
                                    arr.AddInt(t.id);
                        }
                    );

                // Optional per-unit speed-die colours. `dieColor` tints the
                // inner hex fill (frame sprite mean); `dieAccentColor` tints
                // the numerals (CDC's _rouletteImg tint, which it also paints
                // onto img_tensNum / img_unitsNum in-game). The frontend
                // derives the outline as a lightened shade of `dieColor` via
                // CSS color-mix, so both elements stay in the same family.
                var dieColors = CustomDiceColorProbe.TryGet(unit);
                if (dieColors.Fill != null)
                    w.Add("dieColor", dieColors.Fill);
                if (dieColors.Accent != null)
                    w.Add("dieAccentColor", dieColors.Accent);

                if (unit.Book != null)
                    WriteKeyPage(w, unit);

                WriteSpeedDice(w, unit);
                WriteSlottedCards(w, unit, isAlly, enemyTargetsHidden, dieDescriptionsHidden);
                WritePassives(w, unit);
                WriteBuffs(w, unit);
                WriteEmotion(w, unit);

                if (isAlly)
                    WriteAllyCards(w, unit);
            });
        }

        /// <summary>
        /// Writes a JSON object representing an equipped key page.
        /// </summary>
        /// <remarks>
        /// Rarity is intentionally omitted from this battle-context payload so combat
        /// surfaces never display a rarity outline. Rarity is only emitted on
        /// librarian-owned and inventory key page emission sites.
        /// </remarks>
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
        /// <remarks>
        /// <para>The <c>locked</c> flag reflects only the in-game lock overlay:
        /// vanilla draws the lock root via <c>SpeedDiceSetter.BreakDice</c>
        /// when <c>HasStun()</c> is true and the die is <c>breaked</c>. Stun
        /// marks dice <c>breaked</c> via <c>SpeedDiceBreakedAdder</c>; mirroring
        /// the setter we emit <c>locked=true, staggered=false</c> for that
        /// combination so the frontend's broken-priority rule doesn't show the
        /// X glyph for stunned dice.</para>
        ///
        /// <para>Per-die <c>!isControlable</c> (e.g. clock EGO) is reported
        /// separately via the <c>controllable</c> field: vanilla doesn't draw
        /// any overlay for that case — the die looks normal but the click
        /// handler bails out. The frontend mirrors that and flashes a red
        /// rejection cue on click instead of painting a lock.</para>
        ///
        /// <para>Unit-level <c>!IsControlable()</c> (mind-control / charm buffs)
        /// is reported on the unit via <c>controllable</c>; the frontend reuses
        /// the unclaimed-unit affordance for that state.</para>
        /// </remarks>
        private static void WriteSpeedDice(JsonWriter w, BattleUnitModel unit)
        {
            bool hasStun = unit.bufListDetail != null && unit.bufListDetail.HasStun();
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
                            bool stunLocked = hasStun && d.breaked;
                            bool staggered = d.breaked && !stunLocked;
                            arr.AddObject(o =>
                            {
                                o.Add("slot", i)
                                    .Add("value", d.value)
                                    .Add("staggered", staggered)
                                    .Add("locked", stunLocked);
                                if (!d.isControlable)
                                    o.Add("controllable", false);
                            });
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
                        bool stunLocked = hasStun && d.breaked;
                        bool staggered = d.breaked && !stunLocked;
                        arr.AddObject(o =>
                        {
                            o.Add("slot", i)
                                .Add("value", 0)
                                .Add("staggered", staggered)
                                .Add("locked", stunLocked);
                            if (!d.isControlable)
                                o.Add("controllable", false);
                        });
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
        /// <param name="isAlly">True for player-faction units. Drives the enemy-targets gate.</param>
        /// <param name="enemyTargetsHidden">When true (vanilla `StageController.IsVisibleEnemyTarget()` is false),
        /// suppress enemy-side `targetUnitId`/`targetSlot`/`clash`/`subTargets` entirely, and force ally `clash: false`.
        /// Mirrors `BattleUnitTargetArrowManagerUI.Show{Enemy,Parrying}Arrow` short-circuits in the base game
        /// (e.g. while Unhearing Child / `PassiveAbility_240428` is alive on the Crying Children's Page).</param>
        /// <param name="dieDescriptionsHidden">When true (vanilla `StageController.IsHideEnemyDiceAbilityInfo()` is true),
        /// mask the per-die `desc` of every enemy-owned slotted card to the literal `"???"`. Mirrors
        /// `BattleDiceCard_BehaviourDescUI.SetBehaviourInfo`'s `isHide` branch (e.g. while Unseeing Child /
        /// `PassiveAbility_240328` is alive on the Crying Children's Page). Ally slotted cards are unaffected
        /// because vanilla only gates enemy-owned card previews.</param>
        private static void WriteSlottedCards(
            JsonWriter w,
            BattleUnitModel unit,
            bool isAlly,
            bool enemyTargetsHidden,
            bool dieDescriptionsHidden
        )
        {
            w.AddArray(
                "slottedCards",
                arr =>
                {
                    var slots = unit.cardSlotDetail?.cardAry;
                    if (slots == null)
                        return;
                    // Enemy outgoing arrows are hidden entirely; ally outgoing arrows
                    // still draw (vanilla `ShowAllyArrow` does not check the gate) but
                    // the clash marker must drop because clash detection requires the
                    // enemy-side data we are hiding.
                    bool suppressTargetFields = enemyTargetsHidden && !isAlly;
                    bool forceClashFalse = enemyTargetsHidden && isAlly;
                    // Vanilla only masks enemy-owned card previews (BattleUnitProfileInfoUI
                    // checks `card.owner.faction == Faction.Enemy` before applying the gate),
                    // so allied slotted-card descriptions remain visible even when an enemy
                    // holds the Unseeing Child passive.
                    bool maskDie = dieDescriptionsHidden && !isAlly;
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
                            WriteCardFields(o, slot.card, maskDie);
                            if (slot.target != null && !suppressTargetFields)
                            {
                                // Mirror of the in-game clash check in UpdateTargetListData:
                                // A[slotIdx] -> B[targetSlotOrder] is a clash iff B[targetSlotOrder] -> A[slotIdx].
                                var opposing = slot.target.cardSlotDetail?.cardAry;
                                bool isClash =
                                    !forceClashFalse
                                    && opposing != null
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
                    // Plain loop with an inline guard rather than LINQ Where: this runs
                    // per unit on every battle broadcast, so we avoid the iterator/closure
                    // allocation.
                    foreach (var p in list)
                    {
                        if (p == null || p.destroyed || p.isHide)
                            continue;
                        arr.AddObject(o =>
                        {
                            AddLorId(o, "id", p.id);
                            o.Add("name", p.name)
                                .Add("desc", p.desc)
                                .Add("rare", p.rare.ToString())
                                .Add("isNegative", p.isNegative);
                            var passiveXml = Singleton<PassiveXmlList>.Instance?.GetData(p.id);
                            if (passiveXml != null)
                                o.Add("cost", passiveXml.cost);
                            WriteRarityColorOverrides(o, p.id?.packageId, p.rare, passiveXml);
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
                        // Same text source as the selection overlay and EmotionPassiveCardUI.SetTexts:
                        // AbnormalityCardDescXmlList keyed by the script-name string (XmlInfo.Name).
                        // Falling back to the raw script name would surface internal ids like
                        // "bigbird1" in the UI when the localized entry is missing.
                        var descList = Singleton<AbnormalityCardDescXmlList>.Instance;
                        foreach (var ab in passiveList)
                        {
                            if (ab?.XmlInfo == null)
                                continue;
                            var desc = descList?.GetAbnormalityCard(ab.XmlInfo.Name);
                            var localizedName = desc?.cardName;
                            if (string.IsNullOrEmpty(localizedName) || localizedName == "Not found")
                                localizedName = ab.XmlInfo.Name;
                            arr.AddObject(o =>
                            {
                                o.Add("id", ab.XmlInfo.id)
                                    .Add("name", localizedName)
                                    .Add("emotionLevel", ab.XmlInfo.EmotionLevel)
                                    .Add("state", ab.XmlInfo.State.ToString());
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
    }
}
