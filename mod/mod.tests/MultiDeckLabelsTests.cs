using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PlayLoRWithMe;
using Xunit;

namespace PlayLoRWithMe.Tests
{
    /// <summary>
    /// Coverage for <see cref="MultiDeckLabels"/> — the two-source (runtime cache, then
    /// static text-id table) label and deck-count resolution logic, with game-type
    /// coupling (BookModel, TextDataModel) stripped out at the boundary so this can run
    /// headless. See <see cref="MultiDeckLabelsAdapter"/> for the game-typed wrapper.
    /// </summary>
    /// <remarks>
    /// The cache is static process-wide state, so every test clears it first rather than
    /// relying on xUnit's per-class isolation (which does not extend to statics shared
    /// across collections) — mirroring StoryLogTests' approach for the same reason.
    ///
    /// The one entry in the production LabelTextIds table is keyed on packageId "" and
    /// book id 250035 (The Purple Tear). Tests that need to exercise the static-table
    /// path reuse that exact key; tests that only care about cache behaviour use
    /// distinct, made-up book ids so they can't collide with it.
    /// </remarks>
    public class MultiDeckLabelsTests
    {
        // The Purple Tear's key, matching MultiDeckLabels' internal LabelTextIds table.
        private const string PurpleTearPackageId = "";
        private const int PurpleTearBookId = 250035;

        private static readonly string[] PurpleTearStanceIds =
            { "ui_slash_form", "ui_penetrate_form", "ui_hit_form", "ui_defense_form" };

        public MultiDeckLabelsTests() => MultiDeckLabels.ClearCacheForTests();

        private static string ThrowingResolver(string id) =>
            throw new InvalidOperationException(
                $"resolveText should not have been called for id '{id}' when the cache already has an entry"
            );

        // ── TryGetLabels: source ordering ──────────────────────────────────────────

        [Fact]
        public void TryGetLabels_CacheHit_TakesPriorityOverStaticTable()
        {
            var cached = new[] { "Cached A", "Cached B", "Cached C", "Cached D" };
            Assert.True(MultiDeckLabels.RecordLabels(PurpleTearPackageId, PurpleTearBookId, cached));

            bool found = MultiDeckLabels.TryGetLabels(
                PurpleTearPackageId,
                PurpleTearBookId,
                isMultiDeck: true,
                resolveText: ThrowingResolver,
                out var labels
            );

            Assert.True(found);
            Assert.Equal(cached, labels);
        }

        [Fact]
        public void TryGetLabels_UsesStaticTable_WhenCacheHasNoEntry()
        {
            bool found = MultiDeckLabels.TryGetLabels(
                PurpleTearPackageId,
                PurpleTearBookId,
                isMultiDeck: true,
                resolveText: id => "resolved:" + id,
                out var labels
            );

            Assert.True(found);
            Assert.Equal(4, labels.Length);
            for (int i = 0; i < PurpleTearStanceIds.Length; i++)
                Assert.Equal("resolved:" + PurpleTearStanceIds[i], labels[i]);
        }

        [Fact]
        public void TryGetLabels_FallsThroughToFalse_WhenNeitherSourceHasEntry()
        {
            bool found = MultiDeckLabels.TryGetLabels(
                "some.unknown.package",
                999999,
                isMultiDeck: true,
                resolveText: id => "irrelevant",
                out var labels
            );

            Assert.False(found);
            Assert.Null(labels);
        }

        [Fact]
        public void TryGetLabels_ReturnsFalse_WhenBookIsNotMultiDeck()
        {
            // Even the known Purple Tear key must not resolve if the caller reports the
            // book as single-deck (a mod-modified variant of a vanilla id, say).
            bool found = MultiDeckLabels.TryGetLabels(
                PurpleTearPackageId,
                PurpleTearBookId,
                isMultiDeck: false,
                resolveText: ThrowingResolver,
                out var labels
            );

            Assert.False(found);
            Assert.Null(labels);
        }

