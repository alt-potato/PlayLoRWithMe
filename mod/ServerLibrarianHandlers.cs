using System;
using System.Collections.Generic;
using LOR_DiceSystem;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Librarian-management WebSocket handlers for <see cref="Server"/>: rename,
    /// key-page equip, deck editing, passive attribution, and appearance/gift
    /// customization. Split out from the transport/routing core in Server.cs;
    /// both halves compose into the same partial class.
    /// </summary>
    public partial class Server
    {
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
                SendResult(client, reqId, false, "Name cannot be empty");
                return;
            }

            string key = LockKey(fi, ui);
            if (!_sessionManager.IsLibrarianLockHolder(key, client.SessionId))
            {
                SendResult(client, reqId, false, "Not authorized — acquire lock first");
                return;
            }

            // UnitDataModel is a plain C# object and SavePlayData is file I/O —
            // both are safe to invoke directly from the server thread.
            // ActionInjector is not used here because its drain hook is a Harmony
            // postfix on StageController.OnUpdate, which only fires during battle.
            var unit = GetLibrarianUnit(fi, ui);
            if (unit == null)
            {
                SendResult(client, reqId, false, "Librarian not found");
                return;
            }

            // Sephirah (patron) librarians cannot be renamed — their names
            // come from CharactersNameXmlList and are fixed in the base game.
            if (unit.isSephirah)
            {
                SendResult(client, reqId, false, "Patron librarians cannot be renamed");
                return;
            }

            unit.SetCustomName(newName.Trim());
            SaveAndBroadcast();
            SendResult(client, reqId, true, null);
        }

        /// <summary>
        /// Equips a key page from the book inventory to a librarian.
        /// Requires the caller to hold the edit lock for the librarian.
        /// </summary>
        private void HandleEquipKeyPage(WebSocketClient client, JsonReader r, string reqId)
        {
            var unit = ValidateLibrarianEdit(client, r, reqId, out int fi, out int ui);
            if (unit == null)
                return;

            if (!r.TryGetInt("bookInstanceId", out int bookInstanceId))
            {
                SendResult(client, reqId, false, "Missing bookInstanceId");
                return;
            }

            var book = FindEquippedBook(bookInstanceId);
            if (book == null)
            {
                SendResult(client, reqId, false, "Key page not found in inventory");
                return;
            }

            // EquipBook's return value is always false (the method never returns true),
            // so we cannot use it to detect success. Pre-check the only hard-failure
            // case (book already owned by another unit) and then verify the equip
            // by inspecting unit.bookItem afterward.
            if (book.owner != null)
            {
                SendResult(client, reqId, false, "Key page is already in use");
                return;
            }

            unit.EquipBook(book);

            bool equipped = unit.bookItem?.instanceId == bookInstanceId;
            if (!equipped)
            {
                SendResult(client, reqId, false, "Equip was blocked by game state");
                return;
            }

            // Key page changes affect the card inventory, so do a full card panel refresh.
            RefreshCharacterRenderer(
                unit,
                unit.OwnerSephirah,
                FloorListSlotBase + ui,
                refreshCardInventory: true
            );

            SaveAndBroadcast();
            SendResult(client, reqId, true, null);
        }

        /// <summary>
        /// Returns a librarian to their immutable base (origin) key page.
        /// The engine implements this as <c>UnitDataModel.EquipBook(null)</c>,
        /// which causes the <c>bookItem</c> getter to fall back to <c>defaultBook</c>.
        /// Requires the caller to hold the edit lock for the librarian.
        /// </summary>
        private void HandleUnequipKeyPage(WebSocketClient client, JsonReader r, string reqId)
        {
            var unit = ValidateLibrarianEdit(client, r, reqId, out int fi, out int ui);
            if (unit == null)
                return;

            var baseBook = unit.defaultBook;
            if (baseBook == null)
            {
                // every librarian is constructed with a defaultBook in UnitDataModel.Init,
                // so this should be unreachable — guard defensively rather than crash.
                SendResult(client, reqId, false, "Librarian has no base key page");
                return;
            }

            // already on base: bookItem getter returns _defaultBook when _bookItem is null.
            // treat as a successful no-op so retries / double-clicks don't churn state.
            if (unit.bookItem == baseBook)
            {
                SendResult(client, reqId, true, null);
                return;
            }

            unit.EquipBook(null);

            // EquipBook honors IsChangeItemLock() variants (Binah / Black Silence / Gebura),
            // any of which can substitute a non-null book or refuse the call entirely.
            // Verify the post-condition by identity instead of trusting the return value
            // (which is always false — same caveat as HandleEquipKeyPage).
            if (unit.bookItem != baseBook)
            {
                SendResult(client, reqId, false, "Unequip was blocked by game state");
                return;
            }

            // Mirrors HandleEquipKeyPage — card inventory changes when the active
            // book changes, so do a full panel refresh.
            RefreshCharacterRenderer(
                unit,
                unit.OwnerSephirah,
                FloorListSlotBase + ui,
                refreshCardInventory: true
            );

            SaveAndBroadcast();
            SendResult(client, reqId, true, null);
        }

        /// <summary>
        /// Moves a card from the shared inventory into a librarian's deck.
        /// Requires the caller to hold the edit lock for the librarian.
        /// </summary>
        private void HandleAddCardToDeck(WebSocketClient client, JsonReader r, string reqId)
        {
            if (
                !TryResolveDeckEdit(client, r, reqId, out var book, out int deckIndex, out var lorId)
            )
                return;

            // Reject cards whose XML wasn't loaded — without errNull,
            // GetCardItem returns a fresh isError sentinel that would
            // pass AddCardFromInventory's checks if the inventory has
            // a count for the LorId, and end up unremovable from the
            // deck (see DeckModel.MoveCardToInventory: Remove uses
            // reference equality, but each GetCardItem call mints a
            // new sentinel instance). We deliberately do NOT scrub
            // existing error cards in the deck — the vanilla startup
            // dialog already offers that, and a user may keep them
            // pending the mod's reinstall.
            if (ItemXmlDataList.instance.GetCardItem(lorId, errNull: true) == null)
            {
                SendResult(
                    client,
                    reqId,
                    false,
                    "Card XML not found (likely from an uninstalled mod)"
                );
                return;
            }

            var result = WithActiveDeck(
                book,
                deckIndex,
                () => book.AddCardFromInventoryToCurrentDeck(lorId)
            );
            bool ok = result == CardEquipState.Equippable;

            if (ok)
                SaveAndBroadcast();

            SendResult(client, reqId, ok, ok ? null : result.ToString());
        }

        /// <summary>
        /// Returns a card from a librarian's deck back to the shared inventory.
        /// Requires the caller to hold the edit lock for the librarian.
        /// </summary>
        private void HandleRemoveCardFromDeck(WebSocketClient client, JsonReader r, string reqId)
        {
            if (
                !TryResolveDeckEdit(client, r, reqId, out var book, out int deckIndex, out var lorId)
            )
                return;

            bool removed = WithActiveDeck(
                book,
                deckIndex,
                () => book.MoveCardFromCurrentDeckToInventory(lorId)
            );

            if (removed)
                SaveAndBroadcast();

            SendResult(client, reqId, removed, removed ? null : "Card not found in deck");
        }

        // Multi-deck key pages expose up to 4 deck slots (indices 0..3); index 0 is the
        // always-present primary deck.
        private const int DeckSlotCount = 4;

        /// <summary>
        /// Shared validation for deck add/remove: requires the edit lock, resolves the
        /// book, validates the optional <c>deckIndex</c> against <c>IsMultiDeck</c>, and
        /// builds the card <c>LorId</c>. On any failure it sends the error result and
        /// returns false.
        /// </summary>
        private bool TryResolveDeckEdit(
            WebSocketClient client,
            JsonReader r,
            string reqId,
            out BookModel book,
            out int deckIndex,
            out LorId lorId
        )
        {
            book = null;
            deckIndex = 0;
            lorId = default;

            if (!r.TryGetInt("cardId", out int cardId))
                return false;

            var unit = ValidateLibrarianEdit(client, r, reqId, out _, out _);
            if (unit == null)
                return false;

            // packageId is an empty string for vanilla cards and a workshop ID for mods.
            string packageId = r.GetString("packageId") ?? "";

            book = unit.bookItem;
            if (book == null)
            {
                SendResult(client, reqId, false, "Deck not found");
                return false;
            }

            // Multi-deck addressing: optional deckIndex, defaults to active slot. Index
            // range and IsMultiDeck are validated before any state mutation so a bad
            // request can't transiently swap the active deck.
            deckIndex = r.TryGetInt("deckIndex", out int parsedIdx) ? parsedIdx : 0;
            if (deckIndex < 0 || deckIndex >= DeckSlotCount)
            {
                SendResult(client, reqId, false, "deckIndex out of range");
                return false;
            }
            if (deckIndex != 0 && !book.IsMultiDeck())
            {
                SendResult(client, reqId, false, "key page is not multi-deck");
                return false;
            }

            lorId = string.IsNullOrEmpty(packageId)
                ? new LorId(cardId)
                : new LorId(packageId, cardId);
            return true;
        }

        /// <summary>
        /// Runs <paramref name="op"/> with the book's active deck transiently switched to
        /// <paramref name="deckIndex"/>, restoring the previous active deck in a finally
        /// so a stance-change passive in battle observes only the user's intended active
        /// deck. Both this and any battle-thread code run on the Unity main thread, so no
        /// observer can interleave between the swap and the restore.
        /// </summary>
        private static T WithActiveDeck<T>(BookModel book, int deckIndex, System.Func<T> op)
        {
            int prevIdx = book.GetCurrentDeckIndex();
            try
            {
                if (deckIndex != prevIdx)
                    book.ChangeDeck(deckIndex);
                return op();
            }
            finally
            {
                if (book.GetCurrentDeckIndex() != prevIdx)
                    book.ChangeDeck(prevIdx);
            }
        }

        /// <summary>
        /// Returns the internal _activatedAllPassives list for a BookModel via
        /// reflection, since the field is private.
        /// </summary>
        private static System.Collections.Generic.List<PassiveModel> GetAllPassives(BookModel book)
        {
            return typeof(BookModel)
                    .GetField(
                        "_activatedAllPassives",
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance
                    )
                    ?.GetValue(book) as System.Collections.Generic.List<PassiveModel>;
        }

        /// <summary>
        /// Initializes reserved data on a book and all its passives so that the
        /// reservation/apply pattern works correctly for passive succession.
        /// </summary>
        private static void InitReserved(BookModel book)
        {
            book.InitReservedDataForPassiveSuccession();
            var passives = GetAllPassives(book);
            if (passives != null)
            {
                foreach (var p in passives)
                    p.InitReservedData();
            }
        }

        /// <summary>
        /// Persists the librarian-edit mutation to disk and pushes a state snapshot
        /// to all connected clients. Called after every successful librarian edit.
        /// </summary>
        private static void SaveAndBroadcast()
        {
            Singleton<GameSave.SaveManager>.Instance?.SavePlayData(1);
            StateBroadcaster.Broadcast();
        }

        /// <summary>
        /// Finds an equipped-inventory book by instance ID, or returns null if the
        /// inventory is unavailable or the book is not found.
        /// </summary>
        private static BookModel FindEquippedBook(int instanceId)
        {
            return BookInventoryModel
                .Instance?.GetBookList_equip()
                ?.Find(b => b?.instanceId == instanceId);
        }

        /// <summary>
        /// Equips a key page as a passive source for the librarian's current key page.
        /// The source book's passives become available for attribution to the target.
        /// Limited to 4 source books per target key page.
        /// </summary>
        private void HandleEquipSourceBook(WebSocketClient client, JsonReader r, string reqId)
        {
            var unit = ValidateLibrarianEdit(client, r, reqId, out int fi, out int ui);
            if (unit == null)
                return;

            if (!r.TryGetInt("bookInstanceId", out int bookInstanceId))
            {
                SendResult(client, reqId, false, "Missing bookInstanceId");
                return;
            }

            var targetBook = unit.bookItem;
            if (targetBook == null)
            {
                SendResult(client, reqId, false, "Librarian has no key page equipped");
                return;
            }

            var sourceBook = FindEquippedBook(bookInstanceId);
            if (sourceBook == null)
            {
                SendResult(client, reqId, false, "Source key page not found in inventory");
                return;
            }

            // source must not already be attributed to a different key page
            if (
                sourceBook.originData?.equipedPassiveBookInstanceId != -1
                && sourceBook.originData.equipedPassiveBookInstanceId != targetBook.instanceId
            )
            {
                SendResult(client, reqId, false, "Source key page is already attributed elsewhere");
                return;
            }

            // initialize reserved data before mutating
            InitReserved(targetBook);
            InitReserved(sourceBook);

            bool ok = targetBook.EquipGivePassiveBook(sourceBook);
            if (!ok)
            {
                SendResult(client, reqId, false, "Cannot equip source — 4-book limit reached");
                return;
            }

            targetBook.ApplyPassiveSuccession();
            sourceBook.ApplyPassiveSuccession();

            SaveAndBroadcast();
            SendResult(client, reqId, true, null);
        }

        /// <summary>
        /// Unequips a source book from the librarian's key page, releasing all
        /// passives that were attributed from it.
        /// </summary>
        private void HandleUnequipSourceBook(WebSocketClient client, JsonReader r, string reqId)
        {
            var unit = ValidateLibrarianEdit(client, r, reqId, out int fi, out int ui);
            if (unit == null)
                return;

            if (!r.TryGetInt("bookInstanceId", out int bookInstanceId))
            {
                SendResult(client, reqId, false, "Missing bookInstanceId");
                return;
            }

            var targetBook = unit.bookItem;
            if (targetBook == null)
            {
                SendResult(client, reqId, false, "Librarian has no key page equipped");
                return;
            }

            var sourceBook = FindEquippedBook(bookInstanceId);
            if (sourceBook == null)
            {
                SendResult(client, reqId, false, "Source key page not found in inventory");
                return;
            }

            // verify source is actually equipped on this target
            if (
                targetBook.originData?.equipedBookIdListInPassive == null
                || !targetBook.originData.equipedBookIdListInPassive.Contains(bookInstanceId)
            )
            {
                SendResult(client, reqId, false, "Source key page is not equipped on this target");
                return;
            }

            InitReserved(targetBook);
            InitReserved(sourceBook);

            targetBook.UnEquipGivePassiveBook(sourceBook);
            targetBook.ApplyPassiveSuccession();
            sourceBook.ApplyPassiveSuccession();

            SaveAndBroadcast();
            SendResult(client, reqId, true, null);
        }

        /// <summary>
        /// Attributes a passive from a source book to the librarian's key page.
        /// Finds the first available empty attribution slot on the target, validates
        /// eligibility (uniqueness, cost), and performs the change.
        /// </summary>
        private void HandleAttributePassive(WebSocketClient client, JsonReader r, string reqId)
        {
            var unit = ValidateLibrarianEdit(client, r, reqId, out int fi, out int ui);
            if (unit == null)
                return;

            if (
                !r.TryGetInt("sourceInstanceId", out int sourceInstanceId)
                || !r.TryGetInt("passiveId", out int passiveId)
            )
            {
                SendResult(client, reqId, false, "Missing sourceInstanceId or passiveId");
                return;
            }

            string passivePackageId = r.GetString("passivePackageId") ?? "";

            var targetBook = unit.bookItem;
            if (targetBook == null)
            {
                SendResult(client, reqId, false, "Librarian has no key page equipped");
                return;
            }

            // find the source book and the passive on it
            var sourceBook = FindEquippedBook(sourceInstanceId);
            if (sourceBook == null)
            {
                SendResult(client, reqId, false, "Source key page not found");
                return;
            }

            var passiveLorId = string.IsNullOrEmpty(passivePackageId)
                ? new LorId(passiveId)
                : new LorId(passivePackageId, passiveId);

            var sourcePassives = GetAllPassives(sourceBook);
            var sourcePassive = sourcePassives?.Find(p => p.originpassive?.id == passiveLorId);
            if (sourcePassive == null)
            {
                SendResult(client, reqId, false, "Passive not found on source key page");
                return;
            }

            // initialize reserved data before any mutations
            InitReserved(targetBook);
            InitReserved(sourceBook);

            // find the first empty attribution slot on target (placeholder passive
            // that has not already received an attributed passive)
            var targetPassives = GetAllPassives(targetBook);
            PassiveModel targetSlot = null;
            if (targetPassives != null)
            {
                targetSlot = targetPassives.Find(p =>
                    p.originpassive?.id == GameStateSerializer.EmptyAttributionPassiveId
                    && !p.IsReceivedSuccessionPassive
                );
            }
            if (targetSlot == null)
            {
                SendResult(client, reqId, false, "No empty passive slot available");
                return;
            }

            // validate: unique passive check
            GivePassiveState state;
            if (!targetBook.CanSuccessionPassive(sourcePassive, out state))
            {
                SendResult(client, reqId, false, "Cannot attribute passive: " + state);
                return;
            }

            // validate: cost budget
            if (!targetBook.CanSuccessionPassiveByCost(targetSlot, sourcePassive))
            {
                SendResult(client, reqId, false, "Insufficient passive cost budget");
                return;
            }

            targetBook.ChangePassive(targetSlot, sourcePassive);
            targetBook.ApplyPassiveSuccession();
            sourceBook.ApplyPassiveSuccession();

            SaveAndBroadcast();
            SendResult(client, reqId, true, null);
        }

        /// <summary>
        /// Removes a previously attributed passive from the librarian's key page,
        /// restoring its dummy slot and releasing the source passive.
        /// </summary>
        private void HandleRemoveAttributedPassive(
            WebSocketClient client,
            JsonReader r,
            string reqId
        )
        {
            var unit = ValidateLibrarianEdit(client, r, reqId, out int fi, out int ui);
            if (unit == null)
                return;

            if (
                !r.TryGetInt("sourceInstanceId", out int sourceInstanceId)
                || !r.TryGetInt("passiveId", out int passiveId)
            )
            {
                SendResult(client, reqId, false, "Missing sourceInstanceId or passiveId");
                return;
            }

            string passivePackageId = r.GetString("passivePackageId") ?? "";

            var targetBook = unit.bookItem;
            if (targetBook == null)
            {
                SendResult(client, reqId, false, "Librarian has no key page equipped");
                return;
            }

            var passiveLorId = string.IsNullOrEmpty(passivePackageId)
                ? new LorId(passiveId)
                : new LorId(passivePackageId, passiveId);

            // initialize reserved data before mutations
            InitReserved(targetBook);

            // find the matching attributed passive on the target book
            var targetPassives = GetAllPassives(targetBook);
            PassiveModel toRemove = null;
            if (targetPassives != null)
            {
                toRemove = targetPassives.Find(p =>
                    p.IsReceivedSuccessionPassive
                    && p.reservedData?.currentpassive?.id == passiveLorId
                    && p.reservedData?.receivepassivebookId == sourceInstanceId
                );
            }
            if (toRemove == null)
            {
                SendResult(client, reqId, false, "Attributed passive not found");
                return;
            }

            // also init reserved data on the source book so the give-side is released cleanly
            var sourceBook = FindEquippedBook(sourceInstanceId);
            if (sourceBook != null)
                InitReserved(sourceBook);

            targetBook.ReleasePassive(toRemove);
            targetBook.ApplyPassiveSuccession();
            if (sourceBook != null)
                sourceBook.ApplyPassiveSuccession();

            SaveAndBroadcast();
            SendResult(client, reqId, true, null);
        }

        /// <summary>
        /// Applies a batch customization update (appearance, dialogue, titles) to a librarian.
        /// All fields are sent flat in the JSON payload to avoid nested-parsing complexity.
        /// Colors are passed as separate R/G/B integer fields (0–255).
        /// An empty string for a dialogue field restores a random game preset.
        /// </summary>
        private void HandleSetCustomization(WebSocketClient client, JsonReader r, string reqId)
        {
            var unit = ValidateLibrarianEdit(client, r, reqId, out int fi, out int ui);
            if (unit == null)
                return;

            var cd = unit.customizeData;

            // Face/hair/color fields: skip for sephirah (patron) units whose
            // heads use SpecialCustomizedAppearance — the renderer ignores
            // individual sprite IDs and colors, so applying them is a no-op.
            if (cd != null && !unit.isSephirah)
            {
                if (r.TryGetInt("frontHairID", out int fh))
                    cd.frontHairID = fh;
                if (r.TryGetInt("backHairID", out int bh))
                    cd.backHairID = bh;
                if (r.TryGetInt("eyeID", out int eid))
                    cd.eyeID = eid;
                if (r.TryGetInt("browID", out int bid))
                    cd.browID = bid;
                if (r.TryGetInt("mouthID", out int mid))
                    cd.mouthID = mid;
                if (r.TryGetInt("headID", out int hid))
                    cd.headID = hid;

                if (
                    r.TryGetInt("hairR", out int hairR)
                    && r.TryGetInt("hairG", out int hairG)
                    && r.TryGetInt("hairB", out int hairB)
                )
                    cd.hairColor = new Color32((byte)hairR, (byte)hairG, (byte)hairB, 255);

                if (
                    r.TryGetInt("skinR", out int skinR)
                    && r.TryGetInt("skinG", out int skinG)
                    && r.TryGetInt("skinB", out int skinB)
                )
                    cd.skinColor = new Color32((byte)skinR, (byte)skinG, (byte)skinB, 255);

                if (
                    r.TryGetInt("eyeR", out int eyeR)
                    && r.TryGetInt("eyeG", out int eyeG)
                    && r.TryGetInt("eyeB", out int eyeB)
                )
                    cd.eyeColor = new Color32((byte)eyeR, (byte)eyeG, (byte)eyeB, 255);
            }

            // Height is under projection (always editable, even for sephirah).
            if (cd != null && r.TryGetInt("height", out int ht))
                cd.height = ht;

            // Body type: switches between gendered prefab variants (_F / _M / _N).
            var at = r.GetString("appearanceType");
            if (!string.IsNullOrEmpty(at))
            {
                try
                {
                    unit.appearanceType = (Gender)System.Enum.Parse(typeof(Gender), at);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[PRWM] SetCustomization: invalid appearanceType '{at}': {ex.Message}"
                    );
                }
            }

            // Apply dialogue changes. Skip for sephirah units (no BattleDialogueModel).
            // An empty/null custom text restores a random preset.
            var dlgXml = Singleton<BattleDialogXmlList>.Instance;
            var dlgModel = unit.battleDialogModel;
            if (dlgModel == null && dlgXml != null && !unit.isSephirah)
            {
                // Initialize dialogue model for librarians that never had one set
                // (e.g. freshly-created non-Sephirah units).
                try
                {
                    var charData = dlgXml.GetCharacterData("Librarian", "Librarian");
                    if (charData != null)
                    {
                        dlgModel = new BattleDialogueModel(charData);
                        unit.battleDialogModel = dlgModel;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[PRWM] SetCustomization: failed to initialize Librarian dialogue model: {ex.Message}"
                    );
                }
            }

            if (dlgModel != null)
            {
                ApplyDialogue(dlgModel, r, LOR_XML.DialogType.START_BATTLE, "dlgStartBattle");
                ApplyDialogue(dlgModel, r, LOR_XML.DialogType.BATTLE_VICTORY, "dlgVictory");
                ApplyDialogue(dlgModel, r, LOR_XML.DialogType.DEATH, "dlgDeath");
                ApplyDialogue(dlgModel, r, LOR_XML.DialogType.COLLEAGUE_DEATH, "dlgColleagueDeath");
                ApplyDialogue(dlgModel, r, LOR_XML.DialogType.KILLS_OPPONENT, "dlgKillsOpponent");
            }

            // Title IDs.
            if (r.TryGetInt("prefixID", out int pfx))
                unit.prefixID = pfx;
            if (r.TryGetInt("postfixID", out int sfx))
                unit.postfixID = sfx;

            // Fashion projection: equip a custom core book as appearance skin, or unequip.
            // -1 means unequip; any other value is a BookXmlInfo ID to equip.
            // Workshop books carry an additional packageId to form a full LorId.
            if (r.TryGetInt("customBookId", out int cbid))
            {
                if (cbid < 0)
                {
                    unit.EquipCustomCoreBook(null);
                }
                else
                {
                    string cbPkg = r.GetString("customBookPackageId");
                    LorId bookLorId = string.IsNullOrEmpty(cbPkg)
                        ? new LorId(cbid)
                        : new LorId(cbPkg, cbid);
                    var bxi = Singleton<BookXmlList>.Instance?.GetData(bookLorId, errNull: false);
                    if (bxi != null)
                    {
                        unit.EquipCustomCoreBook(new BookModel(bxi));
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[PRWM] SetCustomization: book not found cbid={cbid} pkg={cbPkg}"
                        );
                    }
                }
            }

            // Workshop skin: cloth overlay from the CustomizingResourceLoader system.
            // The value is a contentFolderIdx string ("" unequips).
            // Only set when the key is present in the request.
            string wsKey = "workshopSkin";
            string wsSkin = r.GetString(wsKey);
            if (wsSkin != null)
                unit.workshopSkin = wsSkin;

            RefreshCharacterRenderer(unit, unit.OwnerSephirah, FloorListSlotBase + ui);

            SaveAndBroadcast();
            SendResult(client, reqId, true, null);
        }

        /// <summary>
        /// Returns the currently equipped gift at <paramref name="pos"/>, or null if
        /// the slot is empty. Shared helper for the visibility and unequip branches
        /// of <see cref="HandleSetGifts"/>.
        /// </summary>
        private static GiftModel FindEquippedGiftAt(GiftInventory inv, GiftPosition pos)
        {
            foreach (var g in inv.GetEquippedList())
                if (g.ClassInfo.Position == pos)
                    return g;
            return null;
        }

        /// <summary>
        /// Applies a batch gift update to a librarian: equips/unequips gifts by position index
        /// and toggles per-gift visibility. Keys gift0–gift8 carry a gift ID (-1 to unequip);
        /// vis0–vis8 carry a non-zero value to show or zero to hide the gift at that position.
        /// Visibility is processed before equip/unequip so callers can swap and hide in one message.
        /// </summary>
        private void HandleSetGifts(WebSocketClient client, JsonReader r, string reqId)
        {
            var unit = ValidateLibrarianEdit(client, r, reqId, out int fi, out int ui);
            if (unit == null)
                return;

            var inv = unit.giftInventory;
            if (inv == null)
            {
                SendResult(client, reqId, false, "Gift inventory not available");
                return;
            }

            bool changed = false;

            // Process each of the 9 gift positions (GiftPosition has values 0–8).
            for (int pos = 0; pos <= 8; pos++)
            {
                var giftPos = (GiftPosition)pos;

                // visibility is processed before equip/unequip so it applies to the
                // currently equipped gift at this position (not the incoming one).
                if (r.TryGetInt("vis" + pos, out int vis))
                {
                    var equipped = FindEquippedGiftAt(inv, giftPos);
                    if (equipped != null)
                    {
                        equipped.isShowEquipGift = (vis != 0);
                        changed = true;
                    }
                }

                if (r.TryGetInt("gift" + pos, out int giftId))
                {
                    if (giftId < 0)
                    {
                        var toUnequip = FindEquippedGiftAt(inv, giftPos);
                        if (toUnequip != null)
                        {
                            inv.UnEquip(toUnequip);
                            changed = true;
                        }
                    }
                    else
                    {
                        // Equip: find the matching gift in the unequipped list by ID and position.
                        GiftModel toEquip = null;
                        foreach (var g in inv.GetUnequippedList())
                        {
                            if (g.GetGiftClassInfoId() == giftId && g.ClassInfo.Position == giftPos)
                            {
                                toEquip = g;
                                break;
                            }
                        }

                        if (toEquip != null)
                        {
                            // Equip auto-swaps if the slot is already occupied.
                            inv.Equip(toEquip);
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                RefreshCharacterRenderer(unit, unit.OwnerSephirah, FloorListSlotBase + ui);
                SaveAndBroadcast();
            }

            SendResult(client, reqId, true, null);
        }

        // Applies one dialogue field: empty/absent = restore random; non-empty = set custom.
        private static void ApplyDialogue(
            BattleDialogueModel model,
            JsonReader r,
            LOR_XML.DialogType type,
            string key
        )
        {
            string val = r.GetString(key);
            if (val == null)
                return; // field not present — leave unchanged
            if (string.IsNullOrEmpty(val))
                model.SetDialogByRandom(type); // clear custom, restore random preset
            else
                model.SetDialogByCustom(type, val);
        }

        // UICharacterRenderer slot assignments used by SetCharacterRenderer(fromLeft:false).
        // Floor overview shows each unit at FloorListSlotBase + unitIndex; the librarian
        // detail view always uses LibrarianDetailSlot.
        private const int FloorListSlotBase = 5;
        private const int LibrarianDetailSlot = 10;

        /// <summary>
        /// Validates a librarian edit request: parses floor/unit indices, checks lock
        /// ownership, and resolves the UnitDataModel. Returns the unit on success, or
        /// null after sending an error reply.
        /// </summary>
        private UnitDataModel ValidateLibrarianEdit(
            WebSocketClient client,
            JsonReader r,
            string reqId,
            out int floorIndex,
            out int unitIndex
        )
        {
            floorIndex = -1;
            unitIndex = -1;

            if (
                !r.TryGetInt("floorIndex", out floorIndex)
                || !r.TryGetInt("unitIndex", out unitIndex)
            )
                return null;

            string key = LockKey(floorIndex, unitIndex);
            if (!_sessionManager.IsLibrarianLockHolder(key, client.SessionId))
            {
                SendResult(client, reqId, false, "Not authorized — acquire lock first");
                return null;
            }

            var unit = GetLibrarianUnit(floorIndex, unitIndex);
            if (unit == null)
            {
                SendResult(client, reqId, false, "Librarian not found");
                return null;
            }

            return unit;
        }

        /// <summary>
        /// Schedules a character renderer refresh on the Unity main thread. Reloads
        /// the floor list slot and detail panel slot so the new appearance is picked up.
        /// </summary>
        /// <param name="refreshCardInventory">
        /// When true, the Librarian_CardList branch does a full card panel refresh
        /// (equip panel, inventory list). When false, only the info panel is updated.
        /// </param>
        private void RefreshCharacterRenderer(
            UnitDataModel unit,
            SephirahType sephirah,
            int characterSlot,
            bool refreshCardInventory = false
        )
        {
            var unitRef = unit;
            StateBroadcaster.RunOnMainThread(() =>
            {
                var uic = UI.UIController.Instance;
                if (uic == null)
                    return;

                var renderer = SingletonBehavior<UI.UICharacterRenderer>.Instance;

                if (uic.CurrentSephirah == sephirah)
                {
                    renderer?.SetCharacter(unitRef, characterSlot, forcelyReload: true);
                    var listPanel =
                        uic.GetUIPanel(UI.UIPanelType.CharacterList_Right)
                        as UI.UILibrarianCharacterListPanel;
                    listPanel?.SetLibrarianCharacterListPanel_Default(sephirah);
                }

                if (uic.CurrentUnit == unitRef)
                {
                    if (uic.CurrentUIPhase == UI.UIPhase.Librarian)
                    {
                        var infoPanel =
                            uic.GetUIPanel(UI.UIPanelType.LibrarianInfo) as UI.UILibrarianInfoPanel;
                        if (infoPanel != null)
                        {
                            renderer?.SetCharacter(
                                unitRef,
                                LibrarianDetailSlot,
                                forcelyReload: true
                            );
                            infoPanel.UpdatePanel();
                        }
                    }
                    else if (uic.CurrentUIPhase == UI.UIPhase.Librarian_CardList)
                    {
                        var cardPanel = uic.GetUIPanel(UI.UIPanelType.Page) as UI.UICardPanel;
                        if (refreshCardInventory)
                        {
                            cardPanel?.LibrarianInfoPanel?.SetData(unitRef);
                            cardPanel?.EquipInfoDeckPanel?.SetData();
                            cardPanel?.InvenCardList?.SetData(
                                Singleton<InventoryModel>.Instance?.GetCardList(),
                                unitRef
                            );
                        }
                        else
                        {
                            cardPanel?.LibrarianInfoPanel?.SetData(unitRef);
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Resolves a floor/unit index pair to the corresponding UnitDataModel,
        /// or null if the floor is not open or the index is out of range.
        /// Internal so ActionInjector can reuse it for the rename action.
        /// </summary>
        internal static UnitDataModel GetLibrarianUnit(int floorIndex, int unitIndex)
        {
            var sephirahs = GameStateSerializer.Sephirahs;

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
    }
}
