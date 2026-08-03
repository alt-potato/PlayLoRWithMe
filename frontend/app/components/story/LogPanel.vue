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
        }"
      >
        <div v-if="visiblePortrait(entry)" class="slog-portrait">
          <img
            class="slog-portrait-img"
            :src="visiblePortrait(entry)!"
            alt=""
            @error="onPortraitError(entry)"
          />
        </div>
        <div class="slog-body">
          <p v-if="showsSpeaker(entry)" class="slog-name">
            <span v-if="entry.title" class="slog-title">{{ entry.title }}</span>
            <span class="slog-teller">{{ entry.teller }}</span>
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
  background: color-mix(in srgb, var(--bg-card-2) 94%, transparent);
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
  background: var(--border-mid);
}

.slog-portrait-img {
  width: calc(100% - var(--story-log-portrait-border) * 2);
  height: calc(100% - var(--story-log-portrait-border) * 2);
  object-fit: cover;
  clip-path: var(--hex-pointy);
  background: var(--bg-card-3);
}

.slog-body {
  min-width: 0;
  flex: 1;
}

.slog-name {
  display: flex;
  align-items: baseline;
  flex-wrap: wrap;
  gap: var(--sp-2);
  margin: 0 0 var(--sp-1);
}

/* Title before name and smaller, mirroring the in-game log's name line — which
   composes both from one string, so both share the display face. */
.slog-title {
  font-family: var(--font-display);
  font-size: var(--fs-4xs);
  color: var(--text-3);
  letter-spacing: 0.03em;
}

.slog-teller {
  font-family: var(--font-display);
  font-size: var(--fs-sm);
  color: var(--text-1);
  letter-spacing: 0.03em;
}

.slog-content {
  font-family: var(--font-body);
  font-size: var(--fs-sm);
  color: var(--text-2);
  line-height: 1.5;
  margin: 0;
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
