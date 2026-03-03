using System.Collections.Concurrent;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Receives action JSON from the web server and queues it for execution
    /// on Unity's main thread via a Harmony patch.
    /// Stub: queue is accepted but not yet drained.
    /// </summary>
    public static class ActionInjector
    {
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();

        /// <summary>
        /// Called from the HTTP server background thread to enqueue an action.
        /// </summary>
        public static void Enqueue(string actionJson)
        {
            _queue.Enqueue(actionJson);
        }

        /// <summary>
        /// Called each frame from the Unity main thread (via Harmony patch) to drain the queue.
        /// </summary>
        public static void DrainQueue()
        {
            while (_queue.TryDequeue(out string actionJson))
            {
                // TODO: parse actionJson and execute the action in-game
            }
        }
    }
}
