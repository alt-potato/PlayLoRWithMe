using System.Collections.Generic;
using System.Linq;
using LOR_DiceSystem;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Serializes the current Library of Ruina game state to JSON.
    /// </summary>
    public static class GameStateSerializer
    {
        public static string Serialize()
        {
            try
            {
                return BuildJson();
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

        private static string BuildJson()
        {
            var gsm = GameSceneManager.Instance;
            if (gsm == null)
                return new JsonWriter().Add("scene", "loading").Build();

            if (gsm.battleScene != null && gsm.battleScene.gameObject.activeSelf)
                return BuildBattleJson();

            if (gsm.uIController != null && gsm.uIController.gameObject.activeSelf)
                return BuildMainJson();

            if (gsm.storyRoot != null && gsm.storyRoot.gameObject.activeSelf)
                return new JsonWriter().Add("scene", "story").Build();

            if (gsm.titleScene != null && gsm.titleScene.gameObject.activeSelf)
                return new JsonWriter().Add("scene", "title").Build();

            return new JsonWriter().Add("scene", "transition").Build();
        }

        private static string BuildMainJson()
        {
            var w = new JsonWriter().Add("scene", "main");
            var uic = UI.UIController.Instance;
            if (uic != null)
                w.Add("uiPhase", uic.CurrentUIPhase.ToString());
            return w.Build();
        }

        // -------------------------------------------------------------------------
        // Battle state
        // -------------------------------------------------------------------------

        private static string BuildBattleJson()
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
                            WriteUnit(arr, unit, isAlly: true);
                    }
                );
                w.AddArray(
                    "enemies",
                    arr =>
                    {
                        foreach (var unit in bom.GetList(Faction.Enemy))
                            WriteUnit(arr, unit, isAlly: false);
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

        // -------------------------------------------------------------------------
        // Unit
        // -------------------------------------------------------------------------

        private static void WriteUnit(JsonArrayWriter arr, BattleUnitModel unit, bool isAlly)
        {
            if (unit == null)
                return;
            arr.AddObject(w =>
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
                    WriteAllyCards(w, unit);
            });
        }

        // -------------------------------------------------------------------------
        // Key page
        // -------------------------------------------------------------------------

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

        // -------------------------------------------------------------------------
        // Speed dice
        // -------------------------------------------------------------------------

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

        // -------------------------------------------------------------------------
        // Slotted cards (cards assigned to speed dice before execution)
        // -------------------------------------------------------------------------

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

        // -------------------------------------------------------------------------
        // Passives
        // -------------------------------------------------------------------------

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

        // -------------------------------------------------------------------------
        // Buffs / keyword effects
        // -------------------------------------------------------------------------

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

        // -------------------------------------------------------------------------
        // Emotion / abnormality
        // -------------------------------------------------------------------------

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

        // -------------------------------------------------------------------------
        // Ally-only: hand, deck, EGO cards, team abnormality pages
        // -------------------------------------------------------------------------

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

        // -------------------------------------------------------------------------
        // Card fields shared between hand/deck/ego lists and slotted cards
        // -------------------------------------------------------------------------

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
            if (xml.DiceBehaviourList != null && xml.DiceBehaviourList.Count > 0)
                o.AddArray(
                    "dice",
                    arr =>
                    {
                        foreach (var d in xml.DiceBehaviourList)
                        {
                            arr.AddObject(die =>
                            {
                                die.Add("type", d.Type.ToString())
                                    .Add("detail", d.Detail.ToString())
                                    .Add("min", d.Min)
                                    .Add("max", d.Dice);
                                // Per-die ability text (Script → BattleCardAbilityText.xml entry)
                                var desc = abilityDescList?.GetAbilityDesc(d) ?? "";
                                if (string.IsNullOrEmpty(desc))
                                    desc = d.Desc ?? "";
                                if (!string.IsNullOrEmpty(desc))
                                    die.Add("desc", desc);
                            });
                        }
                    }
                );
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        private static void AddLorId(JsonWriter w, string key, LorId lorId)
        {
            w.AddObject(
                key,
                o => o.Add("id", lorId?.id ?? -1).Add("packageId", lorId?.packageId ?? "")
            );
        }
    }
}
