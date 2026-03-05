using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();

        /// <summary>Called from the HTTP server background thread to enqueue an action.</summary>
        public static void Enqueue(string actionJson) => _queue.Enqueue(actionJson);

        /// <summary>Called each frame from the Unity main thread to execute queued actions.</summary>
        public static void DrainQueue()
        {
            while (_queue.TryDequeue(out string json))
            {
                try
                {
                    Execute(json);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PlayLoRWithMe] ActionInjector: {ex.Message}\n{ex.StackTrace}");
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

        private static void Execute(string json)
        {
            var r = new JsonReader(json);
            string type = r.GetString("type");
            if (type == null) return;

            switch (type)
            {
                case "playCard":   DoPlayCard(r);   break;
                case "removeCard": DoRemoveCard(r); break;
                case "confirm":    DoConfirm();     break;
                default:
                    Debug.LogWarning($"[PlayLoRWithMe] Unknown action type: '{type}'");
                    break;
            }

            // Push a fresh snapshot so all clients immediately see the result.
            StateBroadcaster.Broadcast();
        }

        // -------------------------------------------------------------------------
        // playCard
        //
        // BattlePlayingCardSlotDetail.AddCard uses unit.cardOrder to select the slot
        // and requires a non-null target for non-Instance cards (the CanChangeAttackTarget
        // call at the bottom of AddCard will NPE otherwise).  For CardRange.Instance
        // self-buff cards the target parameter is ignored, so we pass the unit itself.
        // -------------------------------------------------------------------------

        private static void DoPlayCard(JsonReader r)
        {
            if (!r.TryGetInt("unitId",    out int unitId))    return;
            if (!r.TryGetInt("cardIndex", out int cardIndex)) return;
            if (!r.TryGetInt("diceSlot",  out int diceSlot))  return;

            var unit = FindAlly(unitId);
            if (unit == null)
            {
                Debug.LogWarning($"[PlayLoRWithMe] playCard: ally unit {unitId} not found");
                return;
            }

            var hand = unit.allyCardDetail?.GetHand();
            if (hand == null || cardIndex < 0 || cardIndex >= hand.Count)
            {
                Debug.LogWarning($"[PlayLoRWithMe] playCard: no card at hand index {cardIndex}");
                return;
            }

            var card = hand[cardIndex];

            // Resolve target.  For CardRange.Instance cards the target is ignored by AddCard,
            // so passing the unit itself is safe.  For all other ranges a real target is needed.
            BattleUnitModel target;
            int targetSlot;
            if (r.TryGetInt("targetUnitId", out int targetId) && r.TryGetInt("targetDiceSlot", out targetSlot))
            {
                target = FindAnyUnit(targetId);
                if (target == null)
                {
                    Debug.LogWarning($"[PlayLoRWithMe] playCard: target unit {targetId} not found");
                    return;
                }
            }
            else
            {
                // No target supplied — only valid for CardRange.Instance; fall back to self.
                target = unit;
                targetSlot = 0;
            }

            unit.cardOrder = diceSlot;
            unit.cardSlotDetail.AddCard(card, target, targetSlot);
            SingletonBehavior<BattleManagerUI>.Instance?.ui_TargetArrow?.UpdateTargetList();
            Debug.Log($"[PlayLoRWithMe] playCard: unit={unitId} card='{card.GetName()}' slot={diceSlot} target={target.id} targetSlot={targetSlot}");
        }

        // -------------------------------------------------------------------------
        // removeCard
        //
        // AddCard(null, null, 0) returns the existing card to hand and clears the slot.
        // It exits before the CanChangeAttackTarget null-dereference, so target=null is safe.
        // -------------------------------------------------------------------------

        private static void DoRemoveCard(JsonReader r)
        {
            if (!r.TryGetInt("unitId",   out int unitId))   return;
            if (!r.TryGetInt("diceSlot", out int diceSlot)) return;

            var unit = FindAlly(unitId);
            if (unit == null) return;

            unit.cardOrder = diceSlot;
            unit.cardSlotDetail.AddCard(null, null, 0);
            SingletonBehavior<BattleManagerUI>.Instance?.ui_TargetArrow?.UpdateTargetList();
            Debug.Log($"[PlayLoRWithMe] removeCard: unit={unitId} slot={diceSlot}");
        }

        // -------------------------------------------------------------------------
        // confirm — end this player's card-selection phase
        // -------------------------------------------------------------------------

        private static void DoConfirm()
        {
            var sc = Singleton<StageController>.Instance;
            if (sc == null) return;

            sc.CompleteApplyingLibrarianCardPhase(auto: false);
            Debug.Log("[PlayLoRWithMe] confirm");
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        private static BattleUnitModel FindAlly(int unitId)
        {
            var bom = BattleObjectManager.instance;
            if (bom == null) return null;
            return FindUnit(bom.GetList(Faction.Player), unitId);
        }

        private static BattleUnitModel FindAnyUnit(int unitId)
        {
            var bom = BattleObjectManager.instance;
            if (bom == null) return null;
            foreach (Faction f in Enum.GetValues(typeof(Faction)))
            {
                var u = FindUnit(bom.GetList(f), unitId);
                if (u != null) return u;
            }
            return null;
        }

        private static BattleUnitModel FindUnit(List<BattleUnitModel> list, int unitId)
        {
            if (list == null) return null;
            foreach (var u in list)
                if (u.id == unitId) return u;
            return null;
        }
    }
}
