<!--
  BattleView.vue

  Top-level battle scene coordinator. Owns all interactive state and provides it
  to EnemyUnit and AllyUnit via BATTLE_CTX (provide/inject), avoiding prop drilling.

  Renders:
    - Stage bar (floor / chapter / wave / round / phase / confirm button)
    - Status banner (error only — targeting is now handled by TargetPicker)
    - Two-column battlefield grid (enemies left, allies right)
    - TargetPicker overlay when a non-Instance card slot has been chosen

  Props:
    state – full battle state snapshot from the SSE stream
-->
<script setup lang="ts">
import type { BattleCtx } from '~/composables/useBattleContext'

const props = defineProps<{ state: any }>()

// ---------------------------------------------------------------------------
// Interactive state (only meaningful during SelectCard phase)
// ---------------------------------------------------------------------------

/** True when the stage is in the card-assignment phase. */
const isSelectPhase = computed(() => props.state?.phase === 'ApplyLibrarianCardPhase')

/**
 * Card-first flow: set when a hand card is tapped; cleared when a slot is
 * chosen or the user cancels.
 */
const selectingSlotFor = ref<{ unitId: number; cardIndex: number } | null>(null)

/**
 * Targeting flow: set after a non-Instance card's slot is chosen; the player
 * must now pick a target speed die via TargetPicker.
 */
const selectingTargetFor = ref<{
  unitId: number; cardIndex: number; diceSlot: number;
  cardName: string; cardRange: string;
} | null>(null)

/** Last action error message, shown in the status banner. */
const actionError = ref<string | null>(null)

// Reset all selection state on every phase change.
watch(() => props.state?.phase, () => {
  selectingSlotFor.value = null
  selectingTargetFor.value = null
  actionError.value = null
})

// ---------------------------------------------------------------------------
// Derived display data
// ---------------------------------------------------------------------------

const ALLY_COLORS = ['#4fc3f7', '#81c784', '#ffb74d', '#ce93d8', '#f48fb1']

const allyColors = computed<Record<number, string>>(() => {
  const m: Record<number, string> = {}
  ;(props.state?.allies ?? []).forEach((a: any, i: number) => {
    m[a.id] = ALLY_COLORS[i % ALLY_COLORS.length]
  })
  return m
})

/** Faction-agnostic attack map: covers ally→enemy, enemy→enemy, etc. */
const attackMap = computed(() => {
  const m: Record<number, Record<number, Array<{ name: string; color: string; range: string }>>> = {}
  const allSides = [...(props.state?.allies ?? []), ...(props.state?.enemies ?? [])]
  allSides.forEach((unit: any, i: number) => {
    const color = ALLY_COLORS[i % ALLY_COLORS.length]
    const name = unit.name ?? unit.keyPage?.name ?? `#${unit.id}`
    ;(unit.slottedCards ?? []).forEach((sc: any) => {
      if (sc.targetUnitId == null) return
      ;(m[sc.targetUnitId] ??= {})[sc.targetSlot] ??= []
      m[sc.targetUnitId][sc.targetSlot].push({ name, color, range: sc.range ?? '' })
    })
  })
  return m
})

const allUnits = computed(() => [
  ...(props.state?.allies ?? []),
  ...(props.state?.enemies ?? []),
])

// ---------------------------------------------------------------------------
// Action senders
// ---------------------------------------------------------------------------

async function sendAction(action: object): Promise<boolean> {
  actionError.value = null
  try {
    const res = await fetch('/action', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(action),
    })
    if (!res.ok) { actionError.value = `Server error ${res.status}`; return false }
    return true
  } catch (e) {
    actionError.value = String(e)
    return false
  }
}

// ---------------------------------------------------------------------------
// Interaction handlers (provided to child components via BATTLE_CTX)
// ---------------------------------------------------------------------------

/** Toggle hand-card selection; tapping the same card again cancels. */
function onCardClick(unitId: number, cardIndex: number) {
  if (selectingSlotFor.value?.unitId === unitId && selectingSlotFor.value?.cardIndex === cardIndex) {
    selectingSlotFor.value = null
  } else {
    selectingSlotFor.value = { unitId, cardIndex }
    selectingTargetFor.value = null
  }
}

/**
 * Player chose a dice slot for the selected hand card.
 * Instance-range cards need no target; all others open TargetPicker.
 */
async function onSlotClick(unit: any, cardIndex: number, diceSlot: number) {
  selectingSlotFor.value = null
  const card = unit.hand?.[cardIndex]
  if (card?.range === 'Instance') {
    await sendAction({ type: 'playCard', unitId: unit.id, cardIndex, diceSlot })
  } else {
    selectingTargetFor.value = {
      unitId: unit.id, cardIndex, diceSlot,
      cardName: card?.name ?? '?',
      cardRange: card?.range ?? '',
    }
  }
}

