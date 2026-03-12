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
      <span class="session-toggle-label">
        Players ({{ players.length }})
        <span v-if="unclaimedCount > 0" class="unclaimed-badge">
          {{ unclaimedCount }} unclaimed
        </span>
      </span>
      <span class="session-chevron">{{ expanded ? "▾" : "▸" }}</span>
    </button>

    <Transition name="session-expand">
      <div v-if="expanded" class="session-body">
        <!-- Librarian rows -->
        <div
          v-for="ally in allies"
          :key="ally.id"
          class="librarian-row"
          :style="{ '--lc': allyColors[ally.id] ?? 'var(--gold-dim)' }"
        >
          <span class="librarian-dot" />
          <span class="librarian-name">{{
            ally.name ?? ally.keyPage?.name ?? `Unit #${ally.id}`
          }}</span>

          <span
            v-if="ownerOf(ally.id) as PlayerInfo | undefined"
            class="owner-tag"
          >
            {{ (ownerOf(ally.id) as PlayerInfo).name }}
          </span>
          <span v-else class="owner-tag owner-tag--unclaimed">unclaimed</span>

          <button
            v-if="isMyUnit(ally.id)"
            class="claim-btn claim-btn--release"
            @click="releaseUnit(ally.id)"
          >
            Release
          </button>
          <button
            v-else-if="!ownerOf(ally.id)"
            class="claim-btn"
            @click="claimUnit(ally.id)"
          >
            Claim
          </button>
        </div>

        <!-- Connected players not assigned to any unit -->
        <div v-if="players.length === 0" class="session-empty">
          No other players connected.
        </div>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.session-panel {
  position: relative;
  background: var(--bg-mid, #1a1a1a);
  border-bottom: 1px solid var(--gold-dim, #4a3f28);
  font-size: 0.8rem;
}

.session-toggle {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  width: 100%;
  padding: 0.3rem 0.75rem;
  background: none;
  border: none;
  color: var(--gold, #c9a227);
  cursor: pointer;
  font: inherit;
  font-size: 0.8rem;
  text-align: left;
}

.session-toggle:hover {
  background: rgba(201, 162, 39, 0.08);
}

.session-toggle-label {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.unclaimed-badge {
  background: var(--crimson, #8b0000);
  color: #fff;
  font-size: 0.7rem;
  padding: 0.1rem 0.4rem;
  border-radius: 3px;
}

.session-chevron {
  color: var(--gold-dim, #4a3f28);
}

.session-body {
  position: absolute;
  top: 100%;
  right: 0;
  z-index: 100;
  min-width: 16rem;
  padding: 0.25rem 0.75rem 0.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  background: var(--bg-mid, #1a1a1a);
  border: 1px solid var(--gold-dim, #4a3f28);
}

.librarian-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.librarian-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--lc);
  flex-shrink: 0;
}

.librarian-name {
  flex: 1;
  color: var(--text, #ccc);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.owner-tag {
  color: var(--gold, #c9a227);
  font-size: 0.75rem;
  white-space: nowrap;
}

.owner-tag--unclaimed {
  color: var(--text-dim, #666);
  font-style: italic;
}

.claim-btn {
  padding: 0.15rem 0.5rem;
  border: 1px solid var(--gold-dim, #4a3f28);
  background: none;
  color: var(--gold, #c9a227);
  cursor: pointer;
  font: inherit;
  font-size: 0.75rem;
  border-radius: 3px;
  white-space: nowrap;
}

.claim-btn:hover {
  background: rgba(201, 162, 39, 0.15);
}

.claim-btn--release {
  border-color: var(--crimson, #8b0000);
  color: #e57373;
}

.claim-btn--release:hover {
  background: rgba(139, 0, 0, 0.15);
}

.session-empty {
  color: var(--text-dim, #666);
  font-style: italic;
  padding: 0.25rem 0;
}

.session-expand-enter-active,
.session-expand-leave-active {
  transition: opacity 0.15s ease;
}

.session-expand-enter-from,
.session-expand-leave-to {
  opacity: 0;
}
</style>
