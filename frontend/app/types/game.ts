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
  /** BookModel.instanceId — present on librarian key pages, absent on battle unit key pages. */
  instanceId?: number;
  name: string;
  speedDiceCount?: number;
  speedMin?: number;
  speedMax?: number;
  resistances?: Resistances;
  /** Max HP including gift bonuses — present on librarian key pages. */
  hp?: number;
  /** Break (stagger) gauge capacity — present on librarian key pages. */
  breakGauge?: number;
  /** BookXmlInfo.RangeType: "Melee" | "Range" | "Hybrid" — determines which card ranges can be equipped. */
  equipRangeType?: string;
  /** BookXmlInfo integer ID — used to load the body composite PNG for the preview. */
  bookId?: number;
}

// ── Unit shapes ───────────────────────────────────────────────────────────────

/** Card entry in a BattleSetting deck preview (grouped by card type with a count). */
export interface DeckCardPreview {
  /** LorId of the card — present in librarian deck previews, absent in BattleSetting previews. */
  cardId?: { id: number; packageId: string };
  name: string;
  cost: number;
  range: string;
  rarity?: string;
  /** How many copies of this card are in the deck. */
  count: number;
  dice?: Die[];
  abilityDesc?: string;
}

export interface EmotionCardEntry {
  /** Floor realization level at which this card unlocks (1–6). */
  level: number;
  name: string;
  state: "Positive" | "Negative";
  targetType: "All" | "SelectOne" | "AllIncludingEnemy";
  /** Emotion coin cost during battle. */
  emotionLevel: number;
  /** Name of the abnormality this card belongs to (e.g. "Big Bird"). */
  abnormalityName?: string;
  /** Ability description from AbnormalityCardDescXmlList (same source as battle selection). */
  desc?: string;
  flavorText?: string;
}

export interface FloorEntry {
  floorIndex: number;
  /** Localized floor name from the game's text system (e.g. "Malkuth"). */
  officialName: string;
  /** Current realization level (1–6). */
  realizationLevel: number;
  /**
   * EGO pages: full battle cards (with dice) from EmotionEgoXmlList.
   * These are distinct from abnormality pages — they are real DiceCardXmlInfo
   * objects, serialized identically to deckPreview cards.
   */
  egoCards: DeckCardPreview[];
  /** All abnormality pages (Awakening/Breakdown) unlocked up to the current realization level. */
  emotionCards: EmotionCardEntry[];
  librarians: LibrarianEntry[];
}

