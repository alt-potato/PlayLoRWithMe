using System;
using System.Collections.Generic;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Resolves localized deck labels for known multi-deck key pages, given only the
    /// identifying primitives a caller can pull off a book (package id, book id,
    /// whether it is multi-deck) plus a text-resolution callback.
    ///
    /// Two label sources, checked in order:
    ///
    /// 1. <c>RecordLabels</c> cache, populated by the game-coupled adapter in
    ///    <see cref="MultiDeckLabelHook"/> after every
    ///    <c>UIEquipDeckCardList.SetDeckLayout</c> invocation. This captures
    ///    whatever any mod's Harmony patches resolved <em>after</em> the
    ///    engine's defaults — including custom literal strings written
    ///    directly to <c>TabName.text</c> (e.g. the Binah Multi-Deck mod's
    ///    "Philosophy" / "Arbiter"). Only fires once the player has opened
    ///    the in-game deck editor for that librarian, so the cache is empty
    ///    until then.
    ///
    /// 2. A static text-id table, mapping known key pages to
    ///    <c>TextDataModel</c> ids that ship with localized strings in every
    ///    supported language. The engine's deck-editor prefab uses these ids
    ///    by default (see the Binah mod's <c>SetDeckLayout</c> pre-patch,
    ///    workshop 2788324005), so vanilla multi-deck books like The Purple
    ///    Tear resolve correctly here even before the player has opened the
    ///    editor. Resolution goes through a caller-supplied delegate rather
    ///    than <c>TextDataModel</c> directly, which is what keeps this file
    ///    free of game types and lets it link into the headless test project.
    ///
    /// Books missing from both sources fall through to the frontend's
    /// generic <c>Deck N</c> placeholder.
    ///
    /// Deliberately free of Unity and Assembly-CSharp types, mirroring
    /// StoryLog.cs, so this file can be linked into the headless mod.tests
    /// project. <see cref="MultiDeckLabelsAdapter"/> and
    /// <see cref="MultiDeckLabelHook"/> own the mapping from the game's
    /// <c>BookModel</c>/<c>UnitDataModel</c> types and the UI reflection
    /// needed to synthesize a deck-layout invocation.
    /// </summary>
    internal static class MultiDeckLabels
    {
        // Cache populated (via RecordLabels) by the game-coupled adapter on each
        // SetDeckLayout observation. Length-4 arrays; individual entries may be
        // null/empty when a multi-deck book hides some of its tabs (the engine
        // constructs the tab strip with all four buttons but mods deactivate
        // unused ones).
        // Locked because Harmony patches and the broadcast thread can both
        // read; writes only come from the main-thread postfix.
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<BookKey, string[]> Cache =
            new Dictionary<BookKey, string[]>();

        // Length of a multi-deck label vector; mirrors BookModel._deckList's
        // fixed size (4 slots regardless of how many a key page actually uses).
        internal const int LabelCount = 4;

        // True while the adapter is inside a synthetic SetDeckLayout invocation.
        // MultiDeckLabelHook checks this so it doesn't trigger a broadcast from
        // within the serializer (which is already preparing one) — that would
        // recursively fan out one broadcast per multi-deck book on the first
        // state push. Only the adapter (the one caller that knows when a
        // synthetic invocation starts and ends) toggles this.
        private static bool _inSynthetic;
        internal static bool InSyntheticInvoke => _inSynthetic;

        /// <summary>Toggled by <see cref="MultiDeckLabelsAdapter.EnsureLabelsCached"/> around its synthetic invocation.</summary>
        internal static void SetInSyntheticInvoke(bool value) => _inSynthetic = value;

        /// <summary>
        /// Test-only: empties the label cache so xUnit tests can isolate cache state
        /// between cases, including cases that must reuse the one book key present in
        /// <see cref="LabelTextIds"/>. Production code never calls this — the cache is
        /// meant to persist for the lifetime of the game process.
        /// </summary>
        internal static void ClearCacheForTests()
        {
            lock (CacheLock)
                Cache.Clear();
        }

        /// <summary>
        /// Records the tab labels observed for a book. <paramref name="tabLabels"/>
        /// may be shorter than <see cref="LabelCount"/> or contain null/empty
        /// entries for hidden tabs; both are preserved as-is. Returns true
        /// when the cache contents changed, so the caller can trigger a
        /// state broadcast — without that, the just-cached labels would
        /// only reach connected clients on the next unrelated state push.
        /// </summary>
        internal static bool RecordLabels(string packageId, int bookId, string[] tabLabels)
        {
            if (tabLabels == null)
                return false;
            var k = new BookKey(packageId, bookId);

            var snapshot = new string[LabelCount];
            for (int i = 0; i < LabelCount && i < tabLabels.Length; i++)
                snapshot[i] = tabLabels[i];

            lock (CacheLock)
            {
                if (Cache.TryGetValue(k, out var existing) && SameContents(existing, snapshot))
                    return false;
                Cache[k] = snapshot;
            }
            return true;
        }

        private static bool SameContents(string[] a, string[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;
            return true;
        }

        /// <summary>Whether the cache already has an entry for this book, so the adapter can skip a redundant synthetic invocation.</summary>
        internal static bool HasCachedLabels(string packageId, int bookId)
        {
            lock (CacheLock)
                return Cache.ContainsKey(new BookKey(packageId, bookId));
        }

        /// <summary>
        /// Effective deck count for a multi-deck book — the number of slots
        /// the in-game editor actually exposes. Resolution order:
        ///
        /// 1. Cache: returns "highest non-null label index + 1" so observed
        ///    mod overrides that hide unused tabs win immediately.
        /// 2. <see cref="DeckCountOverrides"/> static table: lets known
        ///    mod-multi-deck books surface the correct shape on the first
        ///    state push, before the in-game panel has had a chance to
        ///    populate the cache.
        /// 3. <see cref="LabelCount"/> (4) as the conservative default.
        /// </summary>
        internal static int GetEffectiveDeckCount(string packageId, int bookId, bool isMultiDeck)
        {
            if (!isMultiDeck)
                return 1;

            var k = new BookKey(packageId, bookId);

            string[] cached;
            lock (CacheLock)
                Cache.TryGetValue(k, out cached);
            if (cached != null)
            {
                int last = -1;
                for (int i = 0; i < cached.Length; i++)
                    if (!string.IsNullOrEmpty(cached[i]))
                        last = i;
                if (last >= 0)
                    return last + 1;
                // fall through if every slot is null/empty (rare; the
                // static table or default still gives a sensible shape).
            }

            if (DeckCountOverrides.TryGetValue(k, out var overrideCount))
                return overrideCount;

            return LabelCount;
        }

        // The four TextDataModel ids the engine uses for the standard
        // stance tab labels — sourced from the Binah Multi-Deck mod's
        // SetDeckLayout pre-patch (workshop id 2788324005), which sets
        // these as the defaults before applying its own overrides.
        private static readonly string[] StanceFormTextIds =
            { "ui_slash_form", "ui_penetrate_form", "ui_hit_form", "ui_defense_form" };

        // Maps (packageId, bookId) -> array of TextDataModel ids in deck
        // index order 0..3. The caller resolves each id through its own
        // text-resolution delegate at serialization time so the wire payload
        // carries strings in the player's game language.
        private static readonly Dictionary<BookKey, string[]> LabelTextIds =
            new Dictionary<BookKey, string[]>
            {
                // The Purple Tear uses the engine's standard stance ids.
                { new BookKey("", 250035), StanceFormTextIds },
            };

        // Extension point for static deck-count overrides. Intentionally
        // empty: we don't encode mod-specific knowledge here because
        // mod-modified key pages share their LorId with the vanilla page
        // they patch, so a hardcoded entry would also affect vanilla
        // (where the same id is single-deck). Left as an entry point in
        // case a future case turns up where a key page is universally
        // multi-deck and the engine's UI never renders the right shape
        // — until then deck count is detected from the cache alone.
        private static readonly Dictionary<BookKey, int> DeckCountOverrides =
            new Dictionary<BookKey, int>();

        /// <summary>
        /// Resolves the deck labels for the given book. Returns false when
        /// no source has any label for this book. On true,
        /// <paramref name="labels"/> is always length <see cref="LabelCount"/>
        /// but individual entries may be <c>null</c> or empty — callers
        /// SHOULD check each entry before emitting <c>label</c> on the wire.
        ///
        /// Resolution order: cache (populated from the SetDeckLayout hook;
        /// captures custom mod overrides) wins where present; otherwise
        /// the static text-id table runs through <paramref name="resolveText"/>
        /// for known vanilla pages.
        /// </summary>
        internal static bool TryGetLabels(
            string packageId,
            int bookId,
            bool isMultiDeck,
            Func<string, string> resolveText,
            out string[] labels
        )
        {
            labels = null;
            if (!isMultiDeck)
                return false;

            var k = new BookKey(packageId, bookId);

            string[] cached;
            lock (CacheLock)
                Cache.TryGetValue(k, out cached);
            if (cached != null)
            {
                // Defensive copy so callers can't mutate the cached array.
                labels = (string[])cached.Clone();
                return true;
            }

            if (!LabelTextIds.TryGetValue(k, out var textIds))
                return false;

            var resolved = new string[LabelCount];
            for (int i = 0; i < textIds.Length && i < LabelCount; i++)
            {
                string text;
                try
                {
                    // The resolver returns "" (and the game logs an error) if the id
                    // isn't known; it can also throw when the caller's backing table
                    // hasn't been populated yet. Both mean "not ready, skip the label".
                    text = resolveText(textIds[i]);
                }
                catch
                {
                    return false;
                }
                if (string.IsNullOrEmpty(text))
                    return false;
                resolved[i] = text;
            }
            labels = resolved;
            return true;
        }

        // Tuples as keys would need .NET 4.7+ ValueTuple; the mod targets
        // 4.8 but pinning a custom struct keeps the dictionary usage
        // self-contained and avoids depending on the framework's tuple impl.
        private struct BookKey : IEquatable<BookKey>
        {
            public readonly string PackageId;
            public readonly int BookId;

            public BookKey(string packageId, int bookId)
            {
                PackageId = packageId ?? "";
                BookId = bookId;
            }

            public bool Equals(BookKey other) =>
                BookId == other.BookId && PackageId == other.PackageId;

            public override bool Equals(object obj) =>
                obj is BookKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (PackageId.GetHashCode() * 397) ^ BookId;
                }
            }
        }
    }
}
