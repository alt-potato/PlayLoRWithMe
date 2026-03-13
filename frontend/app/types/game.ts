/**
 * Shared domain types for the PlayLoRWithMe game state API.
 *
 * These types mirror the JSON shapes produced by GameStateSerializer.cs and
 * consumed by the SSE stream / GET /state endpoint. Define each type once here
 * and import wherever needed.
 */

export type SceneName =
  | "title"
  | "main"
  | "story"
  | "battle"
  | "loading"
  | "transition";

export type BattleUnitTurnState =
  | "WAIT_TURN"
  | "WAIT_CARD"
  | "DOING_ACTION"
  | "DONE_ACTION"
  | "DOING_INTERLACE"
  | "DOING_PARRYING"
  | "SKIP_TURN"
  | "BREAK";

export interface StageInfo {
  floor?: number;
  chapter?: number;
  wave?: number;
  round?: number;
  /** Reception/stage name from StageClassInfo.stageName. */
  name?: string;
  /** Story-chapter icon sprite ID; served at /assets/stageicons/<icon>.png. */
  icon?: string;
  /** Glow layer rendered behind the icon (same sprite set as icon). */
  iconGlow?: string;
}

// ── Cards & dice ──────────────────────────────────────────────────────────────

/** Behaviour die inside a card (type, detail, min/max roll, optional script desc). */
export interface Die {
  type: string;
  detail: string;
  min: number;
  max: number;
  desc?: string;
}

/** Token/buff applied by a card (displayed as a small icon chip on the card). */
export interface CardToken {
  label: string;
  stack: number;
  icon?: string;
}

/** Unique identifier for a LoR data-entry (row id + package id). */
export interface EntryId {
  id: number;
  packageId: number;
}

/** A playable battle card as returned in a unit's hand, deck, or EGO list. */
export interface Card {
  id: EntryId;
  index: number;
  name: string;
  cost: number;
  /** Present when cost has been modified mid-round; used by costStyle(). */
  baseCost?: number;
  range: string;
  rarity?: string;
  options?: string[];
  allyTarget?: boolean;
  canUse?: boolean;
  emotionLimit?: number;
  desc?: string;
  flavorText?: string;
  abilityDesc?: string;
  dice?: Die[];
  bufs?: CardToken[];
  icon?: string;
}

/** Secondary target slot for mass-range (FarArea/FarAreaEach) attacks. */
export interface SubTarget {
  targetUnitId: number;
  targetSlot: number;
}

/** A card already slotted into a speed die — subset of Card plus targeting info. */
export interface SlottedCardEntry {
  cardIndex: number;
  slot: number;
  name: string;
  cost: number;
  targetUnitId?: number;
  targetSlot?: number;
  clash: boolean;
  subTargets?: SubTarget[];
  range: string;
  desc?: string;
  flavorText?: string;
  dice?: Die[];
}

/** One speed die on a unit — carries its current rolled value and state. */
export interface SpeedDie {
  slot: number;
  value: number;
  type: string;
  detail: string;
  /** True when the die is staggered (shown as ✕). */
  staggered?: boolean;
}

// ── Unit metadata ─────────────────────────────────────────────────────────────

export interface EmotionCoins {
  positive: number;
  negative: number;
  max: number;
}

/** A passive ability slot on a unit. */
export interface Passive {
  id: EntryId;
  name: string;
  desc?: string;
  icon?: string;
  disabled?: boolean;
  isNegative?: boolean;
  rare?: string;
}

/** An active buff/debuff on a unit. */
export interface Buff {
  keywordId: string;
  /** Internal engine key used as Vue list key. */
  type?: string;
  name: string;
  desc?: string;
  stacks?: number;
  icon?: string;
  /** "Positive" | "Negative" — controls chip colour. */
  positive?: string;
}

/** Entry in a unit's abnormality/emotion-card list. */
export interface AbnormalityEntry {
  id: number;
  name: string;
  emotionLevel?: number;
}

