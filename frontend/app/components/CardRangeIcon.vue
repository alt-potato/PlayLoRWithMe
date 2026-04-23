<!--
  CardRangeIcon.vue

  Single-glyph display of a card's CardRange enum value. Renders a custom
  inline SVG for Near/Far/Instance/Special, the Unicode summation / for-all
  symbols for FarArea / FarAreaEach, and falls back to the raw range string
  for any unknown value.

  The original range string is always exposed via `title` (desktop hover) and
  `aria-label` (assistive tech) regardless of which glyph form is rendered.

  Props:
    range – CardRange enum string from game state
-->
<script setup lang="ts">
import { cardRangeIconDescriptor } from "~/utils/cardRangeGlyph";

const props = defineProps<{ range: string }>();

const descriptor = computed(() => cardRangeIconDescriptor(props.range));
</script>

<template>
  <span
    class="range-icon"
    :title="descriptor.label"
    :aria-label="descriptor.label"
    role="img"
  >
    <!-- inline svg glyphs -->
    <!--
      Near + Far share a 30° CCW rotation to match the in-game melee/ranged
      glyph orientation (tip / muzzle points up-and-left). Rotation is applied
      around the viewBox center (8, 8).
    -->
    <svg
      v-if="descriptor.glyph.kind === 'svg' && descriptor.glyph.id === 'sword'"
      class="range-svg"
      viewBox="0 0 16 16"
      aria-hidden="true"
    >
      <g transform="rotate(-30 8 8)">
        <path
          d="m6.75 4.5.375 6.875H5.875l-.375.875 1.875.875v2.5h1.25v-2.5l1.875-.875-.375-.875H8.875L9.25 4.5 8 1Zl.375 6.875h1.75L9.25 4.5 8 1"
          fill="none"
          stroke="currentColor"
          stroke-width="1.2"
          stroke-linejoin="round"
          stroke-linecap="round"
        />
      </g>
    </svg>
    <svg
      v-else-if="
        descriptor.glyph.kind === 'svg' && descriptor.glyph.id === 'gun'
      "
      class="range-svg"
      viewBox="0 0 16 16"
      aria-hidden="true"
    >
      <g transform="rotate(-30 8 8)">
        <path
          d="M7 10c0-.5 0-1.5 1-2m0 1.5-1 .5-1.5 4.5 2.5.5.5-4.5.5-.3V.7l.2-.2H8Z"
          fill="none"
          stroke="currentColor"
          stroke-width="1.1"
          stroke-linejoin="round"
          stroke-linecap="round"
        />
      </g>
    </svg>
    <svg
      v-else-if="
        descriptor.glyph.kind === 'svg' &&
        descriptor.glyph.id === 'triangle-bolt'
      "
      class="range-svg"
      viewBox="0 0 16 16"
      aria-hidden="true"
    >
      <!-- inverted triangle with a horizontal lightning zigzag, both outline-only -->
      <path
        d="M 2.5 3 L 13.5 3 L 8 13 Z"
        fill="none"
        stroke="currentColor"
        stroke-width="1.3"
        stroke-linejoin="round"
      />
      <path
        d="M 3 7.5 L 8 5.5 L 7 8 L 13 7 L 8 10 L 10 7.5 Z"
        fill="none"
        stroke="currentColor"
        stroke-width="1"
        stroke-linejoin="round"
        stroke-linecap="round"
      />
    </svg>
    <svg
      v-else-if="
        descriptor.glyph.kind === 'svg' && descriptor.glyph.id === 'sword-plus'
      "
      class="range-svg"
      viewBox="0 0 16 16"
      aria-hidden="true"
    >
      <!-- wireframe sword shifted left, plus glyph in upper-right corner -->
      <g transform="rotate(-30 8 8)">
        <path
          d="m6.75 4.5.375 6.875H5.875l-.375.875 1.875.875v2.5h1.25v-2.5l1.875-.875-.375-.875H8.875L9.25 4.5 8 1Zl.375 6.875h1.75L9.25 4.5 8 1"
          fill="none"
          stroke="currentColor"
          stroke-width="1.2"
          stroke-linejoin="round"
          stroke-linecap="round"
        />
      </g>
      <path
        d="M12 1V5M10 3h4"
        fill="none"
        stroke="currentColor"
        stroke-width="1"
        stroke-linejoin="round"
        stroke-linecap="round"
      />
    </svg>

    <!-- unicode glyphs (FarArea, FarAreaEach) -->
    <span
      v-else-if="descriptor.glyph.kind === 'unicode'"
      class="range-unicode"
      aria-hidden="true"
      >{{ descriptor.glyph.symbol }}</span
    >

    <!-- fallback for unknown ranges -->
    <span v-else class="range-fallback" aria-hidden="true">{{
      descriptor.label
    }}</span>
  </span>
</template>

<style scoped>
.range-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--gold);
  line-height: 1;
  flex-shrink: 0;
}

.range-svg {
  width: 0.9em;
  height: 0.9em;
  display: block;
}

.range-unicode {
  font-family: var(--font-body);
  font-size: 0.9em;
  font-weight: 600;
  line-height: 1;
}

.range-fallback {
  font-family: var(--font-body);
  font-size: 0.44rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-3);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
