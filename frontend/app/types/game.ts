/**
 * Shared domain types for the PlayLoRWithMe game state API.
 *
 * These types mirror the JSON shapes produced by GameStateSerializer.cs and
 * consumed via the WebSocket `/ws` endpoint. Every exported type is derived
 * from a Zod schema via `z.infer<>` so the wire contract has a single
 * machine-checkable source of truth. The schemas are exported so the
 * build script at `scripts/generate-schema.ts` can emit a canonical JSON
 * Schema 2020-12 artifact at `schema/gamestate.schema.json`.
 */

import { z } from "zod";

// ── Enums ────────────────────────────────────────────────────────────────────

export const SceneNameSchema = z.enum([
  "title",
  "main",
  "story",
  "battle",
  "loading",
  "transition",
]);
export type SceneName = z.infer<typeof SceneNameSchema>;

export const BattleUnitTurnStateSchema = z.enum([
  "WAIT_TURN",
  "WAIT_CARD",
  "DOING_ACTION",
  "DONE_ACTION",
  "DOING_INTERLACE",
  "DOING_PARRYING",
  "SKIP_TURN",
  "BREAK",
]);
export type BattleUnitTurnState = z.infer<typeof BattleUnitTurnStateSchema>;

// ── Identity types ───────────────────────────────────────────────────────────

/** Unique identifier for a LoR data entry (row id + numeric package id). */
export const EntryIdSchema = z.object({
  id: z.number(),
  packageId: z.number(),
});
export type EntryId = z.infer<typeof EntryIdSchema>;

/**
 * Entry identifier when the package is a mod/DLC string name rather than a
 * numeric id. Used in shared-inventory contexts (available cards, available
 * key pages) where the package identity is the workshop string key.
 */
export const StringEntryIdSchema = z.object({
  id: z.number(),
  packageId: z.string(),
});
export type StringEntryId = z.infer<typeof StringEntryIdSchema>;

// ── Stage ────────────────────────────────────────────────────────────────────

export const StageInfoSchema = z.object({
  floor: z.number().optional(),
  chapter: z.number().optional(),
  wave: z.number().optional(),
  round: z.number().optional(),
  /** Reception/stage name from StageClassInfo.stageName. */
  name: z.string().optional(),
  /** Story-chapter icon sprite ID; served at /assets/stageicons/<icon>.png. */
  icon: z.string().optional(),
  /** Glow layer rendered behind the icon (same sprite set as icon). */
  iconGlow: z.string().optional(),
});
export type StageInfo = z.infer<typeof StageInfoSchema>;

// ── Cards & dice ─────────────────────────────────────────────────────────────

/** Behaviour die inside a card (type, detail, min/max roll, optional script desc). */
export const DieSchema = z.object({
  type: z.string(),
  detail: z.string(),
  min: z.number(),
  max: z.number(),
  desc: z.string().optional(),
});
export type Die = z.infer<typeof DieSchema>;

/** Token/buff applied by a card (displayed as a small icon chip on the card). */
export const CardTokenSchema = z.object({
  label: z.string(),
  stack: z.number(),
  icon: z.string().optional(),
});
export type CardToken = z.infer<typeof CardTokenSchema>;

/** A playable battle card as returned in a unit's hand, deck, or EGO list. */
export const CardSchema = z.object({
  id: EntryIdSchema,
  index: z.number(),
  name: z.string(),
  cost: z.number(),
  /** Present when cost has been modified mid-round; used by costStyle(). */
  baseCost: z.number().optional(),
  range: z.string(),
  rarity: z.string().optional(),
  options: z.array(z.string()).optional(),
  allyTarget: z.boolean().optional(),
  canUse: z.boolean().optional(),
  emotionLimit: z.number().optional(),
  desc: z.string().optional(),
  flavorText: z.string().optional(),
  abilityDesc: z.string().optional(),
  dice: z.array(DieSchema).optional(),
  bufs: z.array(CardTokenSchema).optional(),
  icon: z.string().optional(),
});
export type Card = z.infer<typeof CardSchema>;

/** Secondary target slot for mass-range (FarArea/FarAreaEach) attacks. */
export const SubTargetSchema = z.object({
  targetUnitId: z.number(),
  targetSlot: z.number(),
});
export type SubTarget = z.infer<typeof SubTargetSchema>;

