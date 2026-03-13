<!--
  Pre-battle briefing shown during the BattleSetting phase. Displays the incoming
  enemy formation on the left, a reception focal point in the center, and allied
  librarians on the right with inline claim/release controls for multi-player
  sessions.

  Clicking a unit card expands an inline detail panel showing speed dice range
  and the resistance table.

  Props:
    state       – full battle state (scene = 'main', uiPhase = 'BattleSetting')
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

/** Fill color keyed to HP percentage — green → orange → red as health drops. */
function hpColor(unit: Unit | AllyUnit): string {
  const pct = hpPct(unit);
  if (pct > 66) return "var(--green)";
  if (pct > 33) return "var(--orange)";
  return "var(--red-hi)";
}

// ── Click-to-expand detail panels ─────────────────────────────────────────

/** ID of the currently expanded enemy card (null = none). */
const expandedEnemy = ref<number | null>(null);
/** ID of the currently expanded ally card (null = none). */
const expandedAlly = ref<number | null>(null);

function toggleEnemy(id: number) {
  expandedEnemy.value = expandedEnemy.value === id ? null : id;
}

function toggleAlly(id: number) {
  expandedAlly.value = expandedAlly.value === id ? null : id;
}

// ── Reception icon scaling ─────────────────────────────────────────────────

// 3.5rem at 16px base — matches .reception-frame width/height in CSS.
const RECEPTION_FRAME_PX = 56;

/** Natural pixel size of the glow sprite once it loads. */
const glowNatural = ref({ w: 0, h: 0 });

function onGlowLoad(e: Event) {
  const img = e.target as HTMLImageElement;
  glowNatural.value = { w: img.naturalWidth, h: img.naturalHeight };
}

/**
 * Scale both images uniformly so the larger one (glow) fits the frame.
 * Hidden until the glow loads so there's no flash of natural-size images.
 */
const receptionStackStyle = computed(() => {
  const { w, h } = glowNatural.value;
  if (!w || !h) return { visibility: "hidden" as const };
  const scale = RECEPTION_FRAME_PX / Math.max(w, h);
  return { width: `${w}px`, height: `${h}px`, transform: `scale(${scale})` };
});

