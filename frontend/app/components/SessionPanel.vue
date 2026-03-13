<!--
  Claim/release panel for multi-player session management.

  Shows all allied librarians with their current owner (if any). Players can
  claim unclaimed librarians or release their own. Collapses to a header by
  default to keep the battlefield uncluttered.

  Props:
    allies  – all allied units from the current battle state
    players – connected player list from the server (playerList messages)
-->
<script setup lang="ts">
import type { BattleCtx } from "~/composables/useBattleContext";
import type { AllyUnit, PlayerInfo } from "~/types/game";

const props = defineProps<{
  allies: AllyUnit[];
  players: PlayerInfo[];
}>();

const { session, allyColors, claimUnit, releaseUnit } = inject(
  BATTLE_CTX,
) as BattleCtx;

const expanded = ref(false);

/** Look up which player (if any) currently claims a unit. */
function ownerOf(unitId: number): PlayerInfo | undefined {
  return props.players.find((p) => p.units.includes(unitId));
}

/** True if this session owns the given unit. */
function isMyUnit(unitId: number): boolean {
  return session.value?.assignedUnits.includes(unitId) ?? false;
}

const unclaimedCount = computed(
  () => props.allies.filter((a) => !ownerOf(a.id)).length,
);
</script>

<template>
  <div class="session-panel">
    <button class="session-toggle" @click="expanded = !expanded">
      <span class="toggle-label">Players</span>
      <span class="toggle-count">{{ players.length }}</span>
      <span v-if="unclaimedCount > 0" class="unclaimed-pip">
        {{ unclaimedCount }}
      </span>
      <span class="toggle-chevron" :class="{ open: expanded }">▸</span>
    </button>

    <Transition name="session-drop">
      <div v-if="expanded" class="session-body">
        <div class="body-header">
          <span class="body-title">Librarians</span>
        </div>

        <div
          v-for="ally in allies"
          :key="ally.id"
          class="librarian-row"
          :style="{ '--lc': allyColors[ally.id] ?? 'var(--gold-dim)' }"
        >
          <!-- Ally color stripe -->
          <span class="ally-stripe" />

          <span class="librarian-name">{{
            ally.name ?? ally.keyPage?.name ?? `Unit #${ally.id}`
          }}</span>

          <span
            v-if="ownerOf(ally.id) as PlayerInfo | undefined"
            class="owner-tag"
          >
            {{ (ownerOf(ally.id) as PlayerInfo).name }}
          </span>
          <span v-else class="owner-tag owner-tag--unclaimed">—</span>

          <button
            v-if="isMyUnit(ally.id)"
            class="action-btn action-btn--release"
            @click="releaseUnit(ally.id)"
          >
            Release
          </button>
          <button
            v-else-if="!ownerOf(ally.id)"
            class="action-btn"
            @click="claimUnit(ally.id)"
          >
            Claim
          </button>
          <span v-else class="action-placeholder" />
        </div>

        <div v-if="allies.length === 0" class="session-empty">
          No librarians in battle.
        </div>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
/* ── Toggle (lives inside teaser-bar, must be visually flush) ── */
.session-panel {
  position: relative;
}

.session-toggle {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  padding: 0.15rem 0.4rem;
  background: none;
  border: none;
  cursor: pointer;
  color: var(--text-2);
  font-family: var(--font-display);
  font-size: 0.58rem;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  transition: color 0.15s;
}

.session-toggle:hover {
  color: var(--text-1);
}

.toggle-label {
  color: inherit;
}

.toggle-count {
  color: var(--gold);
  font-size: 0.7rem;
  font-family: var(--font-body);
  font-style: normal;
  letter-spacing: 0;
}

/* Red pip for unclaimed count */
.unclaimed-pip {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 1rem;
  height: 1rem;
  background: var(--crimson);
  color: var(--text-red);
  font-size: 0.52rem;
  font-family: var(--font-body);
  letter-spacing: 0;
  padding: 0 0.2rem;
}

.toggle-chevron {
  color: var(--text-3);
  font-size: 0.55rem;
  display: inline-block;
  transition: transform 0.18s ease;
}

.toggle-chevron.open {
  transform: rotate(90deg);
}

/* ── Dropdown body ── */
.session-body {
  position: absolute;
  top: calc(100% + 0.35rem);
  right: 0;
  z-index: 100;
  min-width: 15rem;
  background: var(--bg-card);
  border: 1px solid var(--border-mid);
  border-top: 2px solid var(--gold-dim);
  display: flex;
  flex-direction: column;
}

.body-header {
  padding: 0.3rem 0.6rem 0.25rem;
  border-bottom: 1px solid var(--border);
}

.body-title {
  font-family: var(--font-display);
  font-size: 0.52rem;
  text-transform: uppercase;
  letter-spacing: 0.14em;
  color: var(--text-3);
}

/* ── Librarian rows ── */
.librarian-row {
  display: flex;
  align-items: center;
  gap: 0.45rem;
  padding: 0.28rem 0.6rem 0.28rem 0;
  border-bottom: 1px solid var(--border);
  transition: background 0.1s;
}

.librarian-row:last-child {
  border-bottom: none;
}

.librarian-row:hover {
  background: var(--bg-card-2);
}

/* Left color stripe, mirrors the unit card side border */
.ally-stripe {
  width: 2px;
  align-self: stretch;
  background: var(--lc);
  flex-shrink: 0;
  margin-left: 0;
}

.librarian-name {
  flex: 1;
  font-family: var(--font-body);
  font-size: 0.72rem;
  color: var(--text-1);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
}

.owner-tag {
  font-family: var(--font-body);
  font-size: 0.62rem;
  color: var(--gold);
  white-space: nowrap;
  letter-spacing: 0.02em;
}

.owner-tag--unclaimed {
  color: var(--text-3);
}

/* ── Action buttons (match ego-toggle style from DisplayCard) ── */
.action-btn {
  font-family: var(--font-display);
  font-size: 0.48rem;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  padding: 0.12rem 0.45rem;
  background: transparent;
  border: 1px solid var(--gold-dim);
  color: var(--gold);
  cursor: pointer;
  white-space: nowrap;
  transition:
    background 0.12s,
    color 0.12s,
    border-color 0.12s;
  flex-shrink: 0;
  margin-right: 0.5rem;
}

.action-btn:hover {
  background: rgba(201, 162, 39, 0.12);
  border-color: var(--gold);
}

.action-btn--release {
  border-color: var(--crimson);
  color: var(--text-red);
}

.action-btn--release:hover {
  background: var(--crimson-dim);
  border-color: var(--crimson-hi);
}

.action-placeholder {
  /* keeps row height stable when no button shown */
  display: inline-block;
  width: 3.2rem;
  margin-right: 0.5rem;
  flex-shrink: 0;
}

.session-empty {
  padding: 0.5rem 0.6rem;
  font-size: 0.68rem;
  color: var(--text-3);
  font-style: italic;
  font-family: var(--font-body);
}

/* ── Drop transition (mirrors hand-expand in DisplayCard) ── */
.session-drop-enter-active {
  transition:
    opacity 0.18s ease,
    transform 0.18s ease;
}

.session-drop-leave-active {
  transition:
    opacity 0.13s ease,
    transform 0.13s ease;
}

.session-drop-enter-from,
.session-drop-leave-to {
  opacity: 0;
  transform: translateY(-5px);
}
</style>