/** A card already slotted into a speed die — subset of Card plus targeting info. */
export const SlottedCardEntrySchema = z.object({
  cardIndex: z.number(),
  slot: z.number(),
  name: z.string(),
  cost: z.number(),
  targetUnitId: z.number().optional(),
  targetSlot: z.number().optional(),
  clash: z.boolean(),
  subTargets: z.array(SubTargetSchema).optional(),
  range: z.string(),
  desc: z.string().optional(),
  flavorText: z.string().optional(),
  dice: z.array(DieSchema).optional(),
});
export type SlottedCardEntry = z.infer<typeof SlottedCardEntrySchema>;

/** One speed die on a unit — carries its current rolled value and state. */
export const SpeedDieSchema = z.object({
  slot: z.number(),
  value: z.number(),
  type: z.string(),
  detail: z.string(),
  /** True when the die is staggered (shown as ✕). */
  staggered: z.boolean().optional(),
});
export type SpeedDie = z.infer<typeof SpeedDieSchema>;

// ── Unit metadata ────────────────────────────────────────────────────────────

export const EmotionCoinsSchema = z.object({
  positive: z.number(),
  negative: z.number(),
  max: z.number(),
});
export type EmotionCoins = z.infer<typeof EmotionCoinsSchema>;

/** A passive ability slot on a unit. */
export const PassiveSchema = z.object({
  id: EntryIdSchema,
  name: z.string(),
  desc: z.string().optional(),
  icon: z.string().optional(),
  isNegative: z.boolean().optional(),
  rare: z.string().optional(),
  cost: z.number().optional(),
  /** False when the passive cannot be attributed to another key page (unique). Absent = true. */
  canTransfer: z.boolean().optional(),
});
export type Passive = z.infer<typeof PassiveSchema>;

/** An active buff/debuff on a unit. */
export const BuffSchema = z.object({
  keywordId: z.string(),
  /** Internal engine key used as Vue list key. */
  type: z.string().optional(),
  name: z.string(),
  desc: z.string().optional(),
  stacks: z.number().optional(),
  icon: z.string().optional(),
  /** "Positive" | "Negative" — controls chip colour. */
  positive: z.string().optional(),
});
export type Buff = z.infer<typeof BuffSchema>;

/** Entry in a unit's abnormality/emotion-card list. */
export const AbnormalityEntrySchema = z.object({
  id: z.number(),
  name: z.string(),
  emotionLevel: z.number().optional(),
});
export type AbnormalityEntry = z.infer<typeof AbnormalityEntrySchema>;

export const ResistancesSchema = z.object({
  slashHp: z.string().optional(),
  pierceHp: z.string().optional(),
  bluntHp: z.string().optional(),
  slashBp: z.string().optional(),
  pierceBp: z.string().optional(),
  bluntBp: z.string().optional(),
});
export type Resistances = z.infer<typeof ResistancesSchema>;

export const KeyPageSchema = z.object({
  /** Unique identifier for managed (librarian) key pages; absent on transient battle unit key pages. */
  instanceId: z.number().optional(),
  name: z.string(),
  speedDiceCount: z.number().optional(),
  speedMin: z.number().optional(),
  speedMax: z.number().optional(),
  resistances: ResistancesSchema.optional(),
  /** Max HP including gift bonuses — present on librarian key pages. */
  hp: z.number().optional(),
  /** Break (stagger) gauge capacity — present on librarian key pages. */
  breakGauge: z.number().optional(),
  /** BookXmlInfo.RangeType: "Melee" | "Range" | "Hybrid" — determines which card ranges can be equipped. */
  equipRangeType: z.string().optional(),
  /** BookXmlInfo integer ID — used to load the body composite PNG for the preview. */
  bookId: z.number().optional(),
  /** Non-empty for workshop (mod) key pages; used with bookId to construct the body PNG URL. */
  bookPackageId: z.string().optional(),
});
export type KeyPage = z.infer<typeof KeyPageSchema>;

/** A passive attributed (succession-received) from another key page. */
export const AttributedPassiveSchema = z.object({
  passive: PassiveSchema,
  sourceInstanceId: z.number(),
  sourceName: z.string().optional(),
});
export type AttributedPassive = z.infer<typeof AttributedPassiveSchema>;

// ── Deck / floor / librarian ─────────────────────────────────────────────────