// ── Confirm ────────────────────────────────────────────────────────────────

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
  <!-- ── Top bar (mirrors BattleStage teaser-bar) ────────────────────────────── -->
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
      <span class="bar-phase">{{ state.uiPhase }}</span>
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
          :class="{
            'unit-card--open': expandedEnemy === unit.id,
            'unit-card--disabled': unit.enabled === false,
          }"
          @click="toggleEnemy(unit.id)"
        >
          <span class="unit-stripe unit-stripe--enemy" />
          <div class="unit-body">
            <div class="unit-header">
              <div class="unit-title">
                <span class="unit-name">
                  {{ unit.name ?? unit.keyPage?.name ?? `Unit #${unit.id}` }}
                </span>
                <span
                  v-if="unit.keyPage?.name && unit.name !== unit.keyPage.name"
                  class="unit-keypage"
                  >{{ unit.keyPage.name }}</span
                >
              </div>
            </div>
            <div class="unit-stats">
              <div class="stats-inner">
                <img src="/assets/stats/health.png" class="stat-icon" alt="HP" /><span class="stat-v" :style="{ color: hpColor(unit) }"
                  >{{ unit.hp
                  }}<span class="stat-max"> / {{ unit.maxHp }}</span></span
                >
                <template v-if="unit.maxStaggerGauge">
                  <span class="stat-sep">·</span>
                  <img src="/assets/stats/stagger.png" class="stat-icon" alt="Stagger" /><span class="stat-v">{{ unit.maxStaggerGauge }}</span>
                </template>
                <template v-if="unit.keyPage?.speedMin != null">
                  <span class="stat-sep">·</span>
                  <img src="/assets/stats/speed.png" class="stat-icon" alt="Speed" /><span class="stat-v"
                    >{{ unit.keyPage.speedMin }}–{{
                      unit.keyPage.speedMax
                    }}</span
                  >
                </template>
              </div>
              <span
                class="unit-chevron"
                :class="{ open: expandedEnemy === unit.id }"
                >▸</span
              >
            </div>
            <div class="hp-bar">
              <div
                class="hp-fill"
                :style="{ width: `${hpPct(unit)}%`, background: hpColor(unit) }"
              />
            </div>

            <Transition name="detail-expand">
              <div
                v-if="expandedEnemy === unit.id"
                class="unit-detail"
                @click.stop
              >
                <BattleSettingDetailPanel :unit="unit" />
              </div>
            </Transition>
          </div>
        </div>
      </TransitionGroup>
    </section>

    <!-- Reception focal (center column) -->
    <div class="reception" role="presentation">
      <div class="reception-rule" />
      <div class="reception-body">
        <span class="reception-label">{{
          state.stage?.name ?? "Reception"
        }}</span>
        <div class="reception-frame">
          <div
            v-if="state.stage?.icon"
            class="reception-icon-stack"
            :style="receptionStackStyle"
          >
            <img
              v-if="state.stage.iconGlow"
              :src="`/assets/stageicons/${state.stage.iconGlow}.png`"
              alt=""
              class="reception-img--glow"
              @load="onGlowLoad"
            />
            <img
              :src="`/assets/stageicons/${state.stage.icon}.png`"
              alt=""
              class="reception-img--icon"
            />
          </div>
          <div v-else class="reception-placeholder">
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
          :class="{
            'unit-card--open': expandedAlly === ally.id,
            'unit-card--disabled': ally.enabled === false,
          }"
          :style="{ '--ac': allyColors[ally.id] ?? 'var(--gold-dim)' }"
          @click="toggleAlly(ally.id)"
        >
          <div class="unit-body unit-body--ally">
            <div class="unit-header unit-header--ally">
              <div class="unit-title unit-title--ally">
                <span
                  v-if="ally.keyPage?.name && ally.name !== ally.keyPage.name"
                  class="unit-keypage unit-keypage--ally"
                  >{{ ally.keyPage.name }}</span
                >
                <span class="unit-name">
                  {{ ally.name ?? ally.keyPage?.name ?? `Unit #${ally.id}` }}
                </span>
              </div>
            </div>
            <div class="unit-stats unit-stats--ally">
              <span
                class="unit-chevron"
                :class="{ open: expandedAlly === ally.id }"
                >▸</span
              >
              <div class="stats-inner stats-inner--ally">
                <img src="/assets/stats/health.png" class="stat-icon" alt="HP" /><span class="stat-v" :style="{ color: hpColor(ally) }"
                  >{{ ally.hp
                  }}<span class="stat-max"> / {{ ally.maxHp }}</span></span
                >
                <template v-if="ally.maxStaggerGauge">
                  <span class="stat-sep">·</span>
                  <img src="/assets/stats/stagger.png" class="stat-icon" alt="Stagger" /><span class="stat-v">{{ ally.maxStaggerGauge }}</span>
                </template>
                <template v-if="ally.keyPage?.speedMin != null">
                  <span class="stat-sep">·</span>
                  <img src="/assets/stats/speed.png" class="stat-icon" alt="Speed" /><span class="stat-v"
                    >{{ ally.keyPage.speedMin }}–{{
                      ally.keyPage.speedMax
                    }}</span
                  >
                </template>
              </div>
            </div>
            <div class="hp-bar">
              <div
                class="hp-fill"
                :style="{ width: `${hpPct(ally)}%`, background: hpColor(ally) }"
              />
            </div>

            <!-- Inline claim/release row — stop propagation so click doesn't toggle detail -->
            <div v-if="claimsEnabled" class="claim-row" @click.stop>
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

            <Transition name="detail-expand">
              <div
                v-if="expandedAlly === ally.id"
                class="unit-detail unit-detail--ally"
                @click.stop
              >
                <BattleSettingDetailPanel :unit="ally" :flip="true" />
              </div>
            </Transition>
          </div>
          <span class="unit-stripe unit-stripe--ally" />
        </div>
      </TransitionGroup>
    </section>
  </div>
</template>

<style scoped>
/* ── Top bar (mirrors .teaser-bar in BattleStage) ──────────────────────────── */
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

/* Stage info chips (mirrors .teaser-item / .teaser-item .k in BattleStage) */
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
  justify-content: space-between;
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
  cursor: pointer;
  transition:
    background 0.13s,
    border-color 0.13s;
}

.unit-card:hover {
  background: var(--bg-card-2);
  border-color: var(--border-mid);
}

.unit-card--open {
  border-color: var(--border-mid);
}

/* Dead or locked units: dimmed and non-interactive */
.unit-card--disabled {
  opacity: 0.4;
  pointer-events: none;
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

/* ── Unit header row (name + expand chevron) ─────────────────────────────── */
.unit-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.3rem;
}

/* Ally header contains only the title; no extra layout needed */
.unit-title {
  flex: 1;
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0.05rem 0.35rem;
  min-width: 0;
}

/* Ally title right-aligns content so the name sits on the right side */
.unit-title--ally {
  justify-content: flex-end;
}

@media (max-width: 599px) {
  .unit-title--ally {
    justify-content: flex-start;
  }
}

