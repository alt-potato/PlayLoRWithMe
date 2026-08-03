<!--
  Mirrors the game's cutscene dialogue log so players who aren't driving can read
  at their own pace while the host clicks through.

  Used on two surfaces from one component:
    - the `story` scene, as the whole view (`collapsible` false)
    - over the battle stage during a BattleStoryUI cutscene (`collapsible` true)

  Read-only by design: the host still owns cutscene playback, so there is no skip
  or advance control here.

  Parents gate on a non-empty `storyLog`, so this component never renders an empty
  state of its own.

  Props:
    entries     - the mirrored log, oldest first
    collapsible - render the overlay chrome with a show/hide toggle
-->
<script setup lang="ts">
import type { StoryLogEntry } from "~/types/game";

const props = withDefaults(
  defineProps<{
    entries: StoryLogEntry[];
    collapsible?: boolean;
  }>(),
  { collapsible: false },
);

const collapsed = ref(false);
const scrollEl = ref<HTMLElement | null>(null);

// Whether the reader is following the newest line. Tracked as state updated on
// scroll rather than measured when a line arrives, because by then the DOM has
// already grown and the reader's position would look stale.
const pinned = ref(true);

// Portrait slugs whose image failed to load, so those rows fall back to name-only
// instead of showing a broken-image box.
const failedPortraits = ref(new Set<string>());

function onScroll() {
  const el = scrollEl.value;
  if (el) pinned.value = isPinnedToBottom(el);
}

/** Portrait URL for a row, or null when it has none or its image already failed. */
function visiblePortrait(entry: StoryLogEntry): string | null {
  if (entry.portrait && failedPortraits.value.has(entry.portrait)) return null;
  return portraitUrl(entry);
}

function onPortraitError(entry: StoryLogEntry) {
  if (entry.portrait) failedPortraits.value.add(entry.portrait);
}

watch(
  () => props.entries.length,
  async () => {
    if (!pinned.value || collapsed.value) return;
    await nextTick();
    const el = scrollEl.value;
    if (el) el.scrollTop = el.scrollHeight;
  },
);
</script>

<template>
  <section class="slog" :class="{ 'slog--overlay': collapsible }">
    <header v-if="collapsible" class="slog-bar">
      <span class="slog-heading">Story log</span>
      <button type="button" class="slog-toggle" @click="collapsed = !collapsed">
        {{ collapsed ? "Show" : "Hide" }}
      </button>
    </header>

    <div
      v-show="!collapsed"
      ref="scrollEl"
      class="slog-scroll"
      @scroll.passive="onScroll"
    >
      <article
        v-for="(entry, index) in entries"
        :key="index"
        class="slog-row"
        :class="{
          'slog-row--choice': isChoiceEntry(entry),
          'slog-row--red': isChoiceEntry(entry) && entry.choiceIsRed,
          'slog-row--place': isPlaceEntry(entry),
        }"
      >
        <!--
          The frame stands whether or not an image loads: the in-game log disables
          only the portrait image and leaves its hex in place, and keeping it also
          holds the text column aligned down the list.
        -->
        <div v-if="showsPortraitFrame(entry)" class="slog-portrait">
          <div class="slog-portrait-frame">
            <img
              v-if="visiblePortrait(entry)"
              class="slog-portrait-img"
              :src="visiblePortrait(entry)!"
              alt=""
              @error="onPortraitError(entry)"
            />
          </div>
        </div>
        <div class="slog-body">
          <!--
            The name line is a reserved row, not a conditional one: monologue rows
            render nothing into it but still occupy it, so their content sits at the
            same height as every other row's, as it does in game.
          -->
          <p v-if="reservesNameRow(entry)" class="slog-name">
            <template v-if="showsSpeaker(entry)">
              <span v-if="entry.title" class="slog-title">{{ entry.title }}</span>
              <span class="slog-teller">{{ entry.teller }}</span>
            </template>
          </p>
          <p class="slog-content">{{ entry.content }}</p>
        </div>
      </article>
    </div>
  </section>
</template>