/** Card entry in a BattleSetting deck preview (grouped by card type with a count). */
export const DeckCardPreviewSchema = z.object({
  /** LorId of the card — present in librarian deck previews, absent in BattleSetting previews. */
  cardId: StringEntryIdSchema.optional(),
  name: z.string(),
  cost: z.number(),
  range: z.string(),
  rarity: z.string().optional(),
  /** How many copies of this card are in the deck. */
  count: z.number(),
  dice: z.array(DieSchema).optional(),
  abilityDesc: z.string().optional(),
});
export type DeckCardPreview = z.infer<typeof DeckCardPreviewSchema>;

export const EmotionCardEntrySchema = z.object({
  /** Floor realization level at which this card unlocks (1–6). */
  level: z.number(),
  name: z.string(),
  state: z.enum(["Positive", "Negative"]),
  targetType: z.enum(["All", "SelectOne", "AllIncludingEnemy"]),
  /** Emotion coin cost during battle. */
  emotionLevel: z.number(),
  /** Name of the abnormality this card belongs to (e.g. "Big Bird"). */
  abnormalityName: z.string().optional(),
  /** Ability description from AbnormalityCardDescXmlList (same source as battle selection). */
  desc: z.string().optional(),
  flavorText: z.string().optional(),
});
export type EmotionCardEntry = z.infer<typeof EmotionCardEntrySchema>;

/** Appearance customization fields mirroring UnitCustomizingData. Colors are [R, G, B] (0–255). */
export const AppearanceDataSchema = z.object({
  frontHairID: z.number(),
  backHairID: z.number(),
  eyeID: z.number(),
  browID: z.number(),
  mouthID: z.number(),
  /** Head sprite index (front=0, side=1). */
  headID: z.number().optional(),
  /** Patron sephirah ID — when set, the head uses head_special_{id}.png from the patron prefab. */
  patronHeadId: z.number().optional(),
  height: z.number(),
  hairColor: z.tuple([z.number(), z.number(), z.number()]),
  skinColor: z.tuple([z.number(), z.number(), z.number()]),
  eyeColor: z.tuple([z.number(), z.number(), z.number()]),
});
export type AppearanceData = z.infer<typeof AppearanceDataSchema>;

/** Per-type custom battle dialogue text. null = game uses a randomly-selected preset. */
export const DialogueDataSchema = z.object({
  startBattle: z.string().nullable(),
  victory: z.string().nullable(),
  death: z.string().nullable(),
  colleagueDeath: z.string().nullable(),
  killsOpponent: z.string().nullable(),
});
export type DialogueData = z.infer<typeof DialogueDataSchema>;

/** Gift-based title IDs (prefix = before name, suffix = after name). */
export const TitleDataSchema = z.object({
  prefixID: z.number(),
  postfixID: z.number(),
});
export type TitleData = z.infer<typeof TitleDataSchema>;

export const TitleOptionSchema = z.object({
  id: z.number(),
  text: z.string(),
});
export type TitleOption = z.infer<typeof TitleOptionSchema>;

// ── Gifts / battle symbols ───────────────────────────────────────────────────

/** Stat bonuses granted by an equipped gift. */
export const GiftStatSchema = z.object({
  hp: z.number(),
  breakGauge: z.number(),
  breakRecover: z.number(),
  /** Speed die min/max modifier. */
  tune: z.number(),
  /** Emotion coin gain modifier. */
  amp: z.number(),
});
export type GiftStat = z.infer<typeof GiftStatSchema>;

/** A gift equipped in one of the 9 positional slots. */
export const GiftSlotSchema = z.object({
  id: z.number(),
  name: z.string(),
  desc: z.string(),
  position: z.string(),
  stat: GiftStatSchema,
  visible: z.boolean(),
});
export type GiftSlot = z.infer<typeof GiftSlotSchema>;

/** An available (unlocked, unequipped) gift that can be placed in a slot. */
export const GiftOptionSchema = z.object({
  id: z.number(),
  name: z.string(),
  desc: z.string(),
  position: z.string(),
  stat: GiftStatSchema,
});
export type GiftOption = z.infer<typeof GiftOptionSchema>;

