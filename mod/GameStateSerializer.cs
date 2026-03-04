using System.Text;
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
                return "{\"scene\":\"error\"}";
            }
        }

        private static string BuildJson()
        {
            var gsm = GameSceneManager.Instance;
            if (gsm == null)
                return "{\"scene\":\"loading\"}";

            var sb = new StringBuilder("{");

            if (gsm.battleScene != null && gsm.battleScene.gameObject.activeSelf)
            {
                sb.Append("\"scene\":\"battle\"");
                AppendBattleState(sb);
            }
            else if (gsm.uIController != null && gsm.uIController.gameObject.activeSelf)
            {
                sb.Append("\"scene\":\"main\"");
                var uic = UI.UIController.Instance;
                if (uic != null)
                {
                    sb.Append(",\"uiPhase\":\"");
                    sb.Append(uic.CurrentUIPhase.ToString());
                    sb.Append('"');
                }
            }
            else if (gsm.storyRoot != null && gsm.storyRoot.gameObject.activeSelf)
            {
                sb.Append("\"scene\":\"story\"");
            }
            else if (gsm.titleScene != null && gsm.titleScene.gameObject.activeSelf)
            {
                sb.Append("\"scene\":\"title\"");
            }
            else
            {
                sb.Append("\"scene\":\"transition\"");
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendBattleState(StringBuilder sb)
        {
            var sc = Singleton<StageController>.Instance;
            if (sc == null)
                return;

            sb.Append(",\"stageState\":\"");
            sb.Append(sc.State.ToString());
            sb.Append('"');

            sb.Append(",\"battleState\":\"");
            sb.Append(sc.battleState.ToString());
            sb.Append('"');

            sb.Append(",\"phase\":\"");
            sb.Append(sc.Phase.ToString());
            sb.Append('"');
        }
    }
}
