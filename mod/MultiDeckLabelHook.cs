using System;
using System.Reflection;
using HarmonyLib;
using UI;
using UnityEngine;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Observes the deck-editor's tab strip after every
    /// <see cref="UIEquipDeckCardList.SetDeckLayout"/> call and records the
    /// resolved <c>TabName.text</c> for each visible tab into
    /// <see cref="MultiDeckLabels"/>. Runs at <see cref="Priority.Last"/> so
    /// it observes the final state of the tabs after any other Harmony
    /// patches in the load order have written their overrides — that lets
    /// us pick up mod-supplied labels (Binah Multi-Deck's "Philosophy" /
    /// "Arbiter", custom workshop key pages, etc.) that never round-trip
    /// through <see cref="TextDataModel"/>.
    ///
    /// Side-effect-free w.r.t. the editor: we read text only.
    /// </summary>
    [HarmonyPatch(typeof(UIEquipDeckCardList), "SetDeckLayout")]
    public static class MultiDeckLabelHook
    {
        // Reflected once at load time. SetDeckLayout is on the engine type
        // so the field reference is stable; resolving per-call is wasteful.
        private static readonly FieldInfo MultiDeckLayoutField =
            typeof(UIEquipDeckCardList).GetField(
                "multiDeckLayout",
                BindingFlags.Instance | BindingFlags.NonPublic);

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        // Binah Multi-Deck (Harmony id "Binah") deactivates unused tabs in
        // its postfix; without HarmonyAfter, Priority.Last alone has been
        // observed to read tab state before that deactivation lands.
        [HarmonyAfter(new[] { "Binah" })]
        public static void Postfix(UIEquipDeckCardList __instance)
        {
            try
            {
                var book = __instance?.currentunit?.bookItem;
                if (book == null || !book.IsMultiDeck())
                    return;

                if (MultiDeckLayoutField == null)
                    return;
                var layout = MultiDeckLayoutField.GetValue(__instance) as GameObject;
                if (layout == null)
                    return;

                // include-inactive=true so the array indexing matches deck
                // index — mods that hide unused tabs still leave the
                // GameObject in the hierarchy. We then null-out hidden ones
                // so the cached array reflects what's actually visible.
                var buttons = layout.GetComponentsInChildren<UICustomTabButton>(includeInactive: true);
                if (buttons == null || buttons.Length == 0)
                    return;

                var labels = new string[MultiDeckLabels.LabelCount];
                int upper = buttons.Length < labels.Length ? buttons.Length : labels.Length;
                for (int i = 0; i < upper; i++)
                {
                    var b = buttons[i];
                    if (b == null)
                        continue;
                    // activeSelf, not activeInHierarchy: the synthetic
                    // invoke fires while the parent UIEquipDeckCardList
                    // GameObject is typically inactive (player isn't
                    // viewing the in-game editor). activeInHierarchy
                    // would then be false for every tab regardless of
                    // how the patch chain set the individual buttons —
                    // we want the local intent ("did the post-patch
                    // deactivate this tab"), not the rendered state.
                    if (!b.gameObject.activeSelf)
                        continue;
                    var tn = b.TabName;
                    if (tn == null)
                        continue;
                    labels[i] = tn.text;
                }
                bool changed = MultiDeckLabels.RecordLabels(book, labels);
                // Broadcast on first observation (or any later change) so
                // open web-UI deck editors update without requiring a
                // close+reopen. Skip when we're inside a synthetic
                // EnsureLabelsCached invocation — the serializer that
                // triggered it is already preparing a broadcast and a
                // recursive fan-out would multiply it per multi-deck book.
                if (changed && !MultiDeckLabels.InSyntheticInvoke)
                    StateBroadcaster.Broadcast();
            }
            catch (Exception)
            {
                // Observation only. A malformed deck-editor prefab or an
                // unexpected mod patch must never break the editor — drop
                // the failure and let the static text-id fallback handle
                // the next serialization.
            }
        }
    }
}