/** Per-librarian gift inventory: 9 equipped slots + available pool. */
export const GiftInventoryDataSchema = z.object({
  equipped: z.array(GiftSlotSchema.nullable()),
  available: z.array(GiftOptionSchema),
});
export type GiftInventoryData = z.infer<typeof GiftInventoryDataSchema>;

// ── Customize options ────────────────────────────────────────────────────────

/** A custom core book that can be used as an appearance projection skin. */
export const FashionBookSchema = z.object({
  id: z.number(),
  /** Non-empty for workshop (mod) books; omitted for core fashion books. */
  packageId: z.string().optional(),
  /** Explicit file stem override for body PNG lookup (e.g. "ws_2921128635"). */
  fileStem: z.string().optional(),
  name: z.string(),
  /** EquipRangeType string — controls compatibility with librarian's range type. */
  rangeType: z.string(),
  /**
   * True when the fashion skin overrides the head model in addition to the body
   * (skinType != "Lor" in BookXmlInfo). The composite face preview is not accurate
   * when this is true.
   */
  replacesHead: z.boolean(),
  /**
   * Z-axis rotation of the customPivot transform in degrees (positive = clockwise
   * in Unity's left-hand screen space). Omitted when zero. Applied to face/hair
   * CSS layers so the preview matches the in-game head tilt.
   */
  headTiltDeg: z.number().optional(),
  /** Pivot horizontal position as a fraction [0,1] of the canvas width (left=0). */
  pivotFracX: z.number().optional(),
  /** Pivot vertical position as a fraction [0,1] of the canvas height (top=0). */
  pivotFracY: z.number().optional(),
  /**
   * True when fashionbodies_front/{id}.png was extracted — some body sprites
   * render in front of the face overlay and must be composited above it.
   */
  hasFrontLayer: z.boolean().optional(),
  /**
   * True when the character model has a Hood sprite — the game hides all back
   * hair renderers in that case (RefreshAppearanceByMotion forcibly deactivates them).
   */
  hidesBackHair: z.boolean().optional(),
  /**
   * Gender variant of the skin (from BookXmlInfo.SkinGender): "F", "M", or omitted for "N".
   * When present, the body type toggle is enabled and body PNGs use a _f / _m suffix.
   */
  skinGender: z.string().optional(),
  /**
   * Vertical position of the character's feet within the body PNG, as a fraction
   * [0,1] from the top.  1.0 (or omitted) means feet sit at the PNG bottom;
   * smaller values indicate the PNG extends below feet (weapons/props), letting
   * the frontend offset inward when aligning feet to a shared floor line.
   */
  feetYFrac: z.number().optional(),
});
export type FashionBook = z.infer<typeof FashionBookSchema>;

/** A workshop cloth-overlay skin from CustomizingResourceLoader. */
export const WorkshopSkinSchema = z.object({
  id: z.number(),
  name: z.string(),
  /** Unique string key used to equip/save this skin (unit.workshopSkin). */
  contentFolderIdx: z.string(),
  /** True when the skin sprite replaces the entire head (no face/hair layers). */
  replacesHead: z.boolean().optional(),
  hasFrontLayer: z.boolean().optional(),
  headTiltDeg: z.number().optional(),
  pivotFracX: z.number().optional(),
  pivotFracY: z.number().optional(),
  feetYFrac: z.number().optional(),
});
export type WorkshopSkin = z.infer<typeof WorkshopSkinSchema>;

export const CustomizeOptionsSchema = z.object({
  suggestedNames: z.array(z.string()),
  prefixTitles: z.array(TitleOptionSchema),
  suffixTitles: z.array(TitleOptionSchema),
  // Mirrors the keyof DialogueData shape explicitly rather than a record, so
  // schema exports expose the same field names downstream consumers expect.
  dialoguePresets: z.object({
    startBattle: z.array(z.string()),
    victory: z.array(z.string()),
    death: z.array(z.string()),
    colleagueDeath: z.array(z.string()),
    killsOpponent: z.array(z.string()),
  }),
  fashionBooks: z.array(FashionBookSchema).optional(),
  workshopSkins: z.array(WorkshopSkinSchema).optional(),
});
export type CustomizeOptions = z.infer<typeof CustomizeOptionsSchema>;

