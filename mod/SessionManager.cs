using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Persistent player identity for a single browser tab (or reconnecting tab).
    /// </summary>
    internal sealed class PlayerSession
    {
        public readonly string SessionId;
        public readonly string DisplayName;

        // Units this player has claimed. Guarded by SessionManager._lock.
        public readonly HashSet<int> AssignedUnitIds = new HashSet<int>();

        public DateTime LastSeen;
        public bool IsConnected;

        // Null while disconnected.
        public WebSocketClient Client;

        // Single-shot timer started on disconnect; cancelled on reconnect.
        internal Timer ExpiryTimer;

        public PlayerSession(string sessionId, string displayName)
        {
            SessionId = sessionId;
            DisplayName = displayName;
            LastSeen = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Manages player sessions: creation, reconnection, unit claim/release, and
    /// 5-minute expiry after disconnect. Also owns the per-session broadcast
    /// helpers used by the WebSocket layer.
    /// </summary>
    internal sealed class SessionManager
    {
        // Sessions expire this long after the last disconnect if not reconnected.
        private const int ExpiryMs = 5 * 60 * 1000;

        private readonly ConcurrentDictionary<string, PlayerSession> _sessions =
            new ConcurrentDictionary<string, PlayerSession>();

        // Protects AssignedUnitIds mutation and multi-step claim/release checks.
        // Never hold this lock while calling client.Send() to avoid deadlocks.
        private readonly object _lock = new object();

        private int _playerCounter = 0;

        // -------------------------------------------------------------------------
        // Session lifecycle
        // -------------------------------------------------------------------------

        /// <summary>
        /// Returns an existing session for <paramref name="sessionToken"/> if valid,
        /// or creates a new one. Thread-safe.
        /// </summary>
        public PlayerSession GetOrCreate(string sessionToken)
        {
            if (
                !string.IsNullOrEmpty(sessionToken)
                && _sessions.TryGetValue(sessionToken, out var existing)
            )
            {
                existing.LastSeen = DateTime.UtcNow;
                return existing;
            }

            string name = "Player " + Interlocked.Increment(ref _playerCounter);
            var session = new PlayerSession(Guid.NewGuid().ToString(), name);
            _sessions[session.SessionId] = session;
            return session;
        }

        /// <summary>
        /// Attaches a live <see cref="WebSocketClient"/> to a session,
        /// cancelling any pending expiry timer.
        /// </summary>
        public void Attach(string sessionId, WebSocketClient client)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return;

            Timer timerToDispose;
            lock (_lock)
            {
                timerToDispose = session.ExpiryTimer;
                session.ExpiryTimer = null;
                session.Client = client;
                session.IsConnected = true;
                session.LastSeen = DateTime.UtcNow;
            }
            timerToDispose?.Dispose();

            // Notify everyone of the updated roster.
            BroadcastPlayerList();
        }

        /// <summary>
        /// Marks a session as disconnected and starts a 5-minute expiry countdown.
        /// Assigned units remain locked during this window to allow reconnection.
        /// </summary>
        public void Detach(string sessionId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return;

            lock (_lock)
            {
                session.IsConnected = false;
                session.Client = null;
                session.ExpiryTimer = new Timer(
                    _ => ExpireSession(sessionId),
                    null,
                    ExpiryMs,
                    Timeout.Infinite
                );
            }

            BroadcastPlayerList();
        }

        private void ExpireSession(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                session.ExpiryTimer?.Dispose();
                Debug.Log($"[PlayLoRWithMe] Session expired: {sessionId} ({session.DisplayName})");
                BroadcastPlayerList();
            }
        }

        // -------------------------------------------------------------------------
        // Unit claim / release
        // -------------------------------------------------------------------------

        /// <summary>
        /// Assigns <paramref name="unitId"/> to the session.
        /// Returns false if the unit is already claimed by another session.
        /// </summary>
        public bool ClaimUnit(string sessionId, int unitId)
        {
            string sessionUpdateJson,
                playerListJson;
            WebSocketClient clientToUpdate;

            lock (_lock)
            {
                if (!_sessions.TryGetValue(sessionId, out var session))
                    return false;

                // Refuse if any other session already owns this unit.
                foreach (var s in _sessions.Values)
                    if (s.SessionId != sessionId && s.AssignedUnitIds.Contains(unitId))
                        return false;

                session.AssignedUnitIds.Add(unitId);
                clientToUpdate = session.Client;
                sessionUpdateJson = BuildSessionUpdateJson(session);
                playerListJson = BuildPlayerListJson();
            }

            // Send outside the lock so client.Send's own sendLock can't deadlock us.
            clientToUpdate?.Send(sessionUpdateJson);
            BroadcastAll(playerListJson);
            return true;
        }

        /// <summary>
        /// Removes <paramref name="unitId"/> from the session's assignments.
        /// </summary>
        public void ReleaseUnit(string sessionId, int unitId)
        {
            string playerListJson;

            lock (_lock)
            {
                if (!_sessions.TryGetValue(sessionId, out var session))
                    return;
                session.AssignedUnitIds.Remove(unitId);
                playerListJson = BuildPlayerListJson();
            }

            BroadcastAll(playerListJson);
        }

        // -------------------------------------------------------------------------
        // Authorization
        // -------------------------------------------------------------------------

        /// <summary>
        /// Returns true when the session may act on <paramref name="unitId"/>:
        /// either the session owns it, or no session owns it (unassigned pool).
        /// </summary>
        public bool IsAuthorized(string sessionId, int unitId)
        {
            lock (_lock)
            {
                // Always allowed if no session has claimed this unit.
                bool anyClaimed = false;
                foreach (var s in _sessions.Values)
                {
                    if (s.AssignedUnitIds.Contains(unitId))
                    {
                        anyClaimed = true;
                        if (s.SessionId == sessionId)
                            return true; // owned by this session
                        // else owned by someone else — keep scanning to be sure
                    }
                }
                return !anyClaimed; // unassigned pool
            }
        }

        /// <summary>Returns all unit IDs currently claimed by any session.</summary>
        public HashSet<int> GetAllAssignedUnitIds()
        {
            var result = new HashSet<int>();
            lock (_lock)
                foreach (var s in _sessions.Values)
                foreach (int uid in s.AssignedUnitIds)
                    result.Add(uid);
            return result;
        }

        /// <summary>Returns the set of unit IDs claimed by a specific session.</summary>
        public HashSet<int> GetAssignedUnitIds(string sessionId)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                    return new HashSet<int>(session.AssignedUnitIds);
                return new HashSet<int>();
            }
        }

        // -------------------------------------------------------------------------
        // Broadcasting
        // -------------------------------------------------------------------------

        /// <summary>Sends <paramref name="json"/> to every connected WebSocket client.</summary>
        public void BroadcastAll(string json)
        {
            foreach (var session in _sessions.Values)
                if (session.IsConnected)
                    session.Client?.Send(json);
        }

        /// <summary>Returns a snapshot of all currently connected sessions.</summary>
        public IEnumerable<PlayerSession> GetConnectedSessions()
        {
            var result = new List<PlayerSession>();
            foreach (var session in _sessions.Values)
                if (session.IsConnected)
                    result.Add(session);
            return result;
        }

        // -------------------------------------------------------------------------
        // JSON builders
        // -------------------------------------------------------------------------

        /// <summary>Builds the playerList message reflecting the current session roster.</summary>
        public string BuildPlayerListJson()
        {
            // Snapshot assignments under lock so the JSON is self-consistent.
            lock (_lock)
            {
                return new JsonWriter()
                    .Add("type", "playerList")
                    .AddArray(
                        "players",
                        arr =>
                        {
                            foreach (var s in _sessions.Values)
                            {
                                arr.AddObject(obj =>
                                {
                                    obj.Add("sessionId", s.SessionId)
                                        .Add("name", s.DisplayName)
                                        .AddArray(
                                            "units",
                                            unitArr =>
                                            {
                                                foreach (int uid in s.AssignedUnitIds)
                                                    unitArr.AddInt(uid);
                                            }
                                        );
                                });
                            }
                        }
                    )
                    .Build();
            }
        }

        private string BuildSessionUpdateJson(PlayerSession session)
        {
            // Called while _lock is held by the caller.
            return new JsonWriter()
                .Add("type", "sessionUpdate")
                .AddArray(
                    "assignedUnits",
                    arr =>
                    {
                        foreach (int uid in session.AssignedUnitIds)
                            arr.AddInt(uid);
                    }
                )
                .Build();
        }

        private void BroadcastPlayerList() => BroadcastAll(BuildPlayerListJson());
    }
}
