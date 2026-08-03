using System.Reflection;
using UI;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Thin, game-typed boundary in front of the Unity-free <see cref="MultiDeckLabels"/>
    /// core. Every method here just extracts the primitives <see cref="MultiDeckLabels"/>
    /// actually needs off a <c>BookModel</c>/<c>UnitDataModel</c> and delegates, except
    /// <see cref="EnsureLabelsCached"/>, whose synthetic-invocation logic is inherently
    /// UI-reflection-based and has no primitives-only equivalent to delegate to.
    /// </summary>
    internal static class MultiDeckLabelsAdapter
    {
        // Reflected once: SetDeckLayout is private on UIEquipDeckCardList.
        private static readonly MethodInfo SetDeckLayoutMethod =
            typeof(UIEquipDeckCardList).GetMethod(
                "SetDeckLayout",
                BindingFlags.Instance | BindingFlags.NonPublic);

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
        internal static void EnsureLabelsCached(BookModel book, UnitDataModel unitData)
        {
            if (book == null || unitData == null)
                return;
            if (!book.IsMultiDeck())
                return;

            var lid = book.GetBookClassInfoId();
            if (MultiDeckLabels.HasCachedLabels(lid.packageId, lid.id))
                return;

            if (SetDeckLayoutMethod == null)
                return;
            // Retry every broadcast until the panel is in the scene and
            // we get a successful fill. The fast path is FindObjectsOfTypeAll
            // returning empty / our cache hit short-circuit, so the cost
            // is negligible during the title screen / pre-library phases.

            UIEquipDeckCardList panel = null;
            try
            {
                // FindObjectsOfTypeAll returns active+inactive scene objects
                // and prefabs already loaded into memory. Either works for
                // our purposes — we just need any instance whose private
                // multiDeckLayout field has been wired up.
                var panels = Resources.FindObjectsOfTypeAll<UIEquipDeckCardList>();
                if (panels != null && panels.Length > 0)
                    panel = panels[0];
            }
            catch
            {
                return;
            }
            if (panel == null)
                return;

            var saved = panel.currentunit;
            MultiDeckLabels.SetInSyntheticInvoke(true);
            try
            {
                panel.currentunit = unitData;
                SetDeckLayoutMethod.Invoke(panel, null);
            }
            catch
            {
                // Best-effort: if invoking the patch chain throws (e.g.
                // because some other mod's prefix/postfix can't handle
                // the synthetic invocation), the cache stays empty and we
                // retry on the next broadcast.
            }
            finally
            {
                panel.currentunit = saved;
                MultiDeckLabels.SetInSyntheticInvoke(false);
            }
        }

        /// <summary>Extracts the book's identity and forwards to <see cref="MultiDeckLabels.GetEffectiveDeckCount"/>.</summary>
        internal static int GetEffectiveDeckCount(BookModel book)
        {
            if (book == null)
                return 1;
            var lid = book.GetBookClassInfoId();
            return MultiDeckLabels.GetEffectiveDeckCount(lid.packageId, lid.id, book.IsMultiDeck());
        }

        /// <summary>
        /// Extracts the book's identity and forwards to <see cref="MultiDeckLabels.TryGetLabels"/>,
        /// supplying <c>TextDataModel.GetText</c> as the text-resolution delegate.
        /// </summary>
        internal static bool TryGetLabels(BookModel book, out string[] labels)
        {
            labels = null;
            if (book == null)
                return false;
            var lid = book.GetBookClassInfoId();
            return MultiDeckLabels.TryGetLabels(
                lid.packageId,
                lid.id,
                book.IsMultiDeck(),
                // TextDataModel.GetText(string, params object[]) doesn't convert to
                // Func<string, string> as a bare method group, hence the lambda.
                id => TextDataModel.GetText(id),
                out labels
            );
        }

        /// <summary>Extracts the book's identity and forwards to <see cref="MultiDeckLabels.RecordLabels"/>.</summary>
        internal static bool RecordLabels(BookModel book, string[] tabLabels)
        {
            if (book == null)
                return false;
            var lid = book.GetBookClassInfoId();
            return MultiDeckLabels.RecordLabels(lid.packageId, lid.id, tabLabels);
        }
    }
}