/** Flat payload sent via setCustomization WebSocket action. */
export const CustomizePayloadSchema = z.object({
  floorIndex: z.number(),
  unitIndex: z.number(),
  frontHairID: z.number(),
  backHairID: z.number(),
  eyeID: z.number(),
  browID: z.number(),
  mouthID: z.number(),
  height: z.number(),
  hairR: z.number(),
  hairG: z.number(),
  hairB: z.number(),
  skinR: z.number(),
  skinG: z.number(),
  skinB: z.number(),
  eyeR: z.number(),
  eyeG: z.number(),
  eyeB: z.number(),
  dlgStartBattle: z.string().nullable(),
  dlgVictory: z.string().nullable(),
  dlgDeath: z.string().nullable(),
  dlgColleagueDeath: z.string().nullable(),
  dlgKillsOpponent: z.string().nullable(),
  prefixID: z.number(),
  postfixID: z.number(),
  /** -1 = unequip; any other value = BookXmlInfo ID to equip as appearance projection. */
  customBookId: z.number(),
  /** Non-empty when customBookId refers to a workshop book; omitted otherwise. */
  customBookPackageId: z.string().optional(),
  /**
   * contentFolderIdx of the workshop skin to equip, or "" to unequip.
   * Omitted from payload when unchanged (key absence means "do not change").
   */
  workshopSkin: z.string().optional(),
  /** Body type variant: "F", "M", or "N". */
  appearanceType: z.string(),
});
export type CustomizePayload = z.infer<typeof CustomizePayloadSchema>;

// ── Inventory ────────────────────────────────────────────────────────────────

/** A key page from the book inventory available to equip to a librarian. */
export const AvailableKeyPageSchema = z.object({
  instanceId: z.number(),
  name: z.string(),
  speedMin: z.number(),
  speedMax: z.number(),
  bookId: StringEntryIdSchema,
  /** Story/origin chapter number — mirrors BookXmlInfo.Chapter. */
  chapter: z.number(),
  /**
   * Story line identifier — mirrors BookXmlInfo.BookIcon (= UIStoryLine enum name,
   * e.g. "Rats", "Chapter1"). Used to group key pages the same way the in-game
   * equip screen does.
   */
  bookIcon: z.string(),
  /** Resolved display name for the book group header (e.g. "The Stray Dogs"). */
  bookGroupName: z.string(),
  hp: z.number(),
  breakGauge: z.number(),
  /** BookXmlInfo.RangeType: "Melee" | "Range" | "Hybrid" — determines which card ranges can be equipped. */
  equipRangeType: z.string(),
  resistances: ResistancesSchema,
  passives: z.array(PassiveSchema),
  /** False when this key page's passives are already attributed to another page. Absent = available. */
  canGivePassive: z.boolean().optional(),
  /** Name of the librarian this key page's passives are attributed to. */
  passiveGivenTo: z.string().optional(),
});
export type AvailableKeyPage = z.infer<typeof AvailableKeyPageSchema>;

/** A card from the shared inventory available to add to a librarian's deck. */
export const AvailableCardSchema = z.object({
  cardId: StringEntryIdSchema,
  name: z.string(),
  cost: z.number(),
  range: z.string(),
  rarity: z.string(),
  count: z.number(),
  abilityDesc: z.string().optional(),
  dice: z.array(DieSchema).optional(),
  chapter: z.number().optional(),
});
export type AvailableCard = z.infer<typeof AvailableCardSchema>;

// ── Abnormality selection phase ──────────────────────────────────────────────

export const AbnormalityChoiceSchema = z.object({
  id: z.number(),
  name: z.string(),
  emotionLevel: z.number(),
  targetType: z.string(),
  state: z.string(),
  desc: z.string().optional(),
  flavorText: z.string().optional(),
});
export type AbnormalityChoice = z.infer<typeof AbnormalityChoiceSchema>;

export const AbnormalitySelectionSchema = z.object({
  choices: z.array(AbnormalityChoiceSchema),
  teamEmotionLevel: z.number().optional(),
  teamCoin: z.number().optional(),
  teamCoinMax: z.number().optional(),
  teamPositiveCoins: z.number().optional(),
  teamNegativeCoins: z.number().optional(),
});
export type AbnormalitySelection = z.infer<typeof AbnormalitySelectionSchema>;

// ── Librarian ────────────────────────────────────────────────────────────────

