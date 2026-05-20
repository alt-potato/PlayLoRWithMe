using System;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Unity-free logging facade for the transport/protocol layer. Routes log
    /// messages through swappable delegates so the same source files can compile
    /// and run both inside the game (where <see cref="Initializer"/> wires the
    /// delegates to <c>UnityEngine.Debug</c>) and in a headless test process
    /// (where the no-op defaults touch no Unity runtime).
    /// </summary>
    internal static class ModLog
    {
        // Defaults are no-ops so files compiled into the test assembly never
        // reach UnityEngine. Initializer overrides these at startup with the
        // real Debug.Log / Debug.LogWarning bindings.
        public static Action<string> Info = _ => { };
        public static Action<string> Warn = _ => { };
    }
}
