using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
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
        // Mutated from the main thread, read by background-thread Broadcast()
        // callers indirectly via SubscribeToUIPhaseChanges. volatile gives a
        // reliable happens-before on the flip without introducing a lock.
        private static volatile bool _subscribedToUIPhase = false;

        // Captured the first time a known-main-thread hook (UIController.Update or
        // StageController.OnUpdate postfix) runs. Read by Broadcast() on background
        // threads to decide whether a caller needs to be marshalled onto the main
        // thread, so it must be volatile for a timely cross-thread read.
        private static volatile int _mainThreadId = -1;

        /// <summary>
        /// Broadcasts the current game state. Safe to call from any thread:
        /// if invoked from a non-main thread, the broadcast is deferred to the
        /// next main-thread tick via <see cref="RunOnMainThread"/>. This is
        /// critical because <c>GameStateSerializer</c> enumerates Unity model
        /// collections (e.g. <c>BookModel.CreatePassiveList</c>) that are
        /// mutated on the main thread — enumerating them from a background
        /// thread can throw <c>InvalidOperationException</c>.
        /// </summary>
        public static void Broadcast()
        {
            if (_mainThreadId == -1 || Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                Server.Instance?.BroadcastFiltered();
            }
            else
            {
                RunOnMainThread(() => Server.Instance?.BroadcastFiltered());
            }
        }

        /// <summary>
        /// Clears both emotion-card selection states (abnormality and EGO branches).
        /// Called on scene transitions that leave or re-enter a battle so a selection
        /// abandoned without a pick cannot survive into another scene or reception —
        /// the per-pick clear in <c>OnPickPassiveCard</c>/<c>OnPickEgoCard</c> only
        /// runs when the player actually commits, so exiting to title mid-selection
        /// would otherwise leave <c>IsActive</c> true with a dangling <c>Floor</c>.
        /// </summary>
        internal static void ResetEmotionSelectionState()
        {
            AbnormalitySelectionState.IsActive = false;
            AbnormalitySelectionState.Choices = null;
            AbnormalitySelectionState.Floor = null;

            EgoSelectionState.IsActive = false;
            EgoSelectionState.Choices = null;
            EgoSelectionState.Floor = null;
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

            uic.PhaseEnterEvent += _ => Broadcast();
            _subscribedToUIPhase = true;
            Debug.Log("[PRWM] Subscribed to UIController.PhaseEnterEvent");
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
                // Drop any emotion-card selection abandoned by a prior battle so it
                // does not leak onto the title-scene snapshot or the next reception.
                ResetEmotionSelectionState();
                Broadcast();
            }
        }

        [HarmonyPatch(typeof(GameSceneManager), "ActivateBattleScene")]
        static class Patch_ActivateBattle
        {
            static void Postfix()
            {
                // Reset so TryTranslateClaimsForBattle runs once for this battle.
                Server.Instance?.ResetClaimsTranslation();
                // Start every reception with a clean selection state regardless of how
                // the previous one ended (LevelUpUI.Init runs mid-battle, well after
                // this activation, so this never clobbers a legitimately-open selection).
                ResetEmotionSelectionState();
                // Retry the theme probe — the SpeedDiceUI prefab may not have
                // been loaded yet when ActivateUIController fired.
                ThemeProbe.TryProbe();
                Broadcast();
            }
        }

        [HarmonyPatch(typeof(GameSceneManager), "ActivateUIController")]
        static class Patch_ActivateUI
        {
            static void Postfix()
            {
                SubscribeToUIPhaseChanges();
                // Extract customization sprites once the main scene is loaded and
                // CustomizingResourceLoader's singleton is available.
                AppearanceCache.EnsureExtracted();
                GiftCache.EnsureExtracted();
                // Best-effort sample of vanilla speed-die colours; may need to
                // retry from ActivateBattleScene if no prefab is loaded yet.
                ThemeProbe.TryProbe();
                // The library/management scene is reached on a normal battle exit;
                // clear any selection abandoned without a pick so it does not persist.
                ResetEmotionSelectionState();
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

        // Fires whenever the player toggles a librarian slot in the BattleSetting screen.
        // SelectedToggles calls SetYesToggleState / SetNoToggleState, which write
        // IsAddedBattle on the UnitBattleDataModel, so we broadcast after it returns to
        // reflect the updated selection.
        [HarmonyPatch(typeof(UI.UIBattleSettingPanel), "SelectedToggles")]
        static class Patch_SelectedToggles
        {
            static void Postfix() => Broadcast();
        }

        // Fires whenever the player switches to a different floor (Sephirah) in the
        // BattleSetting screen. SetNextSephirah updates StageController.CurrentSephirah,
        // so the serializer will pick up the new floor's unit list on the next broadcast.
        [HarmonyPatch(typeof(UI.UIBattleSettingPanel), "SetNextSephirah")]
        static class Patch_SetNextSephirah
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
                bool phaseChanged = current != _lastPhase;
                if (phaseChanged)
                    _lastPhase = current;

                if (_mainThreadId == -1)
                    _mainThreadId = Thread.CurrentThread.ManagedThreadId;

                // drain queued background-thread actions here too, since
                // UIController.Update does not tick during battle scenes.
                DrainMainThreadQueue();

                if (phaseChanged || Server.Instance?.ConsumePendingBroadcast() == true)
                {
                    // Translate BattleSetting position-indices to battle unit IDs on the
                    // first tick where BattleObjectManager has units loaded.
                    Server.Instance?.TryTranslateClaimsForBattle();
                    Broadcast();
                }
            }
        }

        // ------------------------------------------------------------------
        // Card slot changes — broadcast whenever AddCard is called, whether
        // triggered by the in-game UI or our own ActionInjector.
        // ------------------------------------------------------------------

        [HarmonyPatch(typeof(BattlePlayingCardSlotDetail), "AddCard")]
        static class Patch_AddCard
        {
            static void Postfix() => Broadcast();
        }

        // ------------------------------------------------------------------
        // Abnormality page selection
        // ------------------------------------------------------------------

        // Fires when the level-up UI opens with abnormality card choices.
        [HarmonyPatch(typeof(LevelUpUI), "Init")]
        static class Patch_LevelUpInit
        {
            static void Prefix(List<EmotionCardXmlInfo> cardList)
            {
                AbnormalitySelectionState.IsActive = true;
                AbnormalitySelectionState.Choices = cardList;
                AbnormalitySelectionState.Floor =
                    Singleton<StageController>.Instance?.GetCurrentStageFloorModel();
                Broadcast();
            }
        }

        // Fires when any selection is made (in-game UI or our action injector).
        [HarmonyPatch(typeof(StageLibraryFloorModel), "OnPickPassiveCard")]
        static class Patch_OnPickPassiveCard
        {
            static void Postfix()
            {
                AbnormalitySelectionState.IsActive = false;
                AbnormalitySelectionState.Choices = null;
                AbnormalitySelectionState.Floor = null;
                Broadcast();
            }
        }

        // ------------------------------------------------------------------
        // EGO page selection — mirror of the abnormality patches above.
        //
        // RoundEndPhase_ChoiceEmotionCard dispatches to LevelUpUI.InitEgo when
        // team.egoSelectionPoint > 0 (and the abnormality skill points are
        // depleted). OnPickEgoCard fires for both in-game clicks and the mod's
        // own selectEgo action handler, which calls floor.OnPickEgoCard(...).
        // ------------------------------------------------------------------

        [HarmonyPatch(typeof(LevelUpUI), "InitEgo")]
        static class Patch_LevelUpInitEgo
        {
            static void Prefix(List<EmotionEgoXmlInfo> egoList)
            {
                EgoSelectionState.IsActive = true;
                EgoSelectionState.Choices = egoList;
                EgoSelectionState.Floor =
                    Singleton<StageController>.Instance?.GetCurrentStageFloorModel();
                Broadcast();
            }
        }

        [HarmonyPatch(typeof(StageLibraryFloorModel), "OnPickEgoCard")]
        static class Patch_OnPickEgoCard
        {
            static void Postfix()
            {
                EgoSelectionState.IsActive = false;
                EgoSelectionState.Choices = null;
                EgoSelectionState.Floor = null;
                Broadcast();
            }
        }

        // ------------------------------------------------------------------
        // Main-thread dispatcher — for Unity APIs that must run on the main
        // thread but are triggered from background threads (e.g. WebSocket).
        // ------------------------------------------------------------------

        private static readonly ConcurrentQueue<Action> _mainThreadQueue =
            new ConcurrentQueue<Action>();

        /// <summary>
        /// Schedules <paramref name="action"/> to run on the Unity main thread
        /// on the next <c>UIController.Update</c> or <c>StageController.OnUpdate</c> tick.
        /// </summary>
        public static void RunOnMainThread(Action action) => _mainThreadQueue.Enqueue(action);

        private static void DrainMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[PRWM] MainThread action failed: {ex.Message}\n{ex.StackTrace}"
                    );
                }
            }
        }

        // Drain the queue every frame while the main scene (UIController) is active.
        // Patch_OnUpdate above drains it during battle scenes.
        [HarmonyPatch(typeof(UI.UIController), "Update")]
        static class Patch_UIControllerUpdate
        {
            static void Postfix()
            {
                if (_mainThreadId == -1)
                    _mainThreadId = Thread.CurrentThread.ManagedThreadId;

                DrainMainThreadQueue();
            }
        }
    }

    internal static class AbnormalitySelectionState
    {
        public static bool IsActive;
        public static List<EmotionCardXmlInfo> Choices;
        public static StageLibraryFloorModel Floor;
    }

    /// <summary>
    /// Mirror of <see cref="AbnormalitySelectionState"/> for the EGO-card branch of
    /// the level-up UI (opened via <c>LevelUpUI.InitEgo</c>). Mutually exclusive with
    /// the abnormality branch at runtime — <c>StartPickEmotionCard</c> only opens
    /// one at a time — so the two states never both go IsActive in the same tick.
    /// </summary>
    internal static class EgoSelectionState
    {
        public static bool IsActive;
        public static List<EmotionEgoXmlInfo> Choices;
        public static StageLibraryFloorModel Floor;
    }
}