export const LibrarianEntrySchema = z.object({
  floorIndex: z.number(),
  unitIndex: z.number(),
  name: z.string(),
  keyPage: KeyPageSchema,
  passives: z.array(PassiveSchema),
  attributedPassives: z.array(AttributedPassiveSchema).optional(),
  passiveSlotCount: z.number().optional(),
  maxPassiveCost: z.number().optional(),
  currentPassiveCost: z.number().optional(),
  sourceKeyPageIds: z.array(z.number()).optional(),
  deckPreview: z.array(DeckCardPreviewSchema),
  /** Session ID of the player currently editing this librarian, or null. */
  lockedBy: z.string().nullable(),
  /**
   * Page-exclusive cards (CardOption.OnlyPage) belonging to this key page
   * that are currently in the shared inventory. Empty array when none exist.
   * Presented first in the deck editor's add-cards list.
   */
  onlyCards: z.array(AvailableCardSchema).optional(),
  /** True for sephirah (patron) librarians — name editing and face/hair customization disabled. */
  isSephirah: z.boolean().optional(),
  /** Appearance customization data (present for customizable librarians). */
  appearance: AppearanceDataSchema.optional(),
  /** Per-type custom battle dialogue text (null = using a random game preset). */
  dialogue: DialogueDataSchema.optional(),
  /** Title gift IDs for the name bar prefix and suffix. */
  titles: TitleDataSchema.optional(),
  /**
   * ID of the custom core book currently used as an appearance projection, or -1 if none.
   * Set by EquipCustomCoreBook; persisted in save data as customcorebookInstanceId.
   */
  customBookId: z.number().optional(),
  /** Non-empty when customBookId refers to a workshop book; omitted otherwise. */
  customBookPackageId: z.string().optional(),
  /**
   * contentFolderIdx of the active workshop cloth-overlay skin; omitted when none.
   * Equipped via the CustomizingResourceLoader system (separate from fashionBook).
   */
  workshopSkin: z.string().optional(),
  /**
   * Active body type variant: "F" (female), "M" (male), or "N" (neutral).
   * Controls which _F/_M/_N prefab suffix is loaded in-game.
   */
  appearanceType: z.string().optional(),
  /**
   * SkinGender of the active skin source (fashion book or key page): "F" or "M".
   * Omitted when "N" (no gendered variants exist, body type toggle disabled).
   */
  skinGender: z.string().optional(),
  /** Equipped and available battle symbols (gifts). */
  gifts: GiftInventoryDataSchema.optional(),
  /** Equipped key page has a body composite in fashionbodies/ (replacesHead behavior). */
  keyPageReplacesHead: z.boolean().optional(),
  /** Equipped key page has a front-layer composite in fashionbodies_front/. */
  keyPageHasFrontLayer: z.boolean().optional(),
  /** Equipped key page body has a head tilt (Z-axis rotation). */
  keyPageHeadTiltDeg: z.number().optional(),
  keyPagePivotFracX: z.number().optional(),
  keyPagePivotFracY: z.number().optional(),
  /** Equipped key page has a Hood sprite — back hair should be hidden. */
  keyPageHidesBackHair: z.boolean().optional(),
  /** SkinGender of the equipped key page skin: "F" or "M". */
  keyPageSkinGender: z.string().optional(),
  /** Feet-Y fraction of the equipped key page body PNG (see FashionBook.feetYFrac). */
  keyPageFeetYFrac: z.number().optional(),
});
export type LibrarianEntry = z.infer<typeof LibrarianEntrySchema>;

export const FloorEntrySchema = z.object({
  floorIndex: z.number(),
  /** Localized floor name from the game's text system (e.g. "Malkuth"). */
  officialName: z.string(),
  /** Current realization level (1–6). */
  realizationLevel: z.number(),
  /**
   * EGO pages: full battle cards (with dice) from EmotionEgoXmlList.
   * These are distinct from abnormality pages — they are full combat card
   * definitions serialized identically to deckPreview cards.
   */
  egoCards: z.array(DeckCardPreviewSchema),
  /** All abnormality pages (Awakening/Breakdown) unlocked up to the current realization level. */
  emotionCards: z.array(EmotionCardEntrySchema),
  librarians: z.array(LibrarianEntrySchema),
});
export type FloorEntry = z.infer<typeof FloorEntrySchema>;

// ── Unit shapes ──────────────────────────────────────────────────────────────

