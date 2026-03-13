<!--
  Session/player panel that lives in the status bar.

  Shows two tabs via a segmented button:
  - Players — connected players and which units they've claimed
  - Librarians — claim/release controls per allied unit (battle only)

  The Librarians tab is hidden when showLibrarians is false (i.e. during the
  pre-battle BattleSetting screen), since there are no units to claim yet.

  Props:
    allies         – allied units (used for name lookup and librarian rows)
    players        – connected player list
    session        – current session identity
    allyColors     – unitId → hex color map
    showLibrarians – show the Librarians tab (true in battle, false in BattleSetting)
    claimUnit      – claim a unit by ID
    releaseUnit    – release a unit by ID
-->
<script setup lang="ts">
import type {
  AllyUnit,
  PlayerInfo,
  SessionState,
  ActionResult,
} from "~/types/game";

const props = defineProps<{
  allies: AllyUnit[];
  players: PlayerInfo[];
  session: SessionState | null;
  allyColors: Record<number, string>;
  showLibrarians: boolean;
  claimUnit: (unitId: number) => Promise<ActionResult>;
  releaseUnit: (unitId: number) => Promise<ActionResult>;
  renamePlayer: (name: string) => Promise<ActionResult>;
}>();

// ── Rename state ─────────────────────────────────────────────────────────────

const editingName = ref(false);
const nameInput = ref("");

function startRename() {
  const me = props.players.find((p) => isMe(p));
  nameInput.value = me?.name ?? "";
  editingName.value = true;
  nextTick(() => {
    (document.querySelector(".rename-input") as HTMLInputElement)?.select();
  });
}

async function commitRename() {
  const trimmed = nameInput.value.trim();
  if (trimmed) await props.renamePlayer(trimmed);
  editingName.value = false;
}

function cancelRename() {
  editingName.value = false;
}

const expanded = ref(false);
const activeTab = ref<"players" | "librarians">("players");

// Reset to Players tab when leaving battle (Librarians tab becomes unavailable).
watch(
  () => props.showLibrarians,
  (val) => {
    if (!val) activeTab.value = "players";
  },
);

/** Look up which player (if any) currently claims a unit. */
function ownerOf(unitId: number): PlayerInfo | undefined {
  return props.players.find((p) => p.units.includes(unitId));
}

/** True if this session owns the given unit. */
function isMyUnit(unitId: number): boolean {
  return props.session?.assignedUnits.includes(unitId) ?? false;
}

/** True if the given player is this session. */
function isMe(p: PlayerInfo): boolean {
  return p.sessionId === props.session?.sessionId;
}

const unclaimedCount = computed(
  () => props.allies.filter((a) => !ownerOf(a.id)).length,
);
</script>

<template>
  <div class="session-panel">
    <button class="session-toggle" @click="expanded = !expanded">
      <span class="toggle-chevron" :class="{ open: expanded }">▸</span>
      <span class="toggle-label">Players</span>
      <span class="toggle-count">{{ players.length }}</span>
      <span
        v-if="session?.claimsEnabled && unclaimedCount > 0"
        class="unclaimed-pip"
      >
        {{ unclaimedCount }}
      </span>
    </button>

    <Transition name="session-drop">
      <div v-if="expanded" class="session-body">
        <!-- Segmented tab control — only shown in battle where both tabs exist -->
        <div v-if="showLibrarians" class="tab-bar">
          <button
            class="tab-btn"
            :class="{ active: activeTab === 'players' }"
            @click="activeTab = 'players'"
          >
            Players
          </button>
          <button
            class="tab-btn"
            :class="{ active: activeTab === 'librarians' }"
            @click="activeTab = 'librarians'"
          >
            Librarians
          </button>
        </div>

        <!-- Players tab -->
        <template v-if="!showLibrarians || activeTab === 'players'">
          <div
            v-for="p in players"
            :key="p.sessionId"
            class="player-row"
            :class="{ 'player-row--me': isMe(p) }"
          >
            <template v-if="isMe(p) && editingName">
              <input
                v-model="nameInput"
                class="rename-input"
                maxlength="20"
                @keydown.enter="commitRename"
                @keydown.escape="cancelRename"
                @blur="commitRename"
              />
            </template>
            <template v-else>
              <span class="player-name">{{ p.name }}</span>
              <span v-if="isMe(p)" class="player-you">you</span>
              <button
                v-if="isMe(p)"
                class="rename-btn"
                title="Rename"
                @click.stop="startRename"
              >
                ✎
              </button>
            </template>
          </div>
          <div v-if="players.length === 0" class="session-empty">
            No players connected.
          </div>
        </template>

        <!-- Librarians tab (battle only) -->
        <template v-if="showLibrarians && activeTab === 'librarians'">
          <div
            v-for="ally in allies"
            :key="ally.id"
            class="librarian-row"
            :style="{ '--lc': allyColors[ally.id] ?? 'var(--gold-dim)' }"
          >
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
        </template>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
/* ── Toggle ── */
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
  letter-spacing: 0;
}

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
  min-width: 16rem;
  background: var(--bg-card);
  border: 1px solid var(--border-mid);
  border-top: 2px solid var(--gold-dim);
  display: flex;
  flex-direction: column;
}

/* ── Segmented tab bar ── */
.tab-bar {
  display: flex;
  border-bottom: 1px solid var(--border);
}

.tab-btn {
  flex: 1;
  padding: 0.3rem 0.5rem;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  cursor: pointer;
  font-family: var(--font-display);
  font-size: 0.5rem;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--text-3);
  transition:
    color 0.15s,
    border-color 0.15s;
}

.tab-btn:hover {
  color: var(--text-2);
}

.tab-btn.active {
  color: var(--gold);
  border-bottom-color: var(--gold);
}

/* ── Player rows ── */
.player-row {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.28rem 0.6rem;
  border-bottom: 1px solid var(--border);
  transition: background 0.1s;
}

.player-row:last-child {
  border-bottom: none;
}

.player-row:hover {
  background: var(--bg-card-2);
}

.player-row--me .player-name {
  color: var(--gold);
}

.player-name {
  font-family: var(--font-body);
  font-size: 0.72rem;
  color: var(--text-1);
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.player-you {
  font-family: var(--font-display);
  font-size: 0.46rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--gold-dim);
  flex-shrink: 0;
}

.rename-btn {
  background: none;
  border: none;
  color: var(--text-3);
  font-size: 0.7rem;
  cursor: pointer;
  padding: 0 0.1rem;
  line-height: 1;
  flex-shrink: 0;
  transition: color 0.12s;
}

.rename-btn:hover {
  color: var(--gold);
}

.rename-input {
  flex: 1;
  min-width: 0;
  background: var(--bg-card-3);
  border: 1px solid var(--gold-dim);
  color: var(--gold);
  font-family: var(--font-body);
  font-size: 0.72rem;
  padding: 0.1rem 0.3rem;
  outline: none;
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

.ally-stripe {
  width: 2px;
  align-self: stretch;
  background: var(--lc);
  flex-shrink: 0;
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
}

.owner-tag--unclaimed {
  color: var(--text-3);
}

/* ── Action buttons ── */
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

/* ── Drop transition ── */
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