export interface LibrarianEntry {
  floorIndex: number;
  unitIndex: number;
  name: string;
  keyPage: KeyPage;
  passives: Passive[];
  deckPreview: DeckCardPreview[];
  /** Session ID of the player currently editing this librarian, or null. */
  lockedBy: string | null;
  /**
   * Page-exclusive cards (CardOption.OnlyPage) belonging to this key page
   * that are currently in the shared inventory. Empty array when none exist.
   * Presented first in the deck editor's add-cards list.
   */
  onlyCards?: AvailableCard[];
  /** Appearance customization data (present for customizable librarians). */
  appearance?: AppearanceData;
  /** Per-type custom battle dialogue text (null = using a random game preset). */
  dialogue?: DialogueData;
  /** Title gift IDs for the name bar prefix and suffix. */
  titles?: TitleData;
  /**
   * ID of the custom core book currently used as an appearance projection, or -1 if none.
   * Set by EquipCustomCoreBook; persisted in save data as customcorebookInstanceId.
   */
  customBookId?: number;
  /**
   * Active body type variant: "F" (female), "M" (male), or "N" (neutral).
   * Controls which _F/_M/_N prefab suffix is loaded in-game.
   */
  appearanceType?: string;
  /**
   * SkinGender of the active skin source (fashion book or key page): "F" or "M".
   * Omitted when "N" (no gendered variants exist, body type toggle disabled).
   */
  skinGender?: string;
  /** Equipped key page has a body composite in fashionbodies/ (replacesHead behavior). */
  keyPageReplacesHead?: boolean;
  /** Equipped key page has a front-layer composite in fashionbodies_front/. */
  keyPageHasFrontLayer?: boolean;
  /** Equipped key page body has a head tilt (Z-axis rotation). */
  keyPageHeadTiltDeg?: number;
  keyPagePivotFracX?: number;
  keyPagePivotFracY?: number;
  /** Equipped key page has a Hood sprite — back hair should be hidden. */
  keyPageHidesBackHair?: boolean;
  /** SkinGender of the equipped key page skin: "F" or "M". */
  keyPageSkinGender?: string;
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

/** A key page from the book inventory available to equip to a librarian. */
export interface AvailableKeyPage {
  instanceId: number;
  name: string;
  speedMin: number;
  speedMax: number;
  bookId: { id: number; packageId: string };
  /** Story/origin chapter number — mirrors BookXmlInfo.Chapter. */
  chapter: number;
  /**
   * Story line identifier — mirrors BookXmlInfo.BookIcon (= UIStoryLine enum name,
   * e.g. "Rats", "Chapter1"). Used to group key pages the same way the in-game
   * equip screen does.
   */
  bookIcon: string;
  hp: number;
  breakGauge: number;
  /** BookXmlInfo.RangeType: "Melee" | "Range" | "Hybrid" — determines which card ranges can be equipped. */
  equipRangeType: string;
  resistances: Resistances;
  passives: Passive[];
}

/** A card from the shared inventory available to add to a librarian's deck. */
export interface AvailableCard {
  cardId: { id: number; packageId: string };
  name: string;
  cost: number;
  range: string;
  rarity: string;
  count: number;
  abilityDesc?: string;
  dice?: Die[];
  chapter?: number;
}

/** Appearance customization fields mirroring UnitCustomizingData. Colors are [R, G, B] (0–255). */
export interface AppearanceData {
  frontHairID: number;
  backHairID: number;
  eyeID: number;
  browID: number;
  mouthID: number;
  /** Head sprite index (front=0, side=1). Server-serialized but unused by the preview. */
  headID?: number;
  height: number;
  hairColor: [number, number, number];
  skinColor: [number, number, number];
  eyeColor: [number, number, number];
}

/** Per-type custom battle dialogue text. null = game uses a randomly-selected preset. */
export interface DialogueData {
  startBattle: string | null;
  victory: string | null;
  death: string | null;
  colleagueDeath: string | null;
  killsOpponent: string | null;
}

/** Gift-based title IDs (prefix = before name, suffix = after name). */
export interface TitleData {
  prefixID: number;
  postfixID: number;
}

export interface TitleOption {
  id: number;
  text: string;
}

/** Global customization option tables sent once per library state snapshot. */
/** A custom core book that can be used as an appearance projection skin. */
export interface FashionBook {
  id: number;
  name: string;
  /** EquipRangeType string — controls compatibility with librarian's range type. */
  rangeType: string;
  /**
   * True when the fashion skin overrides the head model in addition to the body
   * (skinType != "Lor" in BookXmlInfo). The composite face preview is not accurate
   * when this is true.
   */
  replacesHead: boolean;
  /**
   * Z-axis rotation of the customPivot transform in degrees (positive = clockwise
   * in Unity's left-hand screen space). Omitted when zero. Applied to face/hair
   * CSS layers so the preview matches the in-game head tilt.
   */
  headTiltDeg?: number;
  /** Pivot horizontal position as a fraction [0,1] of the canvas width (left=0). */
  pivotFracX?: number;
  /** Pivot vertical position as a fraction [0,1] of the canvas height (top=0). */
  pivotFracY?: number;
  /**
   * True when fashionbodies_front/{id}.png was extracted — some body sprites
   * render in front of the face overlay and must be composited above it.
   */
  hasFrontLayer?: boolean;
  /**
   * True when the character model has a Hood sprite — the game hides all back
   * hair renderers in that case (RefreshAppearanceByMotion forcibly deactivates them).
   */
  hidesBackHair?: boolean;
  /**
   * Gender variant of the skin (from BookXmlInfo.SkinGender): "F", "M", or omitted for "N".
   * When present, the body type toggle is enabled and body PNGs use a _f / _m suffix.
   */
  skinGender?: string;
}

export interface CustomizeOptions {
  suggestedNames: string[];
  prefixTitles: TitleOption[];
  suffixTitles: TitleOption[];
  dialoguePresets: Record<keyof DialogueData, string[]>;
  fashionBooks?: FashionBook[];
}

/** Flat payload sent via setCustomization WebSocket action. */
export interface CustomizePayload {
  floorIndex: number;
  unitIndex: number;
  frontHairID: number;
  backHairID: number;
  eyeID: number;
  browID: number;
  mouthID: number;
  height: number;
  hairR: number;
  hairG: number;
  hairB: number;
  skinR: number;
  skinG: number;
  skinB: number;
  eyeR: number;
  eyeG: number;
  eyeB: number;
  dlgStartBattle: string | null;
  dlgVictory: string | null;
  dlgDeath: string | null;
  dlgColleagueDeath: string | null;
  dlgKillsOpponent: string | null;
  prefixID: number;
  postfixID: number;
  /** -1 = unequip; any other value = BookXmlInfo ID to equip as appearance projection. */
  customBookId: number;
  /** Body type variant: "F", "M", or "N". */
  appearanceType: string;
}

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
  /** Present in main scene (non-BattleSetting) — floor roster with nested librarians. */
  floors?: FloorEntry[];
  /** Key pages in the book inventory available to equip to a librarian. */
  availableKeyPages?: AvailableKeyPage[];
  /** Cards in the shared inventory available to add to a librarian's deck. */
  availableCards?: AvailableCard[];
  /** Global customization option tables (names, title lists, dialogue presets). */
  customizeOptions?: CustomizeOptions;
}