<style scoped>
.slog {
  /* Matches StoryManager.origintextdialogsize.x — the width of the game's normal
     dialogue box, which is what a player actually reads against. Deliberately not
     DialogLogManager.slotWidth (1500), which sizes the log-history rows.
     A ceiling only — below it the panel fills whatever width it is given, since
     reserving the game's proportional margin would waste space on a phone. */
  --story-log-max-width: 1277px;

  /* Pointy-top hexagons are taller than wide (1 : 2/sqrt(3)), so the portrait box
     is sized on both axes rather than kept square. */
  --story-log-portrait-w: 3rem;
  --story-log-portrait-h: 3.46rem;
  --story-log-portrait-border: 2px;

  /* Portrait art is a head-and-shoulders bust whose face centres at roughly a
     third of the image height, so plain `cover` frames the whole bust and lets
     the hex's point cut through the chest. Zooming past cover and biasing the
     crop upward puts the face in the frame instead.

     Tuned by eye against the extracted sprites: CharacterDialogLog only assigns
     the sprite, and the game's real framing lives in prefab data that cannot be
     decompiled — so these are approximations, kept as tokens to stay adjustable. */
  --story-log-portrait-zoom: 1.1;
  --story-log-portrait-offset-y: 10%;

  /* Comfortable reading measure for the dialogue itself. The panel cap alone does
     not deliver this: 1277px is near a typical laptop's viewport, so lines still
     ran the full width of the screen. Bounding the text column is what actually
     makes long passages readable. */
  --story-log-measure: 68ch;

  /* Bounds the scroller so it scrolls internally. Without a bound the panel grows
     to fit its content and the page scrolls instead, which silently breaks
     auto-scroll — scrollTop on a non-scrolling element does nothing. */
  --story-log-scroll-max-h: 68dvh;

  display: flex;
  flex-direction: column;
  width: 100%;
  max-width: var(--story-log-max-width);
  margin: 0 auto;
  min-height: 0;
}

/* Overlay variant: sits above the battle stage during a mid-battle cutscene. */
.slog--overlay {
  position: absolute;
  inset: 0 0 auto 0;
  z-index: 5;
  max-height: 60%;
  /* Fully opaque: a cutscene blocks combat input, so there is nothing useful to
     read through the panel. Collapsing it is the way to see the stage. */
  background: var(--bg-card-2);
  border-bottom: 1px solid var(--border-mid);
  box-shadow: var(--shadow-lg);
}

.slog-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--sp-2);
  padding: var(--sp-2) var(--sp-3);
  border-bottom: 1px solid var(--border-mid);
}

.slog-heading {
  font-family: var(--font-display);
  font-size: var(--fs-xs);
  letter-spacing: 0.06em;
  color: var(--text-2);
}

.slog-toggle {
  font-family: var(--font-body);
  font-size: var(--fs-2xs);
  color: var(--text-2);
  background: transparent;
  border: 1px solid var(--border-mid);
  padding: var(--sp-1) var(--sp-3);
  cursor: pointer;
  transition: background var(--duration-base) var(--ease-out);
}

.slog-toggle:hover {
  background: var(--bg-card-3);
}

.slog-scroll {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
  padding: var(--sp-3);
  display: flex;
  flex-direction: column;
  gap: var(--sp-3);
  /* Keeps the newest line in view as content grows, so the viewport stays stuck
     to the bottom without fighting the JS auto-scroll. */
  overflow-anchor: auto;
}

/* On the story scene the panel is a plain block child with no height to fill, so
   it needs its own bound to scroll internally rather than growing the page. The
   overlay variant already gets one from its own max-height. */
.slog:not(.slog--overlay) .slog-scroll {
  max-height: var(--story-log-scroll-max-h);
}

.slog-row {
  display: flex;
  gap: var(--sp-3);
  align-items: flex-start;
}

/* Pointy-top hexagonal frame, matching how the in-game log crops portraits.
   Two layers of clip-path fake the outline, because clip-path would cut away a
   real CSS border — the same technique DieRow uses for speed dice. */