/** Fields shared by both ally and enemy units. */
export const UnitSchema = z.object({
  id: z.number(),
  name: z.string().optional(),
  hp: z.number(),
  maxHp: z.number(),
  staggerGauge: z.number(),
  maxStaggerGauge: z.number(),
  staggerThreshold: z.number(),
  targetable: z.boolean(),
  turnState: BattleUnitTurnStateSchema,
  speedDice: z.array(SpeedDieSchema),
  slottedCards: z.array(SlottedCardEntrySchema),
  passives: z.array(PassiveSchema),
  buffs: z.array(BuffSchema),
  abnormalities: z.array(AbnormalityEntrySchema),
  emotionLevel: z.number(),
  emotionCoins: EmotionCoinsSchema,
  keyPage: KeyPageSchema.optional(),
  /**
   * False when the unit is dead or locked — only present in BattleSetting phase.
   * Undefined (absent) during battle means no restriction.
   */
  enabled: z.boolean().optional(),
  /** Deck card preview — only present in BattleSetting phase. */
  deckPreview: z.array(DeckCardPreviewSchema).optional(),
});
export type Unit = z.infer<typeof UnitSchema>;

/** Ally-only extras: light, hand, deck, EGO. */
export const AllyUnitSchema = UnitSchema.extend({
  light: z.number(),
  maxLight: z.number(),
  reservedLight: z.number(),
  /** Present when this ally is owned by the current session. */
  hand: z.array(CardSchema).optional(),
  deck: z.array(CardSchema).optional(),
  ego: z.array(CardSchema).optional(),
  teamHand: z.array(CardSchema).optional(),
  /**
   * Present instead of hand/deck/ego when this ally belongs to another
   * session. The server sends counts rather than card data to preserve privacy.
   */
  handCount: z.number().optional(),
  deckCount: z.number().optional(),
  egoCount: z.number().optional(),
});
export type AllyUnit = z.infer<typeof AllyUnitSchema>;

// ── Session & WebSocket ──────────────────────────────────────────────────────

/** The current browser tab's session identity. */
export const SessionStateSchema = z.object({
  sessionId: z.string(),
  /** Unit IDs this session has claimed. */
  assignedUnits: z.array(z.number()),
  /** Whether the server is enforcing unit claims. False means everyone can play any unit. */
  claimsEnabled: z.boolean(),
});
export type SessionState = z.infer<typeof SessionStateSchema>;

/** One connected player as reported in a playerList message. */
export const PlayerInfoSchema = z.object({
  sessionId: z.string(),
  name: z.string(),
  units: z.array(z.number()),
});
export type PlayerInfo = z.infer<typeof PlayerInfoSchema>;

/** Result returned when a WebSocket action resolves. */
export const ActionResultSchema = z.object({
  ok: z.boolean(),
  error: z.string().optional(),
});
export type ActionResult = z.infer<typeof ActionResultSchema>;

// ── Root state ───────────────────────────────────────────────────────────────

/** Top-level WebSocket `state` payload. */
export const GameStateSchema = z.object({
  scene: SceneNameSchema,
  /** Whether the server has finished extracting appearance/gift sprite assets. */
  assetsReady: z.boolean().optional(),
  /** Active stage phase class name (e.g. "ApplyLibrarianCardPhase"). */
  phase: z.string().optional(),
  /** Active stage state enum value (e.g. "BattleSetting"). */
  stageState: z.string().optional(),
  uiPhase: z.string().optional(),
  stage: StageInfoSchema.optional(),
  allies: z.array(AllyUnitSchema).optional(),
  enemies: z.array(UnitSchema).optional(),
  /** Only present during the key-page selection phase. */
  abnormalitySelection: AbnormalitySelectionSchema.optional(),
  /** Present in main scene (non-BattleSetting) — floor roster with nested librarians. */
  floors: z.array(FloorEntrySchema).optional(),
  /** Key pages in the book inventory available to equip to a librarian. */
  availableKeyPages: z.array(AvailableKeyPageSchema).optional(),
  /** Cards in the shared inventory available to add to a librarian's deck. */
  availableCards: z.array(AvailableCardSchema).optional(),
  /** Global customization option tables (names, title lists, dialogue presets). */
  customizeOptions: CustomizeOptionsSchema.optional(),
});
export type GameState = z.infer<typeof GameStateSchema>;