        [Fact]
        public void TryGetLabels_ReturnsFalse_WhenStaticTableResolverThrows()
        {
            // Mirrors TextDataModel.GetText throwing when its backing dictionary isn't
            // populated yet -- treated as "not ready", not as a hard failure.
            bool found = MultiDeckLabels.TryGetLabels(
                PurpleTearPackageId,
                PurpleTearBookId,
                isMultiDeck: true,
                resolveText: _ => throw new Exception("not ready"),
                out var labels
            );

            Assert.False(found);
            Assert.Null(labels);
        }

        [Fact]
        public void TryGetLabels_ReturnsFalse_WhenStaticTableResolverReturnsEmpty()
        {
            // Mirrors TextDataModel.GetText returning "" for an unresolved id.
            bool found = MultiDeckLabels.TryGetLabels(
                PurpleTearPackageId,
                PurpleTearBookId,
                isMultiDeck: true,
                resolveText: _ => "",
                out var labels
            );

            Assert.False(found);
            Assert.Null(labels);
        }

        // ── RecordLabels: null/empty entries and the "changed" contract ─────────────

        [Fact]
        public void RecordLabels_PreservesNullAndEmptyEntries_ForHiddenTabs()
        {
            // Mirrors what MultiDeckLabelHook writes when the engine's tab strip has
            // hidden buttons: null for tabs it never wrote text into, "" if a mod wrote
            // an explicitly blank label.
            var observed = new[] { "Tab0", null, "", "Tab3" };
            Assert.True(MultiDeckLabels.RecordLabels("pkg", 42, observed));

            bool found = MultiDeckLabels.TryGetLabels(
                "pkg",
                42,
                isMultiDeck: true,
                resolveText: ThrowingResolver,
                out var labels
            );

            Assert.True(found);
            Assert.Equal("Tab0", labels[0]);
            Assert.Null(labels[1]);
            Assert.Equal("", labels[2]);
            Assert.Equal("Tab3", labels[3]);
        }

        [Fact]
        public void RecordLabels_PadsShorterArraysWithNull()
        {
            // A book that only ever shows 2 tabs -- the snapshot must still be
            // LabelCount long so downstream indexing (e.g. deckObj.Add("label", ...))
            // never runs off the end.
            Assert.True(MultiDeckLabels.RecordLabels("pkg", 7, new[] { "A", "B" }));

            MultiDeckLabels.TryGetLabels(
                "pkg",
                7,
                isMultiDeck: true,
                resolveText: ThrowingResolver,
                out var labels
            );

            Assert.Equal(MultiDeckLabels.LabelCount, labels.Length);
            Assert.Equal("A", labels[0]);
            Assert.Equal("B", labels[1]);
            Assert.Null(labels[2]);
            Assert.Null(labels[3]);
        }

        [Fact]
        public void RecordLabels_ReturnsFalse_WhenTabLabelsIsNull()
        {
            Assert.False(MultiDeckLabels.RecordLabels("pkg", 1, null));
        }

        [Fact]
        public void RecordLabels_ReturnsTrue_OnFirstObservation()
        {
            Assert.True(MultiDeckLabels.RecordLabels("pkg", 8, new[] { "A", "B", "C", "D" }));
        }

        [Fact]
        public void RecordLabels_ReturnsFalse_WhenContentsUnchanged()
        {
            var labels = new[] { "A", "B", "C", "D" };
            Assert.True(MultiDeckLabels.RecordLabels("pkg", 9, labels));

            // A fresh array with identical contents -- the contract is about content
            // equality, not reference equality.
            Assert.False(MultiDeckLabels.RecordLabels("pkg", 9, new[] { "A", "B", "C", "D" }));
        }

        [Fact]
        public void RecordLabels_ReturnsTrue_WhenContentsChange()
        {
            Assert.True(MultiDeckLabels.RecordLabels("pkg", 10, new[] { "A", "B", "C", "D" }));
            Assert.True(MultiDeckLabels.RecordLabels("pkg", 10, new[] { "A", "B", "X", "D" }));
        }

        // ── GetEffectiveDeckCount ─────────────────────────────────────────────────

        [Fact]
        public void GetEffectiveDeckCount_ReturnsOne_WhenNotMultiDeck()
        {
            Assert.Equal(1, MultiDeckLabels.GetEffectiveDeckCount("pkg", 11, isMultiDeck: false));
        }