.slog-portrait {
  width: var(--story-log-portrait-w);
  height: var(--story-log-portrait-h);
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  clip-path: var(--hex-pointy);
  /* Gold outline, matching the accent the base game uses on the log's hexes and
     the rest of this UI. */
  background: var(--gold);
}

/* Inner layer holds the fill and does the cropping. clip-path applies to the
   whole subtree, so the zoomed image is cut to the hexagon without needing the
   image itself to be clipped (which would scale the hexagon along with it). */
.slog-portrait-frame {
  width: calc(100% - var(--story-log-portrait-border) * 2);
  height: calc(100% - var(--story-log-portrait-border) * 2);
  clip-path: var(--hex-pointy);
  background: var(--bg-card-3);
  overflow: hidden;
}

.slog-portrait-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  /* Independent transforms: translate applies before scale, so the bias is
     magnified by the zoom — which is the intent, both push toward the face. */
  translate: 0 var(--story-log-portrait-offset-y);
  scale: var(--story-log-portrait-zoom);
}

.slog-body {
  min-width: 0;
  flex: 1;
}

/* Reserved row: monologue rows leave it empty but still occupy it, so min-height
   holds the slot open and keeps content aligned across rows.

   The base game separates this line with a trapezoid tabbed into the top of the
   hexagon. That shape depends on the name plate abutting the portrait, which it
   does not here — the name sits in a separate column — so the separator is a gold
   hairline rule along the line instead, which is the accent language this UI
   already uses elsewhere. */
.slog-name {
  display: flex;
  align-items: baseline;
  flex-wrap: wrap;
  gap: var(--sp-2);
  margin: 0 0 var(--sp-2);
  min-height: var(--fs-md);
  padding-bottom: var(--sp-1);
  border-bottom: 1px solid var(--gold-dim);
}

/* Title before name and smaller, mirroring the in-game log's name line — which
   composes both from one string, so both share a face and differ only in size.
   The reading serif rather than the display face: Cinzel's lowercase reads as
   small caps, which misrepresents character names. */
.slog-title {
  font-family: var(--font-serif);
  font-size: var(--fs-2xs);
  color: var(--text-3);
}

.slog-teller {
  font-family: var(--font-serif);
  font-size: var(--fs-md);
  font-weight: 600;
  color: var(--text-1);
}

/* Serif, matching the in-game dialogue face — the game renders story text in a
   serif and switches only the localized font, never to a sans. */
.slog-content {
  font-family: var(--font-serif);
  font-size: var(--fs-md);
  color: var(--text-2);
  line-height: 1.6;
  margin: 0;
  max-width: var(--story-log-measure);
  /* Long unbroken runs (URLs, CJK without spaces) must not force a horizontal
     scrollbar on the row. */
  overflow-wrap: break-word;
  /* preserve newlines from the game text; incidental whitespace still collapses */
  white-space: pre-line;
}

/* Choice outcomes are centred and accented, as the in-game log renders them. */
.slog-row--choice {
  justify-content: center;
}

.slog-row--choice .slog-body {
  flex: 0 1 auto;
  text-align: center;
}

.slog-row--choice .slog-content {
  font-style: italic;
  color: var(--blue-hi);
}

.slog-row--red .slog-content {
  color: var(--crimson-hi);
}

/* Place captions mark a change of location mid-episode. Rendered as a centred
   interstitial rule rather than a row, since they belong between lines rather
   than to any speaker. */
.slog-row--place {
  align-items: center;
  gap: var(--sp-3);
  margin: var(--sp-2) 0;
}

.slog-row--place::before,
.slog-row--place::after {
  content: "";
  flex: 1;
  height: 1px;
  background: var(--gold-dim);
}

.slog-row--place .slog-body {
  flex: 0 1 auto;
}

.slog-row--place .slog-content {
  font-family: var(--font-display);
  font-size: var(--fs-2xs);
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--gold);
  max-width: none;
  text-align: center;
}

@media (max-width: 600px) {
  .slog {
    --story-log-portrait-w: 2.25rem;
    --story-log-portrait-h: 2.6rem;
  }

  .slog--overlay {
    max-height: 70%;
  }
}
</style>
