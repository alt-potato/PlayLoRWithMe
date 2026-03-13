<!--
  Pre-battle briefing shown during the BattleSetting phase. Displays the incoming
  enemy formation on the left, a reception focal point in the center, and allied
  librarians on the right with inline claim/release controls for multi-player
  sessions.

  Props:
    state       – full battle state (scene = 'battle', BattleSetting phase)
    session     – current session identity (null if not yet connected)
    players     – connected player list (from playerList WS messages)
    sendAction  – WebSocket action dispatcher
    claimUnit   – claim a librarian unit
    releaseUnit – release a librarian unit
-->
<script setup lang="ts">
import type {
  AllyUnit,
  GameState,
  PlayerInfo,
  SessionState,
  ActionResult,
  Unit,
} from "~/types/game";

const props = defineProps<{
  state: GameState;
  session: SessionState | null;
  players: PlayerInfo[];
  sendAction: (action: Record<string, unknown>) => Promise<ActionResult>;
  claimUnit: (unitId: number) => Promise<ActionResult>;
  releaseUnit: (unitId: number) => Promise<ActionResult>;
}>();

const ALLY_COLORS = ["#4fc3f7", "#81c784", "#ffb74d", "#ce93d8", "#f48fb1"];

const allyColors = computed<Record<number, string>>(() => {
  const m: Record<number, string> = {};
  (props.state?.allies ?? []).forEach((a, i) => {
    m[a.id] = ALLY_COLORS[i % ALLY_COLORS.length]!;
  });
  return m;
});

const allies = computed(() => props.state?.allies ?? []);
const enemies = computed(() => props.state?.enemies ?? []);
const claimsEnabled = computed(() => props.session?.claimsEnabled ?? false);

/** Returns the player that has claimed the given unit, if any. */
function ownerOf(unitId: number): PlayerInfo | undefined {
  return props.players.find((p) => p.units.includes(unitId));
}

/** True when this session has claimed the given unit. */
function isMyUnit(unitId: number): boolean {
  return props.session?.assignedUnits.includes(unitId) ?? false;
}

function hpPct(unit: Unit | AllyUnit): number {
  return Math.round((100 * unit.hp) / Math.max(unit.maxHp, 1));
}

const isConfirming = ref(false);

async function onConfirm() {
  if (isConfirming.value) return;
  isConfirming.value = true;
  try {
    await props.sendAction({ type: "confirm" });
  } finally {
    isConfirming.value = false;
  }
}
</script>

