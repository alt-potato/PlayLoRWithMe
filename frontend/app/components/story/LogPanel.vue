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

    <!--
      The scroller spans the full width so its scrollbar rides the page edge; the
      column inside it is what bounds the reading measure.
    -->
    <div v-show="!collapsed" ref="scrollEl" class="slog-scroll" @scroll.passive="onScroll">
      <div class="slog-inner">
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
            <p
              v-if="reservesNameRow(entry)"
              class="slog-name"
              :class="{ 'slog-name--empty': !showsSpeaker(entry) }"
            >
              <template v-if="showsSpeaker(entry)">
                <span v-if="entry.title" class="slog-title">{{ entry.title }}</span>
                <span class="slog-teller">{{ entry.teller }}</span>
              </template>
            </p>
            <p class="slog-content">{{ entry.content }}</p>
          </div>
        </article>
      </div>
    </div>
  </section>
</template>

<style scoped>
.slog {
  /* Pointy-top hexagons are taller than wide (1 : 2/sqrt(3)), so the portrait box
     is sized on both axes rather than kept square. */
  --story-log-portrait-w: 3rem;
  --story-log-portrait-h: 3.46rem;
  --story-log-portrait-border: 2px;

  /* Gap between the portrait and the text column. Named because the name-line
     rule has to reach back across it to meet the hexagon. */
  --story-log-portrait-gap: var(--sp-3);

  /* Portrait art is a head-and-shoulders bust whose face centres at roughly a
     third of the image height, so plain `cover` frames the whole bust and lets
     the hex's point cut through the chest. Zooming past cover and biasing the
     crop upward puts the face in the frame instead.

     Tuned by eye against the extracted sprites: CharacterDialogLog only assigns
     the sprite, and the game's real framing lives in prefab data that cannot be
     decompiled — so these are approximations, kept as tokens to stay adjustable. */
  --story-log-portrait-zoom: 1.1;
  --story-log-portrait-offset-x: 5%;
  --story-log-portrait-offset-y: 10%;

  /* Comfortable reading measure for the dialogue. This, not the game's dialogue
     box width, is what bounds the layout: 1277px sits near a typical laptop
     viewport and so never actually constrained a line. */
  --story-log-measure: 66ch;

  /* The column is sized to exactly what a row needs, so the text fills it edge to
     edge instead of trailing off inside a much wider panel. */
  --story-log-column: calc(
    var(--story-log-measure) + var(--story-log-portrait-w) +
      var(--story-log-portrait-gap)
  );

  display: flex;
  flex-direction: column;
  width: 100%;
  min-height: 0;
  /* Set here, not just on the text, so the `ch` unit in --story-log-measure
     resolves against the same font the dialogue is set in. Left on the inherited
     sans, the column would be sized from one font's advance width and the text
     bounded by another's, and the two would not line up. */
  font-family: var(--font-serif);
}

/* Fills the height `main.main--fill` hands it, so the log runs to the bottom of
   the page and scrolls internally rather than growing the document. */
.slog:not(.slog--overlay) {
  flex: 1;
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
  /* Keeps the newest line in view as content grows, so the viewport stays stuck
     to the bottom without fighting the JS auto-scroll. */
  overflow-anchor: auto;
}

/* The reading column. Centred inside the full-width scroller, which is what puts
   the scrollbar at the page edge rather than alongside the text. */
.slog-inner {
  width: 100%;
  max-width: var(--story-log-column);
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: var(--sp-3);
}

.slog-row {
  display: flex;
  gap: var(--story-log-portrait-gap);
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
  display: flex;
  align-items: center;
  justify-content: center;
}

/* Scaled to the frame's height, never its width.
   The source art is tightly cropped per character, so native sizes differ a lot
   (roughly 0.70 to 1.03 aspect). `cover` therefore scaled narrow portraits by
   width to fill the frame, zooming those heads well past the wide ones'. Head
   size tracks bust height, so fitting height alone keeps every character at a
   consistent scale; narrow portraits simply leave the hexagon's flanks empty and
   wide ones overflow into the clip. */
.slog-portrait-img {
  height: 100%;
  width: auto;
  max-width: none;
  flex: none;
  /* Independent transforms: translate applies before scale, so the bias is
     magnified by the zoom — which is the intent, both push toward the face. */
  translate: var(--story-log-portrait-offset-x) var(--story-log-portrait-offset-y);
  scale: var(--story-log-portrait-zoom);
}

.slog-body {
  min-width: 0;
  flex: 1;
}

/* Reserved row: monologue rows leave it empty but still occupy it, so min-height
   holds the slot open and keeps content aligned across rows.

   The base game separates this line with a trapezoid tabbed into the top of the
   hexagon. A gold hairline stands in for it: same accent language, and it still
   springs from the hexagon's edge and ends with the name, so it reads as part of
   the name plate rather than ruling off the passage below. */
.slog-name {
  display: flex;
  align-items: baseline;
  flex-wrap: wrap;
  gap: var(--sp-2);
  min-height: var(--fs-md);
  padding-bottom: var(--sp-1);
  border-bottom: 1px solid var(--gold-dim);

  /* Reaches back across the row gap so the rule meets the flat right edge of the
     hexagon, and stops at the end of the name so it underlines the speaker rather
     than ruling off the whole passage. */
  margin: 0 0 var(--sp-2) calc(-1 * var(--story-log-portrait-gap));
  padding-left: var(--story-log-portrait-gap);
  width: fit-content;
  max-width: 100%;
}

/* Monologue rows keep the reserved height but drop the rule — there is no name
   for it to underline. Transparent rather than `none` so the box does not shift. */
.slog-name--empty {
  border-bottom-color: transparent;
}

/* Title before name and smaller, mirroring the in-game log's name line — which
   composes both from one string, so both share a face and differ only in size.
   The reading serif rather than the display face: Cinzel's lowercase reads as
   small caps, which misrepresents character names. */
.slog-title {
  font-family: var(--font-serif);
  font-size: var(--fs-2xs);
  /* Same colour as the name: the game composes both from one Text component and
     varies only the size tag, so size is the whole of the distinction. */
  color: var(--text-1);
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
