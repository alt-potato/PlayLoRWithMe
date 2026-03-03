using UnityEngine;

namespace PlayLoRWithMe
{
    public class Initializer : ModInitializer
    {
        public static string packageId = "meconeko.playlorwithme";

        private Server _server;

        public override void OnInitializeMod()
        {
            _server = new Server();
            _server.Start();

            Application.quitting += OnQuit;
        }

        private void OnQuit()
        {
            _server?.Stop();
        }
    }
}