// ── Message envelopes ────────────────────────────────────────────────────────

/** Server → client WebSocket message types. */
export const ServerMessageSchema = z.discriminatedUnion("type", [
  z.object({
    type: z.literal("hello"),
    sessionId: z.string(),
    assignedUnits: z.array(z.number()),
    claimsEnabled: z.boolean(),
  }),
  z.object({
    type: z.literal("state"),
    seq: z.number(),
    data: GameStateSchema,
  }),
  z.object({
    type: z.literal("delta"),
    seq: z.number(),
    data: z.record(z.string(), z.unknown()),
  }),
  z.object({
    type: z.literal("sessionUpdate"),
    assignedUnits: z.array(z.number()),
  }),
  z.object({
    type: z.literal("playerList"),
    players: z.array(PlayerInfoSchema),
  }),
  z.object({
    type: z.literal("actionResult"),
    reqId: z.string(),
    ok: z.boolean(),
    error: z.string().optional(),
  }),
  z.object({
    type: z.literal("ping"),
  }),
]);
export type ServerMessage = z.infer<typeof ServerMessageSchema>;

/** Client → server action payloads. */
export const ClientActionSchema = z.discriminatedUnion("type", [
  z.object({
    type: z.literal("playCard"),
    unitId: z.number(),
    cardIndex: z.number(),
    diceSlot: z.number(),
    targetUnitId: z.number().optional(),
    targetDiceSlot: z.number().optional(),
    isEgo: z.literal(1).optional(),
  }),
  z.object({
    type: z.literal("removeCard"),
    unitId: z.number(),
    diceSlot: z.number(),
  }),
  z.object({
    type: z.literal("confirm"),
  }),
  z.object({
    type: z.literal("selectAbnormality"),
    cardId: z.number(),
    targetUnitId: z.number().optional(),
  }),
  // setCustomization carries the full appearance/dialogue/title payload for
  // a single librarian (floorIndex + unitIndex address it). Field list mirrors
  // CustomizePayloadSchema exactly — kept inline here (rather than via
  // `.extend()`) so the discriminated union remains a plain ZodObject.
  z.object({
    type: z.literal("setCustomization"),
    floorIndex: z.number(),
    unitIndex: z.number(),
    frontHairID: z.number(),
    backHairID: z.number(),
    eyeID: z.number(),
    browID: z.number(),
    mouthID: z.number(),
    height: z.number(),
    hairR: z.number(),
    hairG: z.number(),
    hairB: z.number(),
    skinR: z.number(),
    skinG: z.number(),
    skinB: z.number(),
    eyeR: z.number(),
    eyeG: z.number(),
    eyeB: z.number(),
    dlgStartBattle: z.string().nullable(),
    dlgVictory: z.string().nullable(),
    dlgDeath: z.string().nullable(),
    dlgColleagueDeath: z.string().nullable(),
    dlgKillsOpponent: z.string().nullable(),
    prefixID: z.number(),
    postfixID: z.number(),
    customBookId: z.number(),
    customBookPackageId: z.string().optional(),
    workshopSkin: z.string().optional(),
    appearanceType: z.string(),
  }),
  // setGifts is a sparse batch update — callers send only the (position, key)
  // pairs that changed (see BattleSymbolsTab: one key per click). The server
  // reads keys `gift0`–`gift8` (id, or -1 to unequip) and `vis0`–`vis8`
  // (visibility, non-zero = shown). Every slot is optional; absent = no change.
  z.object({
    type: z.literal("setGifts"),
    floorIndex: z.number(),
    unitIndex: z.number(),
    gift0: z.number().optional(),
    gift1: z.number().optional(),
    gift2: z.number().optional(),
    gift3: z.number().optional(),
    gift4: z.number().optional(),
    gift5: z.number().optional(),
    gift6: z.number().optional(),
    gift7: z.number().optional(),
    gift8: z.number().optional(),
    vis0: z.number().optional(),
    vis1: z.number().optional(),
    vis2: z.number().optional(),
    vis3: z.number().optional(),
    vis4: z.number().optional(),
    vis5: z.number().optional(),
    vis6: z.number().optional(),
    vis7: z.number().optional(),
    vis8: z.number().optional(),
  }),
]);
export type ClientAction = z.infer<typeof ClientActionSchema>;
