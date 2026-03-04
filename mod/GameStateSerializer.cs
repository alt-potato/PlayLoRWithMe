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

        private static string BuildBattleJson()
        {
            var w = new JsonWriter().Add("scene", "battle");

            var sc = Singleton<StageController>.Instance;
            if (sc != null)
            {
                w.Add("stageState", sc.State.ToString())
                 .Add("battleState", sc.battleState.ToString())
                 .Add("phase", sc.Phase.ToString());
            }

            return w.Build();
        }

        private static string BuildMainJson()
        {
            var w = new JsonWriter().Add("scene", "main");

            var uic = UI.UIController.Instance;
            if (uic != null)
                w.Add("uiPhase", uic.CurrentUIPhase.ToString());

            return w.Build();
        }
    }
}