        [Fact]
        public void GetEffectiveDeckCount_FallsBackToLabelCount_WhenNoCacheAndNoOverride()
        {
            Assert.Equal(
                MultiDeckLabels.LabelCount,
                MultiDeckLabels.GetEffectiveDeckCount("unknown.pkg", 12345, isMultiDeck: true)
            );
        }

        [Fact]
        public void GetEffectiveDeckCount_UsesCache_HighestNonEmptyIndexPlusOne()
        {
            // Tabs 2 and 3 are hidden (null) -- only 2 slots should be exposed, matching
            // what MultiDeckLabelHook records when a mod deactivates unused tab buttons.
            MultiDeckLabels.RecordLabels("pkg", 13, new[] { "Slash", "Pierce", null, null });

            Assert.Equal(2, MultiDeckLabels.GetEffectiveDeckCount("pkg", 13, isMultiDeck: true));
        }

        [Fact]
        public void GetEffectiveDeckCount_FallsBackWhenCachedEntryIsAllEmpty()
        {
            // Every slot null/empty is the rare case documented on GetEffectiveDeckCount:
            // fall through to LabelCount rather than reporting zero decks.
            MultiDeckLabels.RecordLabels("pkg", 14, new string[] { null, null, null, null });

            Assert.Equal(
                MultiDeckLabels.LabelCount,
                MultiDeckLabels.GetEffectiveDeckCount("pkg", 14, isMultiDeck: true)
            );
        }

        // ── HasCachedLabels ───────────────────────────────────────────────────────

        [Fact]
        public void HasCachedLabels_ReflectsCacheState()
        {
            Assert.False(MultiDeckLabels.HasCachedLabels("pkg", 15));
            MultiDeckLabels.RecordLabels("pkg", 15, new[] { "A", "B", "C", "D" });
            Assert.True(MultiDeckLabels.HasCachedLabels("pkg", 15));
        }

        // ── InSyntheticInvoke ─────────────────────────────────────────────────────

        [Fact]
        public void InSyntheticInvoke_RoundTripsThroughSetter()
        {
            // Defensive reset: guards this test against leaking true into whichever test
            // runs next if an earlier run in the same process left it set.
            MultiDeckLabels.SetInSyntheticInvoke(false);
            Assert.False(MultiDeckLabels.InSyntheticInvoke);

            MultiDeckLabels.SetInSyntheticInvoke(true);
            Assert.True(MultiDeckLabels.InSyntheticInvoke);

            MultiDeckLabels.SetInSyntheticInvoke(false);
            Assert.False(MultiDeckLabels.InSyntheticInvoke);
        }

        // ── Thread safety ─────────────────────────────────────────────────────────

        [Fact]
        public async Task RecordLabels_And_TryGetLabels_AreSafeUnderConcurrentAccess()
        {
            // CacheLock exists because Harmony patches (main thread) and the broadcast
            // path (which can run from an HTTP thread) both touch the cache. This drives
            // concurrent writers and readers at one key and asserts every observation is
            // internally consistent (right length, no torn/partial array) and that
            // nothing throws -- the properties a lock is actually there to guarantee.
            const string pkg = "concurrent.pkg";
            const int bookId = 99;
            const int iterations = 500;

            Exception observedFailure = null;

            void Writer(int seed)
            {
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var labels = new[] { $"A{seed}-{i}", $"B{seed}-{i}", null, $"D{seed}-{i}" };
                        MultiDeckLabels.RecordLabels(pkg, bookId, labels);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref observedFailure, ex, null);
                }
            }

            void Reader()
            {
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        if (
                            MultiDeckLabels.TryGetLabels(
                                pkg,
                                bookId,
                                isMultiDeck: true,
                                resolveText: ThrowingResolver,
                                out var labels
                            )
                        )
                        {
                            Assert.Equal(MultiDeckLabels.LabelCount, labels.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref observedFailure, ex, null);
                }
            }

            var tasks = new List<Task>();
            for (int t = 0; t < 4; t++)
            {
                int seed = t;
                tasks.Add(Task.Run(() => Writer(seed)));
                tasks.Add(Task.Run(Reader));
            }
            await Task.WhenAll(tasks);

            Assert.Null(observedFailure);
        }
    }
}
