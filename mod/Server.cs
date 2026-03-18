using System;
using System.IO;
using System.Net;
using System.Threading;
using LOR_DiceSystem;
using UnityEngine;

namespace PlayLoRWithMe
{
    public class Server
    {
        public const int Port = 8080;

        // DLL is in <mod root>/Assemblies/; wwwroot is a sibling of Assemblies/
        private static readonly string ModRootPath = Path.GetDirectoryName(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
        );

        internal static readonly string WwwRootPath = Path.Combine(ModRootPath, "wwwroot");

        private HttpListener _listener;
        private Thread _listenerThread;

        private readonly SessionManager _sessionManager = new SessionManager();
        private readonly DeltaEngine _deltaEngine = new DeltaEngine();

        // Last full (unfiltered) state snapshot, updated on the Unity main thread
        // by Broadcast(). Used to give new WebSocket clients an immediate initial
        // state without accessing Unity objects from the listener thread.
        private volatile bool _running = false;
        private volatile string _lastFullJson = null;

        // Set by claim/release handlers (listener thread) to request a filtered
        // broadcast from the Unity main thread on the next OnUpdate tick.
        private volatile bool _pendingBroadcast = false;

        /// <summary>
        /// When false, all players may control any librarian without claiming.
        ///
        /// Read from <c>AppData\LocalLow\Project Moon\LibraryOfRuina\ModConfigs\meconeko.playlorwithme.xml</c>;
        /// defaults to true.
        /// </summary>
        public bool ClaimsEnabled { get; private set; } = true;

        public static Server Instance { get; private set; }

        /// <summary>Exposes the session manager for read-only queries (e.g. lock lookups in serializer).</summary>
        internal SessionManager SessionManager => _sessionManager;

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        public void Start()
        {
            Instance = this;
            LoadConfig();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{Port}/");
            _listener.Start();
            _running = true;

            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "PlayLoRWithMe-HTTP",
            };
            _listenerThread.Start();

            Debug.Log($"[PlayLoRWithMe] Server listening on http://*:{Port}/");
        }

        public void Stop()
        {
            _running = false;
            _listener?.Stop();
        }

