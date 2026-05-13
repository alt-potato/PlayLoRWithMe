using HarmonyLib;
using UnityEngine;

namespace PlayLoRWithMe
{
    public class Initializer : ModInitializer
    {
        public static string packageId = "meconeko.playlorwithme";

        private Server _server;
        private Harmony _harmony;

        public override void OnInitializeMod()
        {
            _server = new Server();
            try
            {
                _server.Start();
            }
            catch (System.Exception ex)
            {
                // A failed Start (port conflict, listener creation error) leaves
                // the mod with no HTTP/WebSocket surface. Drop the half-initialized
                // server reference and skip Harmony patching — applying patches
                // when there's no client to serve their state would only add
                // overhead and confuse OnQuit's UnpatchAll.
                Debug.LogError($"[PRWM] Server failed to start; mod disabled: {ex}");
                _server = null;
                return;
            }

            _harmony = new Harmony(packageId);
            _harmony.PatchAll();

            Application.quitting += OnQuit;
        }

        private void OnQuit()
        {
            _server?.Stop();
            _harmony?.UnpatchAll(packageId);
        }
    }
}