/** Player picked a target die in the TargetPicker. */
async function onTargetDieClick(targetUnitId: number, targetDiceSlot: number) {
  if (!selectingTargetFor.value) return
  const { unitId, cardIndex, diceSlot } = selectingTargetFor.value
  const ok = await sendAction({ type: 'playCard', unitId, cardIndex, diceSlot, targetUnitId, targetDiceSlot })
  if (ok) selectingTargetFor.value = null
}

/** Clear both selection states (e.g. TargetPicker cancel). */
function cancelTargeting() {
  selectingTargetFor.value = null
  selectingSlotFor.value = null
}

/** Return a slotted card to the unit's hand. */
async function onRemoveCard(unitId: number, diceSlot: number) {
  await sendAction({ type: 'removeCard', unitId, diceSlot })
  if (selectingTargetFor.value?.unitId === unitId && selectingTargetFor.value?.diceSlot === diceSlot)
    selectingTargetFor.value = null
  if (selectingSlotFor.value?.unitId === unitId)
    selectingSlotFor.value = null
}

async function onConfirm() {
  await sendAction({ type: 'confirm' })
  selectingSlotFor.value = null
  selectingTargetFor.value = null
}

// ---------------------------------------------------------------------------
// Provide context to EnemyUnit and AllyUnit
// ---------------------------------------------------------------------------

// BATTLE_CTX is auto-imported from useBattleContext.ts
provide(BATTLE_CTX, {
  isSelectPhase,
  selectingSlotFor,
  selectingTargetFor,
  onCardClick,
  onSlotClick,
  onTargetDieClick,
  onRemoveCard,
  allyColors,
  attackMap,
  allUnits,
} satisfies BattleCtx)
</script>

<template>
  <!-- Stage bar -->
  <div class="stage-bar">
    <span class="stage-item">Floor <strong>{{ state.stage?.floor }}</strong></span>
    <span class="stage-item">Ch <strong>{{ state.stage?.chapter }}</strong></span>
    <span class="stage-item">Wave <strong>{{ state.stage?.wave }}</strong></span>
    <span class="stage-item">Round <strong>{{ state.stage?.round }}</strong></span>
    <span class="stage-sep"/>
    <span class="phase">{{ state.phase }}</span>
    <button v-if="isSelectPhase" class="confirm-btn" @click="onConfirm">Confirm</button>
  </div>

  <!-- Error banner -->
  <div v-if="actionError" class="banner error">{{ actionError }}</div>

  <!-- Battlefield: enemies left, allies right -->
  <div class="battlefield">
    <section class="side enemies">
      <h2>Enemies</h2>
      <EnemyUnit v-for="unit in state.enemies" :key="unit.id" :unit="unit" />
    </section>

    <section class="side allies">
      <h2>Allies</h2>
      <AllyUnit v-for="unit in state.allies" :key="unit.id" :unit="unit" />
    </section>
  </div>

  <!-- Target picker overlay -->
  <TargetPicker
    v-if="selectingTargetFor"
    :selecting="selectingTargetFor"
    :enemies="state.enemies ?? []"
    @pick="onTargetDieClick"
    @cancel="cancelTargeting"
  />
</template>

<style scoped>
/* Stage bar */
.stage-bar {
  display: flex; align-items: center; gap: 1rem;
  padding: 0.4rem 0.75rem;
  background: #16162a; border: 1px solid #2a2a4a; border-radius: 4px;
  margin-bottom: 0.75rem; font-size: 0.8rem;
}
.stage-item { color: #888; }
.stage-item strong { color: #c9a227; }
.stage-sep { flex: 1; }
.phase { color: #aaa; font-size: 0.75rem; font-style: italic; }
.confirm-btn {
  padding: 0.2rem 0.7rem;
  background: #1a3a1a; border: 1px solid #2e7d32; border-radius: 3px;
  color: #4caf50; font-size: 0.75rem; cursor: pointer; font-family: monospace;
}
.confirm-btn:hover { background: #2e7d32; color: #fff; }

/* Error banner */
.banner {
  display: flex; align-items: center; gap: 0.5rem;
  padding: 0.3rem 0.75rem; border-radius: 4px;
  font-size: 0.75rem; margin-bottom: 0.5rem;
}
.banner.error { background: #2a0a0a; border: 1px solid #6a1a1a; color: #ef9a9a; }

/* Battlefield layout */
.battlefield {
  display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; align-items: start;
}
.side { display: flex; flex-direction: column; gap: 0.5rem; }
.side h2 { font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.1em; margin-bottom: 0.5rem; }
.allies h2 { color: #c9a227; }
.enemies h2 { color: #c62828; }
</style>