        /// <summary>
        /// Reads optional settings from the mod's XML config file.
        ///
        /// The file is at: <c>&lt;persistentDataPath&gt;/ModConfigs/meconeko.playlorwithme.xml</c>.
        /// Missing file or missing elements silently fall back to defaults.
        /// </summary>
        private void LoadConfig()
        {
            string path = System.IO.Path.Combine(
                Application.persistentDataPath,
                "ModConfigs",
                Initializer.packageId + ".xml"
            );

            Debug.Log($"[PlayLoRWithMe] Config path: {path}");

            if (!File.Exists(path))
                return;
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.Load(path);
                var root = doc.DocumentElement;

                var node = root?.SelectSingleNode("ClaimsEnabled");
                if (node != null && bool.TryParse(node.InnerText, out bool ce))
                    ClaimsEnabled = ce;

                Debug.Log($"[PlayLoRWithMe] Config loaded: claimsEnabled={ClaimsEnabled}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayLoRWithMe] Failed to read config.xml: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------------
        // Accept loop
        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleContext(ctx));
                }
                catch (HttpListenerException) when (!_running)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PlayLoRWithMe] Accept error: {ex}");
                }
            }
        }

        // -------------------------------------------------------------------------
        // Request dispatch
        // -------------------------------------------------------------------------

        private void HandleContext(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    WriteCorsHeaders(ctx.Response);
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                string path = ctx.Request.Url.AbsolutePath;
                string method = ctx.Request.HttpMethod;

                if (method == "GET" && path == "/ws")
                    HandleWebSocket(ctx); // blocks until client disconnects
                else if (method == "GET")
                    ServeStaticFile(ctx, path);
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayLoRWithMe] Handler error: {ex}");
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch { }
            }
        }

        public void BroadcastFiltered()
        {
            // All sessions receive the full unfiltered state; ownership only
            // controls interactivity on the frontend, not data visibility.
            string json = GameStateSerializer.Serialize();
            _lastFullJson = json;
            foreach (var session in _sessionManager.GetConnectedSessions())
            {
                string msg = _deltaEngine.BuildMessage(session.SessionId, json);
                if (msg != null)
                    session.Client?.Send(msg);
            }
        }

        // Tracks whether BattleSetting claim IDs have been translated to battle unit IDs
        // for the current battle. Reset each time the battle scene activates.
        private bool _claimsTranslated = false;

        /// <summary>
        /// Resets the claims-translation flag so the next battle start triggers
        /// a fresh translation. Call this when the battle scene activates.
        /// </summary>
        public void ResetClaimsTranslation() => _claimsTranslated = false;

        /// <summary>
        /// Translates each session's BattleSetting position-indices (0, 1, 2…) to
        /// the actual <c>BattleUnitModel.id</c> values now that the battle has loaded.
        /// Runs at most once per battle; no-ops until <c>BattleObjectManager</c> has units.
        /// Must be called from the Unity main thread.
        /// </summary>
        public void TryTranslateClaimsForBattle()
        {
            if (_claimsTranslated)
                return;

            var bom = BattleObjectManager.instance;
            if (bom == null)
                return;

            var allies = bom.GetList(Faction.Player);
            if (allies == null || allies.Count == 0)
                return;

            var map = new System.Collections.Generic.Dictionary<int, int>();
            for (int i = 0; i < allies.Count; i++)
                map[i] = allies[i].id;

            _sessionManager.TranslateUnitIds(map);
            _claimsTranslated = true;
            Debug.Log($"[PlayLoRWithMe] Translated {map.Count} unit claim IDs for battle.");
        }

        /// <summary>
        /// Returns true (and clears the flag) if a broadcast was requested from a
        /// background thread (e.g. after claim/release or new connection). Called
        /// from the Unity main thread so the resulting serialization is safe.
        /// </summary>
        public bool ConsumePendingBroadcast()
        {
            if (!_pendingBroadcast)
                return false;
            _pendingBroadcast = false;
            return true;
        }

        private void HandleWebSocket(HttpListenerContext ctx)
        {
            Stream stream;
            try
            {
                stream = WebSocketCodec.PerformHandshake(ctx);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayLoRWithMe] WebSocket handshake failed: {ex.Message}");
                try
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                }
                catch { }
                return;
            }

            // Look up or create a persistent session from the ?session=<token> query param.
            string sessionToken = ctx.Request.QueryString["session"];
            var session = _sessionManager.GetOrCreate(sessionToken);

            var client = new WebSocketClient(session.SessionId, stream, OnWebSocketMessage);
            _deltaEngine.AddSession(session.SessionId);
            _sessionManager.Attach(session.SessionId, client);
            Debug.Log(
                $"[PlayLoRWithMe] WebSocket connected: {session.SessionId} ({session.DisplayName})"
            );

            client.Send(BuildHelloMessage(session));

            // Send the cached last-known state as the initial snapshot. This avoids
            // accessing Unity game objects from the listener thread (not thread-safe).
            // The next Broadcast() call from the Unity main thread will follow with a
            // properly per-session filtered view; _pendingBroadcast ensures it fires
            // soon even if no game event occurs.
            // Use the cached last-broadcast JSON if available (fast, main-thread-safe).
            // Fall back to serializing immediately if the cache is cold (first ever
            // connection before any game event has fired a broadcast).
            string cachedJson = _lastFullJson ?? GameStateSerializer.Serialize();
            string initialMsg = _deltaEngine.BuildMessage(session.SessionId, cachedJson);
            if (initialMsg != null)
                client.Send(initialMsg);

            // Request a fresh filtered broadcast on the next Unity tick so any
            // ownership-based data is sent promptly even if no game event fires.
            _pendingBroadcast = true;

            // Blocks until the connection closes.
            client.ReceiveLoop();

            _sessionManager.Detach(session.SessionId);
            _deltaEngine.RemoveSession(session.SessionId);
            Debug.Log($"[PlayLoRWithMe] WebSocket disconnected: {session.SessionId}");
        }

        private void OnWebSocketMessage(WebSocketClient client, string json)
        {
            var r = new JsonReader(json);
            string type = r.GetString("type");
            if (type == null)
                return;

            string reqId = r.GetString("reqId");

            switch (type)
            {
                case "playCard":
                case "removeCard":
                case "confirm":
                case "selectAbnormality":
                    HandleWsAction(client, json, reqId);
                    break;

                case "claimUnit":
                    if (r.TryGetInt("unitId", out int claimUnitId))
                    {
                        bool claimed = _sessionManager.ClaimUnit(client.SessionId, claimUnitId);
                        if (claimed)
                            _pendingBroadcast = true;
                        if (reqId != null)
                            client.Send(
                                BuildActionResult(
                                    reqId,
                                    claimed,
                                    claimed ? null : "Unit already claimed by another player"
                                )
                            );
                    }
                    break;

                case "releaseUnit":
                    if (r.TryGetInt("unitId", out int releaseUnitId))
                    {
                        _sessionManager.ReleaseUnit(client.SessionId, releaseUnitId);
                        _pendingBroadcast = true;
                        if (reqId != null)
                            client.Send(BuildActionResult(reqId, true, null));
                    }
                    break;

                case "rename":
                    string newName = r.GetString("name");
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        _sessionManager.RenameSession(client.SessionId, newName.Trim());
                        if (reqId != null)
                            client.Send(BuildActionResult(reqId, true, null));
                    }
                    break;

                case "lockLibrarian":
                    if (
                        r.TryGetInt("floorIndex", out int lockFi)
                        && r.TryGetInt("unitIndex", out int lockUi)
                    )
                    {
                        string lockKey = lockFi + ":" + lockUi;
                        bool locked = _sessionManager.TryLockLibrarian(lockKey, client.SessionId);
                        if (locked)
                            StateBroadcaster.Broadcast();
                        if (reqId != null)
                            client.Send(
                                BuildActionResult(
                                    reqId,
                                    locked,
                                    locked ? null : "Librarian is being edited by another player"
                                )
                            );
                    }
                    break;

                case "unlockLibrarian":
                    if (
                        r.TryGetInt("floorIndex", out int ulFi)
                        && r.TryGetInt("unitIndex", out int ulUi)
                    )
                    {
                        _sessionManager.UnlockLibrarian(ulFi + ":" + ulUi, client.SessionId);
                        StateBroadcaster.Broadcast();
                        if (reqId != null)
                            client.Send(BuildActionResult(reqId, true, null));
                    }
                    break;

                case "renameLibrarian":
                    HandleRenameLibrarian(client, r, reqId);
                    break;

                case "equipKeyPage":
                    HandleEquipKeyPage(client, r, reqId);
                    break;

                case "addCardToDeck":
                    HandleAddCardToDeck(client, r, reqId);
                    break;

                case "removeCardFromDeck":
                    HandleRemoveCardFromDeck(client, r, reqId);
                    break;

                case "resync":
                    // Client detected a missed sequence number; reset delta state and
                    // send a fresh full snapshot so the client can resync cleanly.
                    _deltaEngine.RemoveSession(client.SessionId);
                    _deltaEngine.AddSession(client.SessionId);
                    string resyncMsg = _deltaEngine.BuildMessage(
                        client.SessionId,
                        GameStateSerializer.Serialize()
                    );
                    if (resyncMsg != null)
                        client.Send(resyncMsg);
                    break;
            }
        }

        // Dispatches a game action to ActionInjector. Non-blocking: enqueues the
        // action and returns immediately; the actionResult is sent back via the
        // WebSocket on the Unity main thread when DrainQueue runs.
        private void HandleWsAction(WebSocketClient client, string json, string reqId)
        {
            // Unit-targeted actions require the session to own (or have unclaimed access to)
            // the unit. confirm and selectAbnormality have no unitId and are always allowed.
            var r = new JsonReader(json);
            if (
                ClaimsEnabled
                && r.TryGetInt("unitId", out int unitId)
                && !_sessionManager.IsAuthorized(client.SessionId, unitId)
            )
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Not authorized for this unit"));
                return;
            }

            ActionInjector.EnqueueWithCallback(
                json,
                (ok, error) =>
                {
                    if (reqId != null)
                        client.Send(BuildActionResult(reqId, ok, error));
                }
            );
        }

        /// <summary>
        /// Handles renameLibrarian actions. Requires the requesting session to
        /// hold the edit lock for this librarian. Enqueues the rename on the
        /// Unity main thread via ActionInjector.
        /// </summary>
        private void HandleRenameLibrarian(WebSocketClient client, JsonReader r, string reqId)
        {
            if (!r.TryGetInt("floorIndex", out int fi) || !r.TryGetInt("unitIndex", out int ui))
                return;

            string newName = r.GetString("name");
            if (string.IsNullOrWhiteSpace(newName))
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Name cannot be empty"));
                return;
            }

            string key = fi + ":" + ui;
            if (!_sessionManager.IsLibrarianLockHolder(key, client.SessionId))
            {
                if (reqId != null)
                    client.Send(
                        BuildActionResult(reqId, false, "Not authorized — acquire lock first")
                    );
                return;
            }

            // UnitDataModel is a plain C# object and SavePlayData is file I/O —
            // both are safe to invoke directly from the server thread.
            // ActionInjector is not used here because its drain hook is a Harmony
            // postfix on StageController.OnUpdate, which only fires during battle.
            var unit = GetLibrarianUnit(fi, ui);
            if (unit == null)
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Librarian not found"));
                return;
            }

            unit.SetCustomName(newName.Trim());
            Singleton<GameSave.SaveManager>.Instance?.SavePlayData(1);
            StateBroadcaster.Broadcast();
            if (reqId != null)
                client.Send(BuildActionResult(reqId, true, null));
        }

        /// <summary>
        /// Equips a key page from the book inventory to a librarian.
        /// Requires the caller to hold the edit lock for the librarian.
        /// </summary>
        /// <summary>
        /// Equips a key page from the book inventory to a librarian.
        /// Requires the caller to hold the edit lock for the librarian.
        /// </summary>
        private void HandleEquipKeyPage(WebSocketClient client, JsonReader r, string reqId)
        {
            if (!r.TryGetInt("floorIndex", out int fi) || !r.TryGetInt("unitIndex", out int ui))
                return;

            if (!r.TryGetInt("bookInstanceId", out int bookInstanceId))
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Missing bookInstanceId"));
                return;
            }

            string key = fi + ":" + ui;
            if (!_sessionManager.IsLibrarianLockHolder(key, client.SessionId))
            {
                if (reqId != null)
                    client.Send(
                        BuildActionResult(reqId, false, "Not authorized — acquire lock first")
                    );
                return;
            }

            var unit = GetLibrarianUnit(fi, ui);
            if (unit == null)
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Librarian not found"));
                return;
            }

            var book = BookInventoryModel
                .Instance?.GetBookList_equip()
                ?.Find(b => b?.instanceId == bookInstanceId);
            if (book == null)
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Key page not found in inventory"));
                return;
            }

            // EquipBook's return value is always false (the method never returns true),
            // so we cannot use it to detect success. Pre-check the only hard-failure
            // case (book already owned by another unit) and then verify the equip
            // by inspecting unit.bookItem afterward.
            if (book.owner != null)
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Key page is already in use"));
                return;
            }

            unit.EquipBook(book);

            bool equipped = unit.bookItem?.instanceId == bookInstanceId;
            if (!equipped)
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Equip was blocked by game state"));
                return;
            }

            // SetCharacter/AssetBundle.Unload are native Unity APIs that crash off the main
            // thread. Schedule the appearance reload and panel refresh for the next
            // UIController.Update tick.
            //
            // Refresh strategy:
            //   Floor overview (UIPhase.Sephirah): unit appearance lives at slot 5+unitIndex in
            //   UICharacterRenderer. We force-reload that slot so the new key page art is picked
            //   up, then call SetLibrarianCharacterListPanel_Default to re-render thumbnails —
            //   same path the game uses on floor-tab switch. Only done when the displayed floor
            //   matches the unit's floor.
            //
            //   Librarian detail view (UIPhase.Librarian): unit appearance lives at slot 10.
            //   Force-reload that slot and call UpdatePanel to refresh the portrait and book info.
            //
            //   Without the force-reload, SetCharacterRenderer's inner SetCharacter call is a
            //   no-op (same-unit cache hit: unitModel == unit), so the old appearance persists.
            var unitRef = unit;
            var unitSephirah = unit.OwnerSephirah;
            // Slot assignment: SetCharacterRenderer(fromLeft:false) starts at index 5.
            int characterSlot = 5 + ui;
            StateBroadcaster.RunOnMainThread(() =>
            {
                var uic = UI.UIController.Instance;
                if (uic == null)
                    return;

                var renderer = SingletonBehavior<UI.UICharacterRenderer>.Instance;

                // Refresh the floor character list if the host is currently viewing
                // the same floor as the changed unit.
                if (uic.CurrentSephirah == unitSephirah)
                {
                    renderer?.SetCharacter(unitRef, characterSlot, forcelyReload: true);
                    var listPanel =
                        uic.GetUIPanel(UI.UIPanelType.CharacterList_Right)
                        as UI.UILibrarianCharacterListPanel;
                    listPanel?.SetLibrarianCharacterListPanel_Default(unitSephirah);
                }

                // Refresh the panel showing the current librarian's key page.
                // The relevant panel differs by phase — UILibrarianInfoPanel._selectedUnit
                // is only set via OnUpdatePhase and is null in Librarian_CardList, so we
                // compare UIController.CurrentUnit instead.
                if (uic.CurrentUnit == unitRef)
                {
                    if (uic.CurrentUIPhase == UI.UIPhase.Librarian)
                    {
                        // UIPhase.Librarian shows UILibrarianInfoPanel (portrait at slot 10
                        // + book/stats info). Force-reload slot 10 so the new appearance
                        // is picked up, then UpdatePanel to refresh book name/icon/stats.
                        var infoPanel =
                            uic.GetUIPanel(UI.UIPanelType.LibrarianInfo) as UI.UILibrarianInfoPanel;
                        if (infoPanel != null)
                        {
                            renderer?.SetCharacter(unitRef, 10, forcelyReload: true);
                            infoPanel.UpdatePanel();
                        }
                    }
                    else if (uic.CurrentUIPhase == UI.UIPhase.Librarian_CardList)
                    {
                        // UIPhase.Librarian_CardList shows UICardPanel with two sub-panels:
                        //   Left:  UILibrarianEquipDeckPanel — combat page deck and book header
                        //   Right: UILibrarianInfoInCardPhase — key page portrait and book info
                        //
                        // The floor-list refresh above already reloaded slot 5+ui and updated
                        // textureIndex, so both SetData calls pick up the new portrait.
                        var cardPanel = uic.GetUIPanel(UI.UIPanelType.Page) as UI.UICardPanel;
                        // Refresh right panel (key page name, icon, passives).
                        cardPanel?.LibrarianInfoPanel?.SetData(unitRef);
                        // Refresh left panel (book header + combat page deck list).
                        // SetData() reads UIController.CurrentUnit, which is already unitRef.
                        cardPanel?.EquipInfoDeckPanel?.SetData();
                    }
                }
            });

            Singleton<GameSave.SaveManager>.Instance?.SavePlayData(1);
            StateBroadcaster.Broadcast();
            if (reqId != null)
                client.Send(BuildActionResult(reqId, true, null));
        }

        /// <summary>
        /// Moves a card from the shared inventory into a librarian's deck.
        /// Requires the caller to hold the edit lock for the librarian.
        /// </summary>
        private void HandleAddCardToDeck(WebSocketClient client, JsonReader r, string reqId)
        {
            if (
                !r.TryGetInt("floorIndex", out int fi)
                || !r.TryGetInt("unitIndex", out int ui)
                || !r.TryGetInt("cardId", out int cardId)
            )
                return;

            // packageId is an empty string for vanilla cards and a workshop ID for mods.
            string packageId = r.GetString("packageId") ?? "";

            string key = fi + ":" + ui;
            if (!_sessionManager.IsLibrarianLockHolder(key, client.SessionId))
            {
                if (reqId != null)
                    client.Send(
                        BuildActionResult(reqId, false, "Not authorized — acquire lock first")
                    );
                return;
            }

            var unit = GetLibrarianUnit(fi, ui);
            if (unit == null)
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Librarian not found"));
                return;
            }

            var deck = unit.bookItem?.GetDeckAll_nocopy()?[0];
            if (deck == null)
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Deck not found"));
                return;
            }

            var lorId = string.IsNullOrEmpty(packageId)
                ? new LorId(cardId)
                : new LorId(packageId, cardId);
            var result = deck.AddCardFromInventory(lorId);
            bool ok = result == CardEquipState.Equippable;

            if (ok)
            {
                Singleton<GameSave.SaveManager>.Instance?.SavePlayData(1);
                StateBroadcaster.Broadcast();
            }

            if (reqId != null)
                client.Send(BuildActionResult(reqId, ok, ok ? null : result.ToString()));
        }

        /// <summary>
        /// Returns a card from a librarian's deck back to the shared inventory.
        /// Requires the caller to hold the edit lock for the librarian.
        /// </summary>
        private void HandleRemoveCardFromDeck(WebSocketClient client, JsonReader r, string reqId)
        {
            if (
                !r.TryGetInt("floorIndex", out int fi)
                || !r.TryGetInt("unitIndex", out int ui)
                || !r.TryGetInt("cardId", out int cardId)
            )
                return;

            string packageId = r.GetString("packageId") ?? "";

            string key = fi + ":" + ui;
            if (!_sessionManager.IsLibrarianLockHolder(key, client.SessionId))
            {
                if (reqId != null)
                    client.Send(
                        BuildActionResult(reqId, false, "Not authorized — acquire lock first")
                    );
                return;
            }

            var unit = GetLibrarianUnit(fi, ui);
            if (unit == null)
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Librarian not found"));
                return;
            }

            var deck = unit.bookItem?.GetDeckAll_nocopy()?[0];
            if (deck == null)
            {
                if (reqId != null)
                    client.Send(BuildActionResult(reqId, false, "Deck not found"));
                return;
            }

            var lorId = string.IsNullOrEmpty(packageId)
                ? new LorId(cardId)
                : new LorId(packageId, cardId);
            bool removed = deck.MoveCardToInventory(lorId);

            if (removed)
            {
                Singleton<GameSave.SaveManager>.Instance?.SavePlayData(1);
                StateBroadcaster.Broadcast();
            }

            if (reqId != null)
                client.Send(
                    BuildActionResult(reqId, removed, removed ? null : "Card not found in deck")
                );
        }

        /// <summary>
        /// Resolves a floor/unit index pair to the corresponding UnitDataModel,
        /// or null if the floor is not open or the index is out of range.
        /// Internal so ActionInjector can reuse it for the rename action.
        /// </summary>
        internal static UnitDataModel GetLibrarianUnit(int floorIndex, int unitIndex)
        {
            var sephirahs = new[]
            {
                SephirahType.Malkuth,
                SephirahType.Yesod,
                SephirahType.Hod,
                SephirahType.Netzach,
                SephirahType.Tiphereth,
                SephirahType.Gebura,
                SephirahType.Chesed,
                SephirahType.Binah,
                SephirahType.Hokma,
                SephirahType.Keter,
            };

            if (floorIndex < 0 || floorIndex >= sephirahs.Length)
                return null;

            var lib = LibraryModel.Instance;
            if (lib == null)
                return null;

            var floor = lib.GetFloor(sephirahs[floorIndex]);
            if (floor == null)
                return null;

            var units = floor.GetUnitDataList();
            if (units == null || unitIndex < 0 || unitIndex >= units.Count)
                return null;

            return units[unitIndex];
        }

        private string BuildHelloMessage(PlayerSession session)
        {
            return new JsonWriter()
                .Add("type", "hello")
                .Add("sessionId", session.SessionId)
                .Add("claimsEnabled", ClaimsEnabled)
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

        // Wraps the raw game-state JSON object in the {type, seq, data} envelope
        // expected by the frontend. seq=0 is used for targeted sends (hello, resync)
        // where the client should not validate sequence continuity.
        private static string WrapStateMessage(string stateJson, int seq) =>
            "{\"type\":\"state\",\"seq\":" + seq + ",\"data\":" + stateJson + "}";

        private static string BuildActionResult(string reqId, bool ok, string error)
        {
            var w = new JsonWriter().Add("type", "actionResult").Add("reqId", reqId).Add("ok", ok);
            if (!ok && error != null)
                w.Add("error", error);
            return w.Build();
        }

        // -------------------------------------------------------------------------
        // HTTP helpers
        // -------------------------------------------------------------------------

        private static void ServeStaticFile(HttpListenerContext ctx, string urlPath)
        {
            string relative = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(relative))
                relative = "index.html";

            string filePath = Path.GetFullPath(Path.Combine(WwwRootPath, relative));

            if (!filePath.StartsWith(WwwRootPath, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                return;
            }

            if (!File.Exists(filePath))
                filePath = Path.Combine(WwwRootPath, "index.html");

            if (!File.Exists(filePath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            ctx.Response.ContentType = MimeType(Path.GetExtension(filePath));
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static string MimeType(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".html":
                    return "text/html; charset=utf-8";
                case ".js":
                case ".mjs":
                    return "application/javascript; charset=utf-8";
                case ".css":
                    return "text/css; charset=utf-8";
                case ".json":
                    return "application/json; charset=utf-8";
                case ".ico":
                    return "image/x-icon";
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".svg":
                    return "image/svg+xml";
                case ".woff":
                    return "font/woff";
                case ".woff2":
                    return "font/woff2";
                default:
                    return "application/octet-stream";
            }
        }

        private static void WriteCorsHeaders(HttpListenerResponse response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        }
    }
}
