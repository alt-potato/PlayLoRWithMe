using System.Collections.Generic;
using System.Reflection;
using LOR_DiceSystem;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Serializes the current Library of Ruina game state to JSON.
    /// This file holds the shared plumbing -- scene dispatch, the theme block, and
    /// the card/dice/rarity writers used by every scene; the scene-specific writers
    /// live in the GameStateSerializer.*.cs partials.
    /// </summary>
    public static partial class GameStateSerializer
    {
        /// <summary>
        /// Marker id used by the game to tag an empty succession (attribution) slot
        /// on a key page's passive list. Passives with this originpassive.id are
        /// placeholders the player can fill via passive attribution.
        /// </summary>
        public const int EmptyAttributionPassiveId = 9999999;

        /// <summary>
        /// Cached reflection lookup for the private <c>LibrariansNameXmlList._dictionary</c>
        /// field, used to read the suggested-name pool without a public API.
        /// </summary>
        private static readonly FieldInfo _libNameDictField =
            typeof(LibrariansNameXmlList).GetField(
                "_dictionary",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        /// <summary>
        /// Cached reflection lookup for the private <c>BookModel._activatedAllPassives</c>
        /// field, read per book per main-scene broadcast to surface attributed
        /// (succession-received) passives. Resolved once to avoid a per-book GetField.
        /// </summary>
        private static readonly FieldInfo _activatedAllPassivesField =
            typeof(BookModel).GetField(
                "_activatedAllPassives",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        /// <summary>
        /// The 10 named Sephirah floors in canonical order; index is the floorIndex
        /// used throughout the JSON API and WebSocket messages.
        /// </summary>
        internal static readonly SephirahType[] Sephirahs = new[]
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

        /// <summary>
        /// Serializes the full game state and is the baseline for delta diffing.
        /// All sessions receive the same unfiltered state; ownership only controls
        /// interactivity on the frontend, not data visibility.
        /// </summary>
        public static string Serialize() => BuildJsonSafe();

        private static string BuildJsonSafe()
        {
            try
            {
                return BuildJson();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PRWM] GameStateSerializer error: {ex}");
                return new JsonWriter().Add("scene", "error").Build();
            }
        }

        // -------------------------------------------------------------------------
        // Scene dispatch
        // -------------------------------------------------------------------------

        /// <summary>
        /// Builds a JSON object representing the current game state.
        /// </summary>
        private static string BuildJson()
        {
            var gsm = GameSceneManager.Instance;
            if (gsm == null)
                return new JsonWriter().Add("scene", "loading").Build();

            var w = new JsonWriter();
            w.Add("assetsReady", AppearanceCache.IsReady && GiftCache.IsReady);
            // emit theme on state too so clients that connected before
            // ThemeProbe.IsReady pick up the colours via the next push
            // (DeltaEngine drops the block when unchanged between pushes).
            WriteTheme(w);

            if (gsm.battleScene != null && gsm.battleScene.gameObject.activeSelf)
            {
                w.Add("scene", "battle");
                WriteBattleScene(w);
            }
            else if (gsm.uIController != null && gsm.uIController.gameObject.activeSelf)
            {
                w.Add("scene", "main");
                WriteMainScene(w);
            }
            else if (gsm.storyRoot != null && gsm.storyRoot.gameObject.activeSelf)
            {
                w.Add("scene", "story");
                WriteStoryScene(w);
            }
            else if (gsm.titleScene != null && gsm.titleScene.gameObject.activeSelf)
            {
                w.Add("scene", "title");
                WriteTitleScene(w);
            }
            else
            {
                w.Add("scene", "transition");
            }

            return w.Build();
        }

        /// <summary>
        /// Serializes the title screen. Currently a no-op placeholder; the scene
        /// tag is already written by the dispatcher.
        /// </summary>
        private static void WriteTitleScene(JsonWriter w) { }

        /// <summary>
        /// Serializes the story/cutscene scene. Currently a no-op placeholder;
        /// the scene tag is already written by the dispatcher.
        /// </summary>
        private static void WriteStoryScene(JsonWriter w) { }

        /// <summary>
        /// Emits the one-shot <c>theme</c> block on a hello (or, on the late-probe
        /// retry path, on the next state push) so the frontend can match vanilla
        /// LoR's per-faction speed-die fill colours. Omits the block entirely
        /// when the probe has not yet bound both colours.
        /// </summary>
        public static void WriteTheme(JsonWriter w)
        {
            if (!ThemeProbe.IsReady)
                return;
            w.AddObject(
                "theme",
                t =>
                    t.AddObject(
                        "factionDieColors",
                        c =>
                            c.Add("ally", ThemeProbe.AllyDieColor)
                                .Add("enemy", ThemeProbe.EnemyDieColor)
                    )
            );
        }

        /// <summary>
        /// Writes a JSON object representing a list of cards associated with a given key.
        ///
        /// If a user is included, the availability of each card is also included, ie. whether it is not disabled.
        /// It does not check light cost.
        /// </summary>
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
        private static void WriteCardFields(JsonWriter o, BattleDiceCardModel card, bool maskDie = false)
        {
            var xml = card.XmlData;
            var abilityDescList = Singleton<BattleCardAbilityDescXmlList>.Instance;

            o.Add("rarity", card.GetRarity().ToString())
                .Add("emotionLimit", card.GetSpec().emotionLimit)
                .Add("baseCost", card.GetSpec().Cost);
            // emit on hand / deck / EGO / slotted card surfaces in one place; vanilla rarities
            // skip the probe and emit no override fields, preserving the pre-change wire shape.
            // Pass xml as the wrapper-hint — for rarity-changed cards (e.g. Black Silence
            // retagged by BlackSilence.CardChange) the wrapper's RarityPackageId is the
            // authoritative bucket key for the colour lookup.
            WriteRarityColorOverrides(o, card.GetID()?.packageId, card.GetRarity(), xml);

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
            WriteDiceBehaviours(o, xml.DiceBehaviourList, abilityDescList, maskDie);
        }

        /// <summary>
        /// Writes a "dice" array for a card's DiceBehaviourList.
        /// Skips the array entirely when the list is null or empty.
        /// </summary>
        /// <param name="maskDie">When true, emit the literal `"???"` for each die's
        /// `desc` instead of the real description string. Mirrors the in-game
        /// `BattleDiceCard_BehaviourDescUI.SetBehaviourInfo` `isHide` branch, which
        /// the base game activates for enemy-owned card previews while
        /// `StageController.IsHideEnemyDiceAbilityInfo()` is true (Crying Children's
        /// Page encounter, passive `PassiveAbility_240328`).</param>
        private static void WriteDiceBehaviours(
            JsonWriter o,
            IEnumerable<DiceBehaviour> behaviours,
            BattleCardAbilityDescXmlList abilityDescList,
            bool maskDie = false
        )
        {
            if (behaviours == null)
                return;
            var list = behaviours as IList<DiceBehaviour> ?? new List<DiceBehaviour>(behaviours);
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
                            if (maskDie)
                            {
                                die.Add("desc", "???");
                                return;
                            }
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
        /// Emits the four optional <c>rarity*Color</c> hex-string fields when the rarity
        /// is custom (past <see cref="Rarity.Special"/>) AND CustomRarityUtil resolves a
        /// matching entry. Vanilla rarities and missing probe results emit nothing, keeping
        /// the wire format byte-identical to the pre-change shape for the common case.
        /// <para/>
        /// <paramref name="xmlForHint"/> is the underlying XML data object (DiceCardXmlInfo
        /// / BookXmlInfo / PassiveXmlInfo). When the runtime type is a CustomRarityUtil
        /// wrapper its <c>RarityPackageId</c> field is the authoritative lookup key,
        /// especially for rarity-change mods that retag vanilla items under a different
        /// mod's rarity registration. Pass <c>null</c> when the XML object is not handy;
        /// the probe falls back to <paramref name="packageId"/> and a global walk.
        /// </summary>
        private static void WriteRarityColorOverrides(
            JsonWriter o,
            string packageId,
            Rarity rarity,
            object xmlForHint = null)
        {
            // skip the probe for vanilla rarities (Common..Special, enum values 0..4).
            // custom rarities live at (Rarity)(4 + _RarityID), so they're 5+.
            if ((int)rarity <= (int)Rarity.Special)
                return;
            var ovr = CustomRarityProbe.TryGet(packageId ?? "", rarity, xmlForHint);
            if (ovr == null)
                return;
            o.Add("rarityColor", CustomRarityProbe.RarityOverride.ToHex(ovr.Frame))
                .Add("rarityRangeIconColor", CustomRarityProbe.RarityOverride.ToHex(ovr.RangeIcon))
                .Add("rarityAbilityColor", CustomRarityProbe.RarityOverride.ToHex(ovr.AbilityDesc))
                .Add(
                    "rarityKeywordColor",
                    CustomRarityProbe.RarityOverride.ToHex(ovr.AbilityKeyword)
                );
        }
    }
}