<template>
  <!-- ── Top bar (mirrors BattleView teaser-bar) ────────────────────────────── -->
  <div class="setting-bar">
    <div class="bar-left">
      <div class="stage-chips">
        <span class="chip">
          <span class="k">Floor</span>
          <strong>{{ state.stage?.floor ?? "—" }}</strong>
        </span>
        <span class="chip-sep">·</span>
        <span class="chip">
          <span class="k">Ch</span>
          <strong>{{ state.stage?.chapter ?? "—" }}</strong>
        </span>
        <span class="chip-sep">·</span>
        <span class="chip">
          <span class="k">Wave</span>
          <strong>{{ state.stage?.wave ?? "—" }}</strong>
        </span>
      </div>
      <span class="bar-phase">{{ state.phase }}</span>
    </div>

    <div class="bar-center">
      <button class="confirm-btn" :disabled="isConfirming" @click="onConfirm">
        {{ isConfirming ? "…" : "CONFIRM" }}
      </button>
    </div>

    <div class="bar-right">
      <span v-if="players.length > 0" class="players-chip">
        <span class="k">Players</span>
        <span class="players-count">{{ players.length }}</span>
      </span>
    </div>
  </div>

  <!-- ── Briefing layout ───────────────────────────────────────────────────── -->
  <div class="briefing">
    <!-- Enemy column -->
    <section class="wing wing--enemy" aria-label="Enemy units">
      <h2 class="wing-heading">Guests</h2>

      <p v-if="enemies.length === 0" class="wing-empty">No guests listed.</p>

      <TransitionGroup name="roster" tag="div" class="roster">
        <div
          v-for="unit in enemies"
          :key="unit.id"
          class="unit-card unit-card--enemy"
        >
          <span class="unit-stripe unit-stripe--enemy" />
          <div class="unit-body">
            <div class="unit-name">
              {{ unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}` }}
            </div>
            <div
              v-if="unit.keyPage?.name && unit.name !== unit.keyPage.name"
              class="unit-subname"
            >
              {{ unit.keyPage.name }}
            </div>
            <div class="hp-bar">
              <div class="hp-fill" :style="{ width: `${hpPct(unit)}%` }" />
            </div>
          </div>
        </div>
      </TransitionGroup>
    </section>

    <!-- Reception focal (center column) -->
    <div class="reception" role="presentation">
      <div class="reception-rule" />
      <div class="reception-body">
        <span class="reception-label">Reception</span>
        <!--
          Future: replace .reception-placeholder with an <img> tag when the
          GameStateSerializer emits a "receptionImage" field.
        -->
        <div class="reception-frame">
          <div class="reception-placeholder">
            <span class="placeholder-mark">✦</span>
          </div>
        </div>
      </div>
      <div class="reception-rule" />
    </div>

    <!-- Ally column -->
    <section class="wing wing--ally" aria-label="Allied librarians">
      <h2 class="wing-heading wing-heading--ally">Librarians</h2>

      <p v-if="allies.length === 0" class="wing-empty wing-empty--ally">
        No librarians assigned.
      </p>

      <TransitionGroup name="roster-ally" tag="div" class="roster roster--ally">
        <div
          v-for="(ally, i) in allies"
          :key="ally.id"
          class="unit-card unit-card--ally"
          :style="{ '--ac': allyColors[ally.id] ?? 'var(--gold-dim)' }"
        >
          <div class="unit-body unit-body--ally">
            <div class="unit-name">
              {{ ally.name ?? ally.keyPage?.name ?? `Unit #${ally.id}` }}
            </div>
            <div
              v-if="ally.keyPage?.name && ally.name !== ally.keyPage.name"
              class="unit-subname"
            >
              {{ ally.keyPage.name }}
            </div>
            <div class="hp-bar">
              <div
                class="hp-fill hp-fill--ally"
                :style="{ width: `${hpPct(ally)}%` }"
              />
            </div>

            <!-- Inline claim/release row -->
            <div v-if="claimsEnabled" class="claim-row">
              <span
                v-if="ownerOf(ally.id)"
                class="owner-label"
                :class="{ 'owner-label--me': isMyUnit(ally.id) }"
              >
                {{ isMyUnit(ally.id) ? "You" : ownerOf(ally.id)!.name }}
              </span>
              <span v-else class="owner-label owner-label--free">—</span>

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
          </div>
          <span class="unit-stripe unit-stripe--ally" />
        </div>
      </TransitionGroup>
    </section>
  </div>
</template>

<style scoped>
/* ── Top bar (mirrors .teaser-bar in BattleView) ──────────────────────────── */
.setting-bar {
  padding: 0.4rem 0.75rem;
  background: var(--bg-surface);
  border: 1px solid var(--border-mid);
  border-bottom: 1px solid var(--gold-dim);
  margin-bottom: 0.75rem;
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  gap: 0.5rem;
}

.bar-left {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 0;
}

.bar-center {
  display: flex;
  justify-content: center;
}

.bar-right {
  display: flex;
  justify-content: flex-end;
  align-items: center;
}

/* Stage info chips (mirrors .teaser-item / .teaser-item .k in BattleView) */
.stage-chips {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  flex-wrap: wrap;
}

.chip {
  font-size: 0.72rem;
  color: var(--text-2);
  font-family: var(--font-body);
}

.chip .k {
  font-family: var(--font-display);
  font-size: 0.58rem;
  letter-spacing: 0.07em;
  text-transform: uppercase;
  color: var(--text-2);
}

.chip strong {
  color: var(--gold);
}

.chip-sep {
  color: var(--border-hi);
  font-size: 0.65rem;
}

.bar-phase {
  color: var(--text-2);
  font-size: 0.65rem;
  font-family: var(--font-mono);
  white-space: nowrap;
}

.confirm-btn {
  padding: 0.25rem 1rem;
  background: var(--bg-green);
  border: 1px solid var(--green-hi);
  color: var(--green);
  font-size: 0.7rem;
  font-family: var(--font-display);
  letter-spacing: 0.08em;
  cursor: pointer;
  white-space: nowrap;
  transition:
    background 0.15s,
    color 0.15s;
}

.confirm-btn:hover:not(:disabled) {
  background: var(--green-hi);
  color: #fff;
}

.confirm-btn:disabled {
  border-color: var(--border-mid);
  color: var(--text-3);
  background: transparent;
  cursor: not-allowed;
  opacity: 0.5;
}

.players-chip {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  font-size: 0.62rem;
  color: var(--text-2);
  font-family: var(--font-body);
}

.players-chip .k {
  font-family: var(--font-display);
  font-size: 0.58rem;
  letter-spacing: 0.07em;
  text-transform: uppercase;
}

.players-count {
  color: var(--gold);
  font-size: 0.7rem;
}

/* ── Briefing layout ──────────────────────────────────────────────────────── */
.briefing {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 0 0.6rem;
}

@media (min-width: 600px) {
  .briefing {
    flex-direction: row;
    align-items: flex-start;
    gap: 0.5rem;
  }
}

/* ── Wings ────────────────────────────────────────────────────────────────── */
.wing {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
  max-width: 28rem;
}

.wing--ally {
  align-items: flex-end;
}

/*
  On mobile, allies appear first so players can act on claim buttons immediately
  without scrolling past the enemy list.
*/
@media (max-width: 599px) {
  .wing--ally {
    order: -1;
    align-items: flex-start;
  }

  .wing--enemy {
    order: 1;
  }

  .reception {
    order: 0;
  }
}

.wing-heading {
  font-family: var(--font-display);
  font-size: 0.65rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.16em;
  color: var(--crimson-hi);
  margin-bottom: 0.5rem;
}

.wing-heading--ally {
  color: var(--gold);
  text-align: right;
}

@media (max-width: 599px) {
  .wing-heading--ally {
    text-align: left;
  }
}

.wing-empty {
  font-size: 0.68rem;
  color: var(--text-3);
  font-style: italic;
  font-family: var(--font-body);
  padding: 0.5rem 0;
}

/* ── Roster ───────────────────────────────────────────────────────────────── */
.roster {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  width: 100%;
}

.roster--ally {
  align-items: flex-end;
}

@media (max-width: 599px) {
  .roster--ally {
    align-items: flex-start;
  }
}

/* ── Unit briefing cards ──────────────────────────────────────────────────── */
.unit-card {
  display: flex;
  width: 100%;
  background: var(--bg-card);
  border: 1px solid var(--border);
  overflow: hidden;
  transition:
    background 0.13s,
    border-color 0.13s;
}

.unit-card:hover {
  background: var(--bg-card-2);
  border-color: var(--border-mid);
}

/* 3px accent stripe mirrors the unit card side border in the battle view */
.unit-stripe {
  width: 3px;
  flex-shrink: 0;
  align-self: stretch;
}

.unit-stripe--enemy {
  background: var(--crimson-hi);
}

.unit-stripe--ally {
  background: var(--ac);
}

.unit-body {
  flex: 1;
  padding: 0.4rem 0.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.18rem;
  min-width: 0;
}

.unit-body--ally {
  text-align: right;
}

@media (max-width: 599px) {
  .unit-body--ally {
    text-align: left;
  }
}

.unit-name {
  font-family: var(--font-display);
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: 0.04em;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.unit-subname {
  font-family: var(--font-body);
  font-size: 0.58rem;
  color: var(--text-3);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* ── HP bar ───────────────────────────────────────────────────────────────── */
.hp-bar {
  width: 100%;
  height: 2px;
  background: var(--bg-card-3);
  margin-top: 0.1rem;
}

.hp-fill {
  height: 100%;
  background: var(--crimson-hi);
  transition: width 0.4s ease;
}

.hp-fill--ally {
  background: var(--gold-dim);
}

/* ── Claim row ────────────────────────────────────────────────────────────── */
.claim-row {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.3rem;
  margin-top: 0.15rem;
}

@media (max-width: 599px) {
  .claim-row {
    justify-content: flex-start;
  }
}

.owner-label {
  font-family: var(--font-body);
  font-size: 0.6rem;
  color: var(--gold);
  white-space: nowrap;
}

.owner-label--me {
  color: var(--gold-bright);
  font-weight: 500;
}

.owner-label--free {
  color: var(--text-3);
}

/* Claim/release buttons match .action-btn in SessionPanel */
.claim-btn {
  font-family: var(--font-display);
  font-size: 0.46rem;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  padding: 0.1rem 0.4rem;
  background: transparent;
  border: 1px solid var(--gold-dim);
  color: var(--gold);
  cursor: pointer;
  white-space: nowrap;
  transition:
    background 0.12s,
    border-color 0.12s;
  flex-shrink: 0;
}

.claim-btn:hover {
  background: rgba(201, 162, 39, 0.12);
  border-color: var(--gold);
}

.claim-btn--release {
  border-color: var(--crimson);
  color: var(--text-red);
}

.claim-btn--release:hover {
  background: var(--crimson-dim);
  border-color: var(--crimson-hi);
}

/* ── Reception focal (center column) ─────────────────────────────────────── */
.reception {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0;
  /* Width chosen to be unobtrusive but visually grounding */
  width: 5rem;
  align-self: stretch;
}

/* Horizontal rule on mobile */
@media (max-width: 599px) {
  .reception {
    width: 100%;
    flex-direction: row;
    align-items: center;
    align-self: auto;
    gap: 0;
  }
}

/* Thin gradient rule — vertical on desktop, horizontal on mobile */
.reception-rule {
  flex: 1;
  width: 1px;
  background: linear-gradient(
    to bottom,
    transparent,
    var(--gold-dim) 30%,
    var(--gold-dim) 70%,
    transparent
  );
}

@media (max-width: 599px) {
  .reception-rule {
    width: auto;
    height: 1px;
    background: linear-gradient(
      to right,
      transparent,
      var(--gold-dim) 30%,
      var(--gold-dim) 70%,
      transparent
    );
  }
}

.reception-body {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.3rem;
  padding: 0.5rem 0;
}

.reception-label {
  font-family: var(--font-display);
  font-size: 0.4rem;
  text-transform: uppercase;
  letter-spacing: 0.22em;
  color: var(--text-3);
}

/* Square frame for the reception image (future: replace placeholder with <img>) */
.reception-frame {
  width: 3.5rem;
  height: 3.5rem;
  border: 1px solid var(--border);
  background: var(--bg-card);
  display: flex;
  align-items: center;
  justify-content: center;
}

.reception-placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 100%;
}

.placeholder-mark {
  color: var(--gold-dim);
  font-size: 1.1rem;
}

/* ── Roster enter/leave transitions ───────────────────────────────────────── */
.roster-enter-active,
.roster-ally-enter-active {
  transition:
    opacity 0.22s ease,
    transform 0.22s ease;
}

.roster-leave-active,
.roster-ally-leave-active {
  transition: opacity 0.15s ease;
}

.roster-enter-from {
  opacity: 0;
  transform: translateX(-8px);
}

.roster-ally-enter-from {
  opacity: 0;
  transform: translateX(8px);
}

.roster-leave-to,
.roster-ally-leave-to {
  opacity: 0;
}
</style>
