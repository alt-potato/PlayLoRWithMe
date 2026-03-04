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
            _server.Start();

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
