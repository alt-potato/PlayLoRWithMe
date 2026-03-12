using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Receives action JSON from the web server, queues it, and executes it on
    /// Unity's main thread via a Harmony Postfix on StageController.OnUpdate.
    ///
    /// Supported actions:
    ///   {"type":"playCard",  "unitId":3, "cardIndex":0, "diceSlot":0, "targetUnitId":5, "targetDiceSlot":1}
    ///   {"type":"playCard",  "unitId":3, "cardIndex":0, "diceSlot":0}
    ///       (targetUnitId/targetDiceSlot omitted for CardRange.Instance self-buff cards)
    ///   {"type":"removeCard","unitId":3, "diceSlot":0}
    ///   {"type":"confirm"}
    /// </summary>
    public static class ActionInjector
    {
        private sealed class PendingAction
        {
            public readonly string Json;
            public bool Ok;
            public string Error;

            // Sync path (EnqueueAndWait): set when execution completes.
            public readonly ManualResetEventSlim Done;

            // Async path (EnqueueWithCallback): invoked on the Unity main thread
            // after execution so the response can be sent back via WebSocket.
            public readonly Action<bool, string> Callback;

            /// <summary>Sync constructor — used by the SSE/HTTP POST path.</summary>
            public PendingAction(string json)
            {
                Json = json;
                Done = new ManualResetEventSlim(false);
            }

            /// <summary>Async constructor — used by the WebSocket path.</summary>
            public PendingAction(string json, Action<bool, string> callback)
            {
                Json = json;
                Callback = callback;
            }
        }

        private static readonly ConcurrentQueue<PendingAction> _queue =
            new ConcurrentQueue<PendingAction>();

        /// <summary>
        /// Called from the HTTP server background thread. Blocks until the Unity main
        /// thread executes the action (or 500 ms timeout). Returns (ok, error).
        /// </summary>
        public static (bool ok, string error) EnqueueAndWait(string actionJson)
        {
            var pending = new PendingAction(actionJson);
            _queue.Enqueue(pending);
            if (!pending.Done.Wait(500))
                return (false, "Action timed out");
            return (pending.Ok, pending.Error);
        }

        /// <summary>Called each frame from the Unity main thread to execute queued actions.</summary>
        /// <summary>
        /// Called from the WebSocket receive thread. Non-blocking; returns immediately.
        /// <paramref name="onComplete"/> is invoked on the Unity main thread after
        /// execution with (ok, errorMessage), so the caller can send an actionResult
        /// response back over the WebSocket without blocking the receive loop.
        /// </summary>
        public static void EnqueueWithCallback(string actionJson, Action<bool, string> onComplete)
        {
            _queue.Enqueue(new PendingAction(actionJson, onComplete));
        }

        /// <summary>Called each frame from the Unity main thread to execute queued actions.</summary>
        public static void DrainQueue()
        {
            while (_queue.TryDequeue(out var pending))
            {
                try
                {
                    pending.Ok = Execute(pending.Json, out pending.Error);
                }
                catch (Exception ex)
                {
                    pending.Ok = false;
                    pending.Error = ex.Message;
                    Debug.LogError(
                        $"[PlayLoRWithMe] ActionInjector: {ex.Message}\n{ex.StackTrace}"
                    );
                }
                finally
                {
                    // Exactly one of Done/Callback is set depending on which path enqueued this.
                    pending.Done?.Set();
                    pending.Callback?.Invoke(pending.Ok, pending.Error);
                }
            }
        }

        // Harmony: drain the queue every frame on the Unity main thread.
        // A second Postfix on the same target is fine; Harmony chains them.
        [HarmonyPatch(typeof(StageController), "OnUpdate")]
        static class Patch_DrainQueue
        {
            static void Postfix() => DrainQueue();
        }

        // -------------------------------------------------------------------------
        // Dispatch
        // -------------------------------------------------------------------------

        private static bool Execute(string json, out string error)
        {
            error = null;
            var r = new JsonReader(json);
            string type = r.GetString("type");
            if (type == null)
            {
                error = "Missing type";
                return false;
            }

            bool ok;
            switch (type)
            {
                case "playCard":
                    ok = DoPlayCard(r, out error);
                    break;
                case "removeCard":
                    ok = DoRemoveCard(r, out error);
                    break;
                case "confirm":
                    ok = DoConfirm(out error);
                    break;
                case "selectAbnormality":
                    ok = DoSelectAbnormality(r, out error);
                    break;
                default:
                    error = $"Unknown action: '{type}'";
                    Debug.LogWarning($"[PlayLoRWithMe] {error}");
                    return false;
            }

            // Push a fresh snapshot so all clients immediately see the result.
            StateBroadcaster.Broadcast();
            return ok;
        }

        // -------------------------------------------------------------------------
        // playCard
        //
        // BattlePlayingCardSlotDetail.AddCard uses unit.cardOrder to select the slot
        // and requires a non-null target for non-Instance cards (the CanChangeAttackTarget
        // call at the bottom of AddCard will NPE otherwise).  For CardRange.Instance
        // self-buff cards the target parameter is ignored, so we pass the unit itself.
        // -------------------------------------------------------------------------

        private static bool DoPlayCard(JsonReader r, out string error)
        {
            error = null;

            if (!r.TryGetInt("unitId", out int unitId))
            {
                error = "Missing unitId";
                return false;
            }
            if (!r.TryGetInt("cardIndex", out int cardIndex))
            {
                error = "Missing cardIndex";
                return false;
            }
            if (!r.TryGetInt("diceSlot", out int diceSlot))
            {
                error = "Missing diceSlot";
                return false;
            }

            var unit = FindAlly(unitId);
            if (unit == null)
            {
                error = $"Ally unit {unitId} not found";
                Debug.LogWarning($"[PlayLoRWithMe] playCard: {error}");
                return false;
            }

            bool isEgo = r.TryGetInt("isEgo", out int isEgoInt) && isEgoInt != 0;
            var hand = isEgo ? unit.personalEgoDetail?.GetHand() : unit.allyCardDetail?.GetHand();
            if (hand == null || cardIndex < 0 || cardIndex >= hand.Count)
            {
                error = $"No card at {(isEgo ? "ego" : "hand")} index {cardIndex}";
                Debug.LogWarning($"[PlayLoRWithMe] playCard: {error}");
                return false;
            }

            var card = hand[cardIndex];

            // Resolve target.  For CardRange.Instance cards the target is ignored by AddCard,
            // so passing the unit itself is safe.  For all other ranges a real target is needed.
            BattleUnitModel target;
            int targetSlot = 0;
            if (r.TryGetInt("targetUnitId", out int targetId))
            {
                target = FindAnyUnit(targetId);
                if (target == null)
                {
                    error = $"Target unit {targetId} not found";
                    Debug.LogWarning($"[PlayLoRWithMe] playCard: {error}");
                    return false;
                }
                r.TryGetInt("targetDiceSlot", out targetSlot); // optional for Instance cards
            }
            else
            {
                // No target supplied — only valid for CardRange.Instance; fall back to self.
                target = unit;
            }

            // Set cardOrder first so CheckCardAvailable evaluates the right slot.
            int prevCardOrder = unit.cardOrder;
            unit.cardOrder = diceSlot;
            if (!unit.CheckCardAvailable(card))
            {
                unit.cardOrder = prevCardOrder;
                error = $"Card '{card.GetName()}' is not available (insufficient light or blocked)";
                Debug.LogWarning($"[PlayLoRWithMe] playCard: {error}");
                return false;
            }

            // cardOrder is already set; AddCard uses it to pick the slot.
            unit.cardSlotDetail.AddCard(card, target, targetSlot);
            SingletonBehavior<BattleManagerUI>.Instance?.ui_TargetArrow?.UpdateTargetList();
            Debug.Log(
                $"[PlayLoRWithMe] playCard: unit={unitId} card='{card.GetName()}' slot={diceSlot} target={target.id} targetSlot={targetSlot}"
            );
            return true;
        }

        // -------------------------------------------------------------------------
        // removeCard
        //
        // AddCard(null, null, 0) returns the existing card to hand and clears the slot.
        // It exits before the CanChangeAttackTarget null-dereference, so target=null is safe.
        // -------------------------------------------------------------------------

        private static bool DoRemoveCard(JsonReader r, out string error)
        {
            error = null;

            if (!r.TryGetInt("unitId", out int unitId))
            {
                error = "Missing unitId";
                return false;
            }
            if (!r.TryGetInt("diceSlot", out int diceSlot))
            {
                error = "Missing diceSlot";
                return false;
            }

            var unit = FindAlly(unitId);
            if (unit == null)
            {
                error = $"Ally unit {unitId} not found";
                return false;
            }

            unit.cardOrder = diceSlot;
            unit.cardSlotDetail.AddCard(null, null, 0);
            SingletonBehavior<BattleManagerUI>.Instance?.ui_TargetArrow?.UpdateTargetList();
            Debug.Log($"[PlayLoRWithMe] removeCard: unit={unitId} slot={diceSlot}");
            return true;
        }

        // -------------------------------------------------------------------------
        // confirm — end this player's card-selection phase
        // -------------------------------------------------------------------------

        private static bool DoConfirm(out string error)
        {
            error = null;
            var sc = Singleton<StageController>.Instance;
            if (sc == null)
                return true; // not an error; phase may have already advanced

            sc.CompleteApplyingLibrarianCardPhase(auto: false);
            Debug.Log("[PlayLoRWithMe] confirm");
            return true;
        }

        // -------------------------------------------------------------------------
        // selectAbnormality
        // -------------------------------------------------------------------------

        private static bool DoSelectAbnormality(JsonReader r, out string error)
        {
            error = null;
            if (!AbnormalitySelectionState.IsActive)
            {
                error = "No abnormality selection active";
                return false;
            }
            if (!r.TryGetInt("cardId", out int cardId))
            {
                error = "Missing cardId";
                return false;
            }

            EmotionCardXmlInfo card = null;
            var choices = AbnormalitySelectionState.Choices;
            if (choices != null)
                foreach (var c in choices)
                    if (c?.id == cardId)
                    {
                        card = c;
                        break;
                    }

            if (card == null)
            {
                error = $"Card {cardId} not in current choices";
                return false;
            }

            BattleUnitModel target = null;
            if (r.TryGetInt("targetUnitId", out int targetId))
            {
                target = FindAlly(targetId);
                if (target == null)
                {
                    error = $"Ally unit {targetId} not found";
                    return false;
                }
            }

            var floor = AbnormalitySelectionState.Floor;
            if (floor == null)
            {
                error = "Floor model unavailable";
                return false;
            }

            floor.OnPickPassiveCard(card, target);
            // Dismiss the in-game LevelUpUI so RoundEndPhase_ChoiceEmotionCard can detect
            // the selection and queue the next level's choices if the emotion level rose
            // by more than one in this act (multi-selection case).
            SingletonBehavior<BattleManagerUI>.Instance?.ui_levelup?.SetRootCanvas(false);
            Debug.Log(
                $"[PlayLoRWithMe] selectAbnormality: card={card.Name} target={target?.id.ToString() ?? "all"}"
            );
            return true;
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        private static BattleUnitModel FindAlly(int unitId)
        {
            var bom = BattleObjectManager.instance;
            if (bom == null)
                return null;
            return FindUnit(bom.GetList(Faction.Player), unitId);
        }

        private static BattleUnitModel FindAnyUnit(int unitId)
        {
            var bom = BattleObjectManager.instance;
            if (bom == null)
                return null;
            foreach (Faction f in Enum.GetValues(typeof(Faction)))
            {
                var u = FindUnit(bom.GetList(f), unitId);
                if (u != null)
                    return u;
            }
            return null;
        }

        private static BattleUnitModel FindUnit(List<BattleUnitModel> list, int unitId)
        {
            if (list == null)
                return null;
            foreach (var u in list)
                if (u.id == unitId)
                    return u;
            return null;
        }
    }
}