export interface Resistances {
  slashHp?: string;
  pierceHp?: string;
  bluntHp?: string;
  slashBp?: string;
  pierceBp?: string;
  bluntBp?: string;
}

export interface KeyPage {
  name: string;
  speedDiceCount?: number;
  speedMin?: number;
  speedMax?: number;
  resistances?: Resistances;
}

// ── Unit shapes ───────────────────────────────────────────────────────────────

/** Card entry in a BattleSetting deck preview (grouped by card type with a count). */
export interface DeckCardPreview {
  name: string;
  cost: number;
  range: string;
  rarity?: string;
  /** How many copies of this card are in the deck. */
  count: number;
  dice?: Die[];
  abilityDesc?: string;
}

/** Fields shared by both ally and enemy units. */
export interface Unit {
  id: number;
  name?: string;
  hp: number;
  maxHp: number;
  staggerGauge: number;
  maxStaggerGauge: number;
  staggerThreshold: number;
  targetable: boolean;
  turnState: BattleUnitTurnState;
  speedDice: SpeedDie[];
  slottedCards: SlottedCardEntry[];
  passives: Passive[];
  buffs: Buff[];
  abnormalities: AbnormalityEntry[];
  emotionLevel: number;
  emotionCoins: EmotionCoins;
  keyPage?: KeyPage;
  /**
   * False when the unit is dead or locked — only present in BattleSetting phase.
   * Undefined (absent) during battle means no restriction.
   */
  enabled?: boolean;
  /** Deck card preview — only present in BattleSetting phase. */
  deckPreview?: DeckCardPreview[];
}

/** Ally-only extras: light, hand, deck, EGO. */
export interface AllyUnit extends Unit {
  playPoint: number;
  maxPlayPoint: number;
  reservedPlayPoint: number;
  /** Present when this ally is owned by the current session. */
  hand?: Card[];
  deck?: Card[];
  ego?: Card[];
  teamHand?: Card[];
  /**
   * Present instead of hand/deck/ego when this ally belongs to another
   * session. The server sends counts rather than card data to preserve privacy.
   */
  handCount?: number;
  deckCount?: number;
  egoCount?: number;
}

// ── Abnormality selection phase ───────────────────────────────────────────────

export interface AbnormalityChoice {
  id: number;
  name: string;
  emotionLevel: number;
  targetType: string;
  state: string;
  desc?: string;
  flavorText?: string;
}

export interface AbnormalitySelection {
  choices: AbnormalityChoice[];
  teamEmotionLevel?: number;
  teamCoin?: number;
  teamCoinMax?: number;
  teamPositiveCoins?: number;
  teamNegativeCoins?: number;
}

// ── Session & WebSocket ───────────────────────────────────────────────────────

/** The current browser tab's session identity. */
export interface SessionState {
  sessionId: string;
  /** Unit IDs this session has claimed. */
  assignedUnits: number[];
  /** Whether the server is enforcing unit claims. False means everyone can play any unit. */
  claimsEnabled: boolean;
}

/** One connected player as reported in a playerList message. */
export interface PlayerInfo {
  sessionId: string;
  name: string;
  units: number[];
}

/** Result returned when a WebSocket action resolves. */
export interface ActionResult {
  ok: boolean;
  error?: string;
}

// ── Root state ────────────────────────────────────────────────────────────────

/** Top-level SSE / GET /state payload. */
export interface GameState {
  scene: SceneName;
  /** Raw C# StageController.Phase class name (e.g. "ApplyLibrarianCardPhase"). */
  phase?: string;
  /** Raw C# StageController.State enum value (e.g. "BattleSetting"). */
  stageState?: string;
  uiPhase?: string;
  stage?: StageInfo;
  allies?: AllyUnit[];
  enemies?: Unit[];
  /** Only present during the key-page selection phase. */
  abnormalitySelection?: AbnormalitySelection;
}
