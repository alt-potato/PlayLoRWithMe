using System;

namespace PlayLoRWithMe
{
    /// <summary>
    /// Resolves localized deck labels for multi-deck key pages.
    ///
    /// Vanilla bakes four <see cref="UITextDataLoader"/> components onto
    /// the deck-editor prefab — one per tab — with hardcoded keys
    /// <c>ui_slash_form</c>, <c>ui_penetrate_form</c>, <c>ui_hit_form</c>,
    /// <c>ui_defense_form</c>. <see cref="TextDataModel.GetText"/> resolves
    /// each to the player's-language string (in English: Slash / Pierce /
    /// Blunt / Guard). The keys are universal across vanilla multi-deck
    /// books, since the deck-editor prefab is shared.
    ///
    /// Mods that want custom per-book labels (e.g. Binah's "Philosophy" /
    /// "Arbiter") accomplish that by Harmony-patching
    /// <c>UIEquipDeckCardList.SetDeckLayout</c> to overwrite the
    /// <c>TabName.text</c> on each tab button — the in-game UI then
    /// shows the patched strings. The wire labels we emit reflect the
    /// vanilla generic keys; books with mod-side label overrides will
    /// appear with the generic stance names in the web UI but the custom
    /// names in-game. Surfacing the patched strings would require reading
    /// runtime tab text after each mod's <c>SetDeckLayout</c> postfix
    /// runs, which we avoid as too prefab-state dependent.
    /// </summary>
    public static class MultiDeckLabels
    {
        // TextDataModel keys baked into the vanilla deck-editor prefab.
        // Order matches the in-game tab order which mirrors the
        // PurpleStance enum on PassiveAbility_250127 (Slash, Penetrate,
        // Hit, Defense).
        private static readonly string[] LabelKeys =
        {
            "ui_slash_form",
            "ui_penetrate_form",
            "ui_hit_form",
            "ui_defense_form",
        };

        // English fallback used when localization hasn't been loaded yet
        // (very early title-scene snapshots before TextDataModel.InitTextData
        // runs). Matches the values returned by GetText against the keys
        // above when language is "en".
        private static readonly string[] EnglishFallback =
            { "Slash", "Pierce", "Blunt", "Guard" };

        /// <summary>
        /// Resolves the four deck labels for the given book. Returns false
        /// for non-multi-deck books so the caller omits <c>label</c> from
        /// the wire payload. Multi-deck books always get four labels —
        /// localized via <see cref="TextDataModel"/> in normal play, English
        /// fallback if the dictionary hasn't loaded yet.
        /// </summary>
        public static bool TryGetLabels(BookModel book, out string[] labels)
        {
            labels = null;
            if (book == null || !book.IsMultiDeck())
                return false;

            var resolved = new string[4];
            for (int i = 0; i < 4; i++)
            {
                string text = null;
                try
                {
                    // GetText returns "" for unknown keys but throws
                    // NullReferenceException if its backing dictionary
                    // hasn't been initialised yet (very early startup).
                    text = TextDataModel.GetText(LabelKeys[i]);
                }
                catch
                {
                    // fall through to English fallback
                }
                if (string.IsNullOrEmpty(text))
                    text = EnglishFallback[i];
                resolved[i] = text;
            }
            labels = resolved;
            return true;
        }
    }
}
