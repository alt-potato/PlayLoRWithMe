<!--
  HandCard.vue

  Renders a single battle card as a small interactive tile.
  Owns all card face rendering; interaction state (selected/dimmed) is driven
  by the parent via props.

  Props:
    card     – card object from game state (name, cost, range, rarity, dice[], …)
    selected – true when this card is the active selection
    dimmed   – true when another card is selected (this one fades out)
    color    – ally color hex used for glow when selected

  Emits:
    click – user tapped the card
-->
<script setup lang="ts">
const props = defineProps<{
  card: any
  selected?: boolean
  dimmed?: boolean
  color?: string
}>()

defineEmits<{ click: [] }>()
</script>

<template>
  <div
    class="hcard"
    :class="{
      'hcard--selected': selected,
      'hcard--dim': dimmed,
    }"
    :style="selected && color ? { borderColor: color, '--glow': color + '44' } : {}"
    @click="$emit('click')"
  >
    <span class="hcard-cost">{{ card.cost }}</span>
    <span class="hcard-name">{{ card.name }}</span>
    <span class="hcard-range">{{ card.range }}</span>
  </div>
</template>

<style scoped>
.hcard {
  flex-shrink: 0;
  width: 3.8rem;
  min-height: 5.2rem;
  background: linear-gradient(160deg, var(--bg-card-2) 0%, var(--bg-card-3) 100%);
  border: 1px solid var(--border-mid);
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 0.2rem 0.15rem 0.15rem;
  gap: 0.15rem;
  cursor: pointer;
  position: relative;
  touch-action: manipulation;
  transition: transform 0.1s, border-color 0.12s, box-shadow 0.12s;
  user-select: none;
  -webkit-user-select: none;
}
.hcard:hover {
  transform: translateY(-3px);
  border-color: var(--border-hi);
}
.hcard--selected {
  transform: translateY(-5px);
  box-shadow: 0 4px 16px var(--glow, rgba(201, 162, 39, 0.25));
}
.hcard--dim {
  opacity: 0.28;
  pointer-events: none;
}

.hcard-cost {
  width: 1.25rem;
  height: 1.25rem;
  background: var(--gold-dim);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.72rem;
  color: var(--gold-bright);
  font-family: var(--font-mono);
  font-weight: bold;
  flex-shrink: 0;
  align-self: flex-start;
}
.hcard-name {
  flex: 1;
  font-size: 0.58rem;
  color: var(--text-1);
  font-family: var(--font-body);
  text-align: center;
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 4;
  line-clamp: 4;
  -webkit-box-orient: vertical;
  line-height: 1.3;
  width: 100%;
}
.hcard-range {
  font-size: 0.5rem;
  color: var(--text-2);
  font-family: var(--font-mono);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  align-self: flex-end;
}
</style>
