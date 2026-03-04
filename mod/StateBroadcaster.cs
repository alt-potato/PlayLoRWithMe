using HarmonyLib;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Harmony patches and event subscriptions that broadcast a game state snapshot
    /// to all SSE clients whenever the active scene or phase changes.
    /// </summary>
    public static class StateBroadcaster
    {
        private static bool _subscribedToUIPhase = false;

        public static void Broadcast()
        {
            Server.Instance?.Broadcast(GameStateSerializer.Serialize());
        }

        // Subscribe to UIController.PhaseEnterEvent once the instance is available.
        // Called from scene-activation patches where UIController.Instance is guaranteed set.
        private static void SubscribeToUIPhaseChanges()
        {
            if (_subscribedToUIPhase)
                return;

            var uic = UI.UIController.Instance;
            if (uic == null)
                return;

            uic.PhaseEnterEvent += (UI.UIPhase _) => Broadcast();
            _subscribedToUIPhase = true;
            Debug.Log("[PlayLoRWithMe] Subscribed to UIController.PhaseEnterEvent");
        }

        // ------------------------------------------------------------------
        // Scene-switch patches
        // ------------------------------------------------------------------

        [HarmonyPatch(typeof(GameSceneManager), "ActivateTitleScene")]
        static class Patch_ActivateTitle
        {
            static void Postfix()
            {
                SubscribeToUIPhaseChanges();
                Broadcast();
            }
        }

        [HarmonyPatch(typeof(GameSceneManager), "ActivateBattleScene")]
        static class Patch_ActivateBattle
        {
            static void Postfix() => Broadcast();
        }

        [HarmonyPatch(typeof(GameSceneManager), "ActivateUIController")]
        static class Patch_ActivateUI
        {
            static void Postfix()
            {
                SubscribeToUIPhaseChanges();
                Broadcast();
            }
        }

        [HarmonyPatch(typeof(GameSceneManager), "ActivateStoryScene")]
        static class Patch_ActivateStory
        {
            static void Postfix() => Broadcast();
        }

        [HarmonyPatch(typeof(GameSceneManager), "OpenBattleSettingUI")]
        static class Patch_OpenBattleSetting
        {
            static void Postfix() => Broadcast();
        }

        // ------------------------------------------------------------------
        // Battle phase tracking — poll in OnUpdate since some phase transitions
        // bypass the property setter with direct _phase field writes, making
        // the onChangePhase delegate unreliable for full coverage.
        // ------------------------------------------------------------------

        [HarmonyPatch(typeof(StageController), "OnUpdate")]
        static class Patch_OnUpdate
        {
            static StageController.StagePhase _lastPhase = (StageController.StagePhase)(-1);

            static void Postfix(StageController __instance)
            {
                var current = __instance.Phase;
                if (current == _lastPhase)
                    return;
                _lastPhase = current;
                Broadcast();
            }
        }
    }
}
