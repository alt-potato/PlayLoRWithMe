using System;
using System.Collections.Generic;
using System.Reflection;
using UI;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Resolves localized deck labels for known multi-deck key pages.
    ///
    /// Two label sources, checked in order:
    ///
    /// 1. <c>RecordLabels</c> cache, populated by
    ///    <see cref="MultiDeckLabelHook"/> after every
    ///    <c>UIEquipDeckCardList.SetDeckLayout</c> invocation. This captures
    ///    whatever any mod's Harmony patches resolved <em>after</em> the
    ///    engine's defaults — including custom literal strings written
    ///    directly to <c>TabName.text</c> (e.g. the Binah Multi-Deck mod's
    ///    "Philosophy" / "Arbiter"). Only fires once the player has opened
    ///    the in-game deck editor for that librarian, so the cache is empty
    ///    until then.
    ///
    /// 2. <c>LabelTextIds</c> static table, mapping known key pages to
    ///    <see cref="TextDataModel"/> ids that ship with localized strings
    ///    in every supported language. The engine's deck-editor prefab uses
    ///    these ids by default (see the Binah mod's
    ///    <c>SetDeckLayout</c> pre-patch, workshop 2788324005), so vanilla
    ///    multi-deck books like The Purple Tear resolve correctly here even
    ///    before the player has opened the editor.
    ///
    /// Books missing from both sources fall through to the frontend's
    /// generic <c>Deck N</c> placeholder.
    /// </summary>
    public static class MultiDeckLabels
    {
        // Cache populated by MultiDeckLabelHook on each SetDeckLayout call.
        // Length-4 arrays; individual entries may be null/empty when a
        // multi-deck book hides some of its tabs (the engine constructs the
        // tab strip with all four buttons but mods deactivate unused ones).
        // Locked because Harmony patches and the broadcast thread can both
        // read; writes only come from the main-thread postfix.
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<KpKey, string[]> Cache =
            new Dictionary<KpKey, string[]>();

        // Length of a multi-deck label vector; mirrors BookModel._deckList's
        // fixed size (4 slots regardless of how many a key page actually uses).
        public const int LabelCount = 4;

        /// <summary>
        /// Records the tab labels observed for a book by
        /// <see cref="MultiDeckLabelHook"/>. <paramref name="tabLabels"/> may
        /// be shorter than <see cref="LabelCount"/> or contain null/empty
        /// entries for hidden tabs; both are preserved as-is. Returns true
        /// when the cache contents changed, so the caller can trigger a
        /// state broadcast — without that, the just-cached labels would
        /// only reach connected clients on the next unrelated state push.
        /// </summary>
        public static bool RecordLabels(BookModel book, string[] tabLabels)
        {
            if (book == null || tabLabels == null)
                return false;
            var lid = book.GetBookClassInfoId();
            var k = new KpKey(lid.packageId ?? "", lid.id);

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

        // Reflected once: SetDeckLayout is private on UIEquipDeckCardList.
        private static readonly MethodInfo SetDeckLayoutMethod =
            typeof(UIEquipDeckCardList).GetMethod(
                "SetDeckLayout",
                BindingFlags.Instance | BindingFlags.NonPublic);

        // True while we're inside a synthetic SetDeckLayout invocation. The
        // hook checks this so it doesn't trigger a broadcast from within
        // the serializer (which is already preparing one) — that would
        // recursively fan out one broadcast per multi-deck book on the
        // first state push.
        private static bool _inSynthetic;
        public static bool InSyntheticInvoke => _inSynthetic;

        // Diagnostic throttling: log each (book, message) outcome at most
        // once per game session. Without this the per-broadcast retry
        // would spam Player.log forever for any book whose panel isn't
        // in the scene.
        private static readonly HashSet<string> LoggedOnce = new HashSet<string>();
        private static void LogOnce(string key, string msg)
        {
            if (!LoggedOnce.Add(key))
                return;
            Debug.Log(msg);
        }

        /// <summary>
        /// Synthesizes the patch chain on <c>UIEquipDeckCardList.SetDeckLayout</c>
        /// for a multi-deck book that the player hasn't opened in-game yet.
        /// Mods like Binah Multi-Deck only assign their custom tab labels via
        /// Harmony postfixes on that method; without invoking it, the
        /// <see cref="MultiDeckLabelHook"/> postfix has no event to attach
        /// to and the cache stays empty. We swap the panel's
        /// <c>currentunit</c> to <paramref name="unitData"/>, run the
        /// private method via reflection to fire all postfixes (including
        /// ours), then restore — visual flicker is bounded to the rare
        /// case where the in-game panel happens to be visible at broadcast
        /// time, and the panel's next natural <c>SetData</c> call resets it.
        /// </summary>
        public static void EnsureLabelsCached(BookModel book, UnitDataModel unitData)
        {
            if (book == null || unitData == null)
                return;
            if (!book.IsMultiDeck())
                return;

            var lid = book.GetBookClassInfoId();
            var k = new KpKey(lid.packageId ?? "", lid.id);
            lock (CacheLock)
            {
                if (Cache.ContainsKey(k))
                    return;
            }

            if (SetDeckLayoutMethod == null)
                return;
            // Retry every broadcast until the panel is in the scene and
            // we get a successful fill. The fast path is FindObjectsOfTypeAll
            // returning empty / our cache hit short-circuit, so the cost
            // is negligible during the title screen / pre-library phases.

            UIEquipDeckCardList panel = null;
            UIEquipDeckCardList[] panels = null;
            try
            {
                // FindObjectsOfTypeAll returns active+inactive scene objects
                // and prefabs already loaded into memory. Either works for
                // our purposes — we just need any instance whose private
                // multiDeckLayout field has been wired up.
                panels = Resources.FindObjectsOfTypeAll<UIEquipDeckCardList>();
                if (panels != null && panels.Length > 0)
                    panel = panels[0];
            }
            catch (Exception e)
            {
                LogOnce(
                    "find-threw:" + lid.packageId + ":" + lid.id,
                    "[multi-deck-labels] FindObjectsOfTypeAll threw: " + e.Message);
                return;
            }
            if (panel == null)
            {
                // Common during early scene-load — log once per book so
                // we can distinguish "panel not yet in scene" from
                // invocation failures further down.
                LogOnce(
                    "no-panel:" + lid.packageId + ":" + lid.id,
                    "[multi-deck-labels] no UIEquipDeckCardList instance for book=("
                        + (lid.packageId ?? "") + "," + lid.id
                        + "); foundCount=" + (panels?.Length ?? 0));
                return;
            }

            var saved = panel.currentunit;
            _inSynthetic = true;
            try
            {
                panel.currentunit = unitData;
                SetDeckLayoutMethod.Invoke(panel, null);
                LogOnce(
                    "invoke-ok:" + lid.packageId + ":" + lid.id,
                    "[multi-deck-labels] synthetic invoke ok for book=("
                        + (lid.packageId ?? "") + "," + lid.id + ")");
            }
            catch (Exception e)
            {
                // Best-effort: if invoking the patch chain throws (e.g.
                // because some other mod's prefix/postfix can't handle
                // the synthetic invocation), the cache stays empty and we
                // retry on the next broadcast.
                LogOnce(
                    "invoke-threw:" + lid.packageId + ":" + lid.id,
                    "[multi-deck-labels] synthetic invoke threw for book=("
                        + (lid.packageId ?? "") + "," + lid.id + "): "
                        + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                panel.currentunit = saved;
                _inSynthetic = false;
            }
        }

        /// <summary>
        /// Effective deck count for a multi-deck book — the number of slots
        /// the in-game editor actually exposes. Resolution order:
        ///
        /// 1. Cache (populated by <see cref="MultiDeckLabelHook"/>): returns
        ///    "highest non-null label index + 1" so observed mod overrides
        ///    that hide unused tabs win immediately.
        /// 2. <see cref="DeckCountOverrides"/> static table: lets known
        ///    mod-multi-deck books surface the correct shape on the first
        ///    state push, before the in-game panel has had a chance to
        ///    populate the cache.
        /// 3. <see cref="LabelCount"/> (4) as the conservative default.
        /// </summary>
        public static int GetEffectiveDeckCount(BookModel book)
        {
            if (book == null || !book.IsMultiDeck())
                return 1;

            var lid = book.GetBookClassInfoId();
            var k = new KpKey(lid.packageId ?? "", lid.id);

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
        // index order 0..3. We resolve each id through TextDataModel at
        // serialization time so the wire payload carries strings in the
        // player's game language.
        private static readonly Dictionary<KpKey, string[]> LabelTextIds =
            new Dictionary<KpKey, string[]>
            {
                // The Purple Tear uses the engine's standard stance ids.
                { new KpKey("", 250035), StanceFormTextIds },
            };

        // Extension point for static deck-count overrides. Intentionally
        // empty: we don't encode mod-specific knowledge here because
        // mod-modified key pages share their LorId with the vanilla page
        // they patch, so a hardcoded entry would also affect vanilla
        // (where the same id is single-deck). Left as an entry point in
        // case a future case turns up where a key page is universally
        // multi-deck and the engine's UI never renders the right shape
        // — until then deck count is detected from the cache alone.
        private static readonly Dictionary<KpKey, int> DeckCountOverrides =
            new Dictionary<KpKey, int>();

        /// <summary>
        /// Resolves the deck labels for the given book. Returns false when
        /// no source has any label for this book. On true,
        /// <paramref name="labels"/> is always length <see cref="LabelCount"/>
        /// but individual entries may be <c>null</c> or empty — callers
        /// SHOULD check each entry before emitting <c>label</c> on the wire.
        ///
        /// Resolution order: cache (populated by the SetDeckLayout hook;
        /// captures custom mod overrides) wins where present; otherwise
        /// the static text-id table runs through TextDataModel for known
        /// vanilla pages.
        /// </summary>
        public static bool TryGetLabels(BookModel book, out string[] labels)
        {
            labels = null;
            if (book == null || !book.IsMultiDeck())
                return false;

            var lid = book.GetBookClassInfoId();
            var k = new KpKey(lid.packageId ?? "", lid.id);

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
                    // GetText returns "" and logs an error if the id isn't
                    // known; it can also throw when textDic hasn't been
                    // populated yet. Both mean "not ready, skip the label".
                    text = TextDataModel.GetText(textIds[i]);
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
        private struct KpKey : IEquatable<KpKey>
        {
            public readonly string PackageId;
            public readonly int BookId;

            public KpKey(string packageId, int bookId)
            {
                PackageId = packageId ?? "";
                BookId = bookId;
            }

            public bool Equals(KpKey other) =>
                BookId == other.BookId && PackageId == other.PackageId;

            public override bool Equals(object obj) =>
                obj is KpKey k && Equals(k);

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
