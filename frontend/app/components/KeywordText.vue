<!--
  KeywordText.vue

  Inline renderer for combat-card description strings. Splits the input
  into plain + bracketed-keyword segments via `splitKeywordSegments`, then
  emits one `<span>` per segment, applying `.keyword` only to bracketed
  runs. Non-keyword spans are unstyled and inherit the parent's colour.

  Layout: purely inline — no block wrapper is produced, so this drops
  into any container (`<p>`, `<span>`) without affecting surrounding
  layout. Template is single-line to avoid any SFC whitespace being
  injected between adjacent segments.

  The `.keyword` colour intentionally overrides any parent colour tint
  (e.g. `.hcard-die-desc--atk`): keyword recognition outranks die-type
  tinting, which is already signalled redundantly by the die icon.
-->
<script setup lang="ts">
import { splitKeywordSegments } from "~/utils/keywordHighlight";

const props = defineProps<{ text: string }>();
const segments = computed(() => splitKeywordSegments(props.text));
</script>

<template><span v-for="(seg, i) in segments" :key="i" :class="{ keyword: seg.isKeyword }">{{ seg.text }}</span></template>

<style scoped>
.keyword {
  color: var(--gold-bright);
  font-weight: 600;
}
</style>