.unit-name {
  flex-shrink: 0;
  font-family: var(--font-display);
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: 0.04em;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
  max-width: 100%;
}

/* Key page name shown beside the unit name; margin-left:auto pushes it right */
.unit-keypage {
  flex-shrink: 0;
  margin-left: auto;
  font-family: var(--font-body);
  font-size: 0.55rem;
  color: var(--text-3);
  white-space: nowrap;
}

/* Ally variant: keypage sits on the left, margin-right:auto pushes name right */
.unit-keypage--ally {
  margin-left: 0;
  margin-right: auto;
}

.unit-chevron {
  color: var(--text-3);
  font-size: 0.55rem;
  display: inline-block;
  flex-shrink: 0;
  transition: transform 0.18s ease;
}

.unit-chevron.open {
  transform: rotate(90deg);
}

/* ── Always-visible stats row (HP · Stagger · Speed · chevron) ───────────── */
.unit-stats {
  display: flex;
  align-items: center;
  gap: 0.3rem;
}

/* Ally: chevron on left, stats pushed to the right */
.unit-stats--ally {
  flex-direction: row; /* chevron first in DOM → left side */
}

/* Stat chip group — fills remaining space in the row */
.stats-inner {
  display: flex;
  align-items: center;
  gap: 0.2rem;
  flex-wrap: wrap;
  flex: 1;
}

/* Right-align chips inside the ally stats row */
.stats-inner--ally {
  justify-content: flex-end;
}

@media (max-width: 599px) {
  .stats-inner--ally {
    justify-content: flex-start;
  }
}

.stat-k {
  font-family: var(--font-display);
  font-size: 0.46rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-3);
}

.stat-icon {
  height: 0.85rem;
  width: auto;
  vertical-align: middle;
  opacity: 0.8;
}

.stat-v {
  font-family: var(--font-body);
  font-size: 0.6rem;
  color: var(--text-2);
  font-weight: 500;
}

.stat-sep {
  color: var(--border-hi);
  font-size: 0.55rem;
}

/* ── HP bar ───────────────────────────────────────────────────────────────── */
.hp-bar {
  width: 100%;
  height: 4px;
  background: var(--bg-card-3);
  margin-top: 0.15rem;
  border-radius: 1px;
  overflow: hidden;
}

.hp-fill {
  height: 100%;
  /* color set inline via hpColor() */
  transition:
    width 0.4s ease,
    background 0.4s ease;
}

/* Dimmed "/ max" portion of the HP label in the stats row */
.stat-max {
  color: var(--text-3);
  font-size: 0.55em;
}

/* ── Detail panel ─────────────────────────────────────────────────────────── */
.unit-detail {
  border-top: 1px solid var(--border);
  margin-top: 0.2rem;
  padding-top: 0.3rem;
}

/* Right-aligned detail for ally cards mirrors the body text alignment */
.unit-detail--ally {
  text-align: right;
}

@media (max-width: 599px) {
  .unit-detail--ally {
    text-align: left;
  }
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
  width: 5rem;
  align-self: stretch;
}

@media (max-width: 599px) {
  .reception {
    width: 100%;
    flex-direction: row;
    align-items: center;
    align-self: auto;
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
  text-align: center;
}

/* Square frame for the reception image */
/* Square frame for the reception image. */
.reception-frame {
  position: relative;
  width: 3.5rem;
  height: 3.5rem;
  border: 1px solid var(--border);
  background: var(--bg-card);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

/*
 * Wrapper sized to the glow's natural pixel dimensions via :style.
 * A uniform scale() transform is applied so the glow fills the frame and
 * the icon scales by the same factor — both images sit at natural size inside.
 */
.reception-icon-stack {
  position: relative;
  flex-shrink: 0;
  transform-origin: center;
}

/* Glow: flow element that defines the stack's natural size. */
.reception-img--glow {
  display: block;
  opacity: 0.9;
  /* color shift to gold */
  filter: invert(36%) sepia(68%) saturate(502%) hue-rotate(6deg) brightness(90%)
    contrast(85%);
}

/* Icon: centered over the glow at its own natural size. */
.reception-img--icon {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
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

/* ── Detail panel expand/collapse transition ──────────────────────────────── */
.detail-expand-enter-active {
  transition:
    opacity 0.18s ease,
    max-height 0.22s ease;
  overflow: hidden;
  max-height: 200px;
}

.detail-expand-leave-active {
  transition:
    opacity 0.13s ease,
    max-height 0.15s ease;
  overflow: hidden;
}

.detail-expand-enter-from,
.detail-expand-leave-to {
  opacity: 0;
  max-height: 0;
}
</style>
