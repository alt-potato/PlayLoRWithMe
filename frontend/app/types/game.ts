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

import { z } from "zod/mini";

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
  floor: z.optional(z.number()),
  chapter: z.optional(z.number()),
  wave: z.optional(z.number()),
  round: z.optional(z.number()),
  /** Reception/stage name from StageClassInfo.stageName. */
  name: z.optional(z.string()),
  /** Story-chapter icon sprite ID; served at /assets/stageicons/<icon>.png. */
  icon: z.optional(z.string()),
  /** Glow layer rendered behind the icon (same sprite set as icon). */
  iconGlow: z.optional(z.string()),
});
export type StageInfo = z.infer<typeof StageInfoSchema>;

// ── Cards & dice ─────────────────────────────────────────────────────────────

/** Behaviour die inside a card (type, detail, min/max roll, optional script desc). */
export const DieSchema = z.object({
  type: z.string(),
  detail: z.string(),
  min: z.number(),
  max: z.number(),
  desc: z.optional(z.string()),
});
export type Die = z.infer<typeof DieSchema>;

/** Token/buff applied by a card (displayed as a small icon chip on the card). */
export const CardTokenSchema = z.object({
  label: z.string(),
  stack: z.number(),
  icon: z.optional(z.string()),
});
export type CardToken = z.infer<typeof CardTokenSchema>;

/** A playable battle card as returned in a unit's hand, deck, or EGO list. */
export const CardSchema = z.object({
  id: EntryIdSchema,
  index: z.number(),
  name: z.string(),
  cost: z.number(),
  /** Present when cost has been modified mid-round; used by costStyle(). */
  baseCost: z.optional(z.number()),
  range: z.string(),
  rarity: z.optional(z.string()),
  options: z.optional(z.array(z.string())),
  allyTarget: z.optional(z.boolean()),
  canUse: z.optional(z.boolean()),
  emotionLimit: z.optional(z.number()),
  desc: z.optional(z.string()),
  flavorText: z.optional(z.string()),
  abilityDesc: z.optional(z.string()),
  dice: z.optional(z.array(DieSchema)),
  bufs: z.optional(z.array(CardTokenSchema)),
  icon: z.optional(z.string()),
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
  targetUnitId: z.optional(z.number()),
  targetSlot: z.optional(z.number()),
  clash: z.boolean(),
  subTargets: z.optional(z.array(SubTargetSchema)),
  range: z.string(),
  desc: z.optional(z.string()),
  flavorText: z.optional(z.string()),
  dice: z.optional(z.array(DieSchema)),
});
export type SlottedCardEntry = z.infer<typeof SlottedCardEntrySchema>;

/**
 * One speed die on a unit — carries its current rolled value and state.
 *
 * Note: no `type`/`detail` here. Behaviour type (Atk/Def/Standby) and detail
 * (Slash/Penetrate/Hit/Guard/Evasion) are properties of `DiceBehaviour` inside
 * a card, not of the speed die itself. The C# serializer's `WriteSpeedDice`
 * has never emitted them, so requiring them here would only generate
 * dev-mode `[wire-contract]` log noise on every state push.
 */
export const SpeedDieSchema = z.object({
  slot: z.number(),
  value: z.number(),
  /** True when the die is staggered (shown as ✕). */
  staggered: z.optional(z.boolean()),
  /**
   * True when the in-game `SpeedDiceSetter` would render the lock overlay —
   * specifically when the owning unit has Stun (`KeywordBuf.Stun`). Vanilla
   * does not raise the lock root for any other condition, so the frontend
   * only draws the glyph for this case to match the game.
   */
  locked: z.optional(z.boolean()),
  /**
   * False when this individual die is disabled by a card effect (e.g. clock
   * EGO). Vanilla shows the die normally and just bounces clicks; the
   * frontend mirrors that — the die keeps its beckon, and a red rejection
   * flash plays on click. Default-true (omitted) means controllable.
   */
  controllable: z.optional(z.boolean()),
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
  desc: z.optional(z.string()),
  icon: z.optional(z.string()),
  isNegative: z.optional(z.boolean()),
  rare: z.optional(z.string()),
  cost: z.optional(z.number()),
  /** False when the passive cannot be attributed to another key page (unique). Absent = true. */
  canTransfer: z.optional(z.boolean()),
});
export type Passive = z.infer<typeof PassiveSchema>;

/** An active buff/debuff on a unit. */
export const BuffSchema = z.object({
  keywordId: z.string(),
  /** Internal engine key used as Vue list key. */
  type: z.optional(z.string()),
  name: z.string(),
  desc: z.optional(z.string()),
  stacks: z.optional(z.number()),
  icon: z.optional(z.string()),
  /** "Positive" | "Negative" — controls chip colour. */
  positive: z.optional(z.string()),
});
export type Buff = z.infer<typeof BuffSchema>;

/**
 * Entry in a unit's abnormality/emotion-card list. `state` is the raw
 * `MentalState` enum from the game ("Positive" | "Negative"); the UI treats
 * anything else as a fallback styling bucket. `desc` and `flavorText` mirror
 * `AbnormalityCardDescXmlList` and are absent when the entry has no matching
 * description xml.
 */
export const AbnormalityEntrySchema = z.object({
  id: z.number(),
  name: z.string(),
  emotionLevel: z.optional(z.number()),
  state: z.optional(z.string()),
  desc: z.optional(z.string()),
  flavorText: z.optional(z.string()),
});
export type AbnormalityEntry = z.infer<typeof AbnormalityEntrySchema>;

export const ResistancesSchema = z.object({
  slashHp: z.optional(z.string()),
  pierceHp: z.optional(z.string()),
  bluntHp: z.optional(z.string()),
  slashBp: z.optional(z.string()),
  pierceBp: z.optional(z.string()),
  bluntBp: z.optional(z.string()),
});
export type Resistances = z.infer<typeof ResistancesSchema>;

export const KeyPageSchema = z.object({
  /** Unique identifier for managed (librarian) key pages; absent on transient battle unit key pages. */
  instanceId: z.optional(z.number()),
  name: z.string(),
  speedDiceCount: z.optional(z.number()),
  speedMin: z.optional(z.number()),
  speedMax: z.optional(z.number()),
  resistances: z.optional(ResistancesSchema),
  /** Max HP including gift bonuses — present on librarian key pages. */
  hp: z.optional(z.number()),
  /** Break (stagger) gauge capacity — present on librarian key pages. */
  breakGauge: z.optional(z.number()),
  /** BookXmlInfo.RangeType: "Melee" | "Range" | "Hybrid" — determines which card ranges can be equipped. */
  equipRangeType: z.optional(z.string()),
  /** BookXmlInfo integer ID — used to load the body composite PNG for the preview. */
  bookId: z.optional(z.number()),
  /** Non-empty for workshop (mod) key pages; used with bookId to construct the body PNG URL. */
  bookPackageId: z.optional(z.string()),
  /**
   * BookXmlInfo.Rarity: "Common" | "Uncommon" | "Rare" | "Unique" | "Special".
   * Emitted on librarian-owned key pages only — battle-context emission sites
   * intentionally omit this so combat surfaces never display a rarity outline.
   */
  rarity: z.optional(z.string()),
  /**
   * True when the equipped key page has the `BookOption.MultiDeck` flag (e.g.
   * The Purple Tear). Drives whether the deck editor exposes per-stance tabs.
   * Emitted on librarian-owned key pages only — battle-context emission sites
   * omit this for consistency with the other librarian-only fields.
   */
  isMultiDeck: z.optional(z.boolean()),
});
export type KeyPage = z.infer<typeof KeyPageSchema>;

/** A passive attributed (succession-received) from another key page. */
export const AttributedPassiveSchema = z.object({
  passive: PassiveSchema,
  sourceInstanceId: z.number(),
  sourceName: z.optional(z.string()),
});
export type AttributedPassive = z.infer<typeof AttributedPassiveSchema>;

// ── Deck / floor / librarian ─────────────────────────────────────────────────

/** Card entry in a BattleSetting deck preview (grouped by card type with a count). */
export const DeckCardPreviewSchema = z.object({
  /** LorId of the card — present in librarian deck previews, absent in BattleSetting previews. */
  cardId: z.optional(StringEntryIdSchema),
  name: z.string(),
  cost: z.number(),
  range: z.string(),
  rarity: z.optional(z.string()),
  /** How many copies of this card are in the deck. */
  count: z.number(),
  dice: z.optional(z.array(DieSchema)),
  abilityDesc: z.optional(z.string()),
});
export type DeckCardPreview = z.infer<typeof DeckCardPreviewSchema>;

/**
 * One slot of a librarian's per-key-page deck list. Single-deck key pages emit
 * a length-1 array (`index: 0`, no `label`). Multi-deck key pages
 * (`KeyPage.isMultiDeck === true`, e.g. The Purple Tear) emit a length-4 array
 * with `index` 0..3 in ascending order. The `label` field is intentionally
 * absent on the wire — the frontend resolves stance/deck labels client-side via
 * `utils/multiDeckLabels.ts` so localization stays out of the C# serializer.
 */
export const DeckPreviewSchema = z.object({
  index: z.number(),
  label: z.optional(z.string()),
  cards: z.array(DeckCardPreviewSchema),
});
export type DeckPreview = z.infer<typeof DeckPreviewSchema>;

export const EmotionCardEntrySchema = z.object({
  /** Floor realization level at which this card unlocks (1–6). */
  level: z.number(),
  name: z.string(),
  state: z.enum(["Positive", "Negative"]),
  targetType: z.enum(["All", "SelectOne", "AllIncludingEnemy"]),
  /** Emotion coin cost during battle. */
  emotionLevel: z.number(),
  /** Name of the abnormality this card belongs to (e.g. "Big Bird"). */
  abnormalityName: z.optional(z.string()),
  /** Ability description from AbnormalityCardDescXmlList (same source as battle selection). */
  desc: z.optional(z.string()),
  flavorText: z.optional(z.string()),
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
  headID: z.optional(z.number()),
  /** Patron sephirah ID — when set, the head uses head_special_{id}.png from the patron prefab. */
  patronHeadId: z.optional(z.number()),
  height: z.number(),
  hairColor: z.tuple([z.number(), z.number(), z.number()]),
  skinColor: z.tuple([z.number(), z.number(), z.number()]),
  eyeColor: z.tuple([z.number(), z.number(), z.number()]),
});
export type AppearanceData = z.infer<typeof AppearanceDataSchema>;

/** Per-type custom battle dialogue text. null = game uses a randomly-selected preset. */
export const DialogueDataSchema = z.object({
  startBattle: z.nullable(z.string()),
  victory: z.nullable(z.string()),
  death: z.nullable(z.string()),
  colleagueDeath: z.nullable(z.string()),
  killsOpponent: z.nullable(z.string()),
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
  equipped: z.array(z.nullable(GiftSlotSchema)),
  available: z.array(GiftOptionSchema),
});
export type GiftInventoryData = z.infer<typeof GiftInventoryDataSchema>;

// ── Customize options ────────────────────────────────────────────────────────

/** A custom core book that can be used as an appearance projection skin. */
export const FashionBookSchema = z.object({
  id: z.number(),
  /** Non-empty for workshop (mod) books; omitted for core fashion books. */
  packageId: z.optional(z.string()),
  /** Explicit file stem override for body PNG lookup (e.g. "ws_2921128635"). */
  fileStem: z.optional(z.string()),
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
  headTiltDeg: z.optional(z.number()),
  /** Pivot horizontal position as a fraction [0,1] of the canvas width (left=0). */
  pivotFracX: z.optional(z.number()),
  /** Pivot vertical position as a fraction [0,1] of the canvas height (top=0). */
  pivotFracY: z.optional(z.number()),
  /**
   * True when fashionbodies_front/{id}.png was extracted — some body sprites
   * render in front of the face overlay and must be composited above it.
   */
  hasFrontLayer: z.optional(z.boolean()),
  /**
   * True when the character model has a Hood sprite — the game hides all back
   * hair renderers in that case (RefreshAppearanceByMotion forcibly deactivates them).
   */
  hidesBackHair: z.optional(z.boolean()),
  /**
   * Gender variant of the skin (from BookXmlInfo.SkinGender): "F", "M", or omitted for "N".
   * When present, the body type toggle is enabled and body PNGs use a _f / _m suffix.
   */
  skinGender: z.optional(z.string()),
  /**
   * Vertical position of the character's feet within the body PNG, as a fraction
   * [0,1] from the top.  1.0 (or omitted) means feet sit at the PNG bottom;
   * smaller values indicate the PNG extends below feet (weapons/props), letting
   * the frontend offset inward when aligning feet to a shared floor line.
   */
  feetYFrac: z.optional(z.number()),
  /**
   * Pixel dimensions of the extracted body PNG.  Supplied by the server so the
   * preview can lay out the body layer synchronously instead of waiting for an
   * @load event to measure the image — required to avoid a feet-snap on first
   * paint and on tab switches.  Omitted when extraction hasn't populated dims.
   */
  bodyW: z.optional(z.number()),
  bodyH: z.optional(z.number()),
});
export type FashionBook = z.infer<typeof FashionBookSchema>;

/** A workshop cloth-overlay skin from CustomizingResourceLoader. */
export const WorkshopSkinSchema = z.object({
  id: z.number(),
  name: z.string(),
  /** Unique string key used to equip/save this skin (unit.workshopSkin). */
  contentFolderIdx: z.string(),
  /** True when the skin sprite replaces the entire head (no face/hair layers). */
  replacesHead: z.optional(z.boolean()),
  hasFrontLayer: z.optional(z.boolean()),
  headTiltDeg: z.optional(z.number()),
  pivotFracX: z.optional(z.number()),
  pivotFracY: z.optional(z.number()),
  feetYFrac: z.optional(z.number()),
  bodyW: z.optional(z.number()),
  bodyH: z.optional(z.number()),
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
  fashionBooks: z.optional(z.array(FashionBookSchema)),
  workshopSkins: z.optional(z.array(WorkshopSkinSchema)),
  /**
   * Pixel dimensions of the shared face/hair canvas (AppearanceCache's
   * extracted sprite size). Supplied so AppearancePreview can compute the
   * head-tilt transform origin synchronously instead of fetching
   * /assets/customize/dimensions.json after mount — that fetch caused a
   * visible head-snap when switching floor tabs because each remount
   * started with dims=null and only updated after the fetch resolved.
   */
  faceCanvasW: z.optional(z.number()),
  faceCanvasH: z.optional(z.number()),
});
export type CustomizeOptions = z.infer<typeof CustomizeOptionsSchema>;

/**
 * Flat payload sent via setCustomization WebSocket action.
 *
 * Field optionality mirrors the C# HandleSetCustomization behaviour: each
 * face/hair/color/dialogue field is read with TryGet, so an absent key means
 * "do not change". Callers that gate edits on patron status (no face/hair) or
 * dialogue availability omit those keys rather than sending sentinel values.
 */
export const CustomizePayloadSchema = z.object({
  floorIndex: z.number(),
  unitIndex: z.number(),
  // Face/hair: omitted by callers when the unit is a patron (sephirah head)
  frontHairID: z.optional(z.number()),
  backHairID: z.optional(z.number()),
  eyeID: z.optional(z.number()),
  browID: z.optional(z.number()),
  mouthID: z.optional(z.number()),
  height: z.number(),
  // Color components: omitted alongside face/hair for patrons
  hairR: z.optional(z.number()),
  hairG: z.optional(z.number()),
  hairB: z.optional(z.number()),
  skinR: z.optional(z.number()),
  skinG: z.optional(z.number()),
  skinB: z.optional(z.number()),
  eyeR: z.optional(z.number()),
  eyeG: z.optional(z.number()),
  eyeB: z.optional(z.number()),
  // Dialogue: omitted when the unit lacks a BattleDialogueModel or is a patron.
  // Empty string restores a random game preset; null means "no change".
  dlgStartBattle: z.optional(z.nullable(z.string())),
  dlgVictory: z.optional(z.nullable(z.string())),
  dlgDeath: z.optional(z.nullable(z.string())),
  dlgColleagueDeath: z.optional(z.nullable(z.string())),
  dlgKillsOpponent: z.optional(z.nullable(z.string())),
  prefixID: z.number(),
  postfixID: z.number(),
  /** -1 = unequip; any other value = BookXmlInfo ID to equip as appearance projection. */
  customBookId: z.number(),
  /** Non-empty when customBookId refers to a workshop book; omitted otherwise. */
  customBookPackageId: z.optional(z.string()),
  /**
   * contentFolderIdx of the workshop skin to equip, or "" to unequip.
   * Omitted from payload when unchanged (key absence means "do not change").
   */
  workshopSkin: z.optional(z.string()),
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
  canGivePassive: z.optional(z.boolean()),
  /** Name of the librarian this key page's passives are attributed to. */
  passiveGivenTo: z.optional(z.string()),
  /** BookXmlInfo.Rarity: "Common" | "Uncommon" | "Rare" | "Unique" | "Special". */
  rarity: z.optional(z.string()),
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
  abilityDesc: z.optional(z.string()),
  dice: z.optional(z.array(DieSchema)),
  chapter: z.optional(z.number()),
});
export type AvailableCard = z.infer<typeof AvailableCardSchema>;

// ── Abnormality selection phase ──────────────────────────────────────────────

export const AbnormalityChoiceSchema = z.object({
  id: z.number(),
  name: z.string(),
  emotionLevel: z.number(),
  targetType: z.string(),
  state: z.string(),
  desc: z.optional(z.string()),
  flavorText: z.optional(z.string()),
});
export type AbnormalityChoice = z.infer<typeof AbnormalityChoiceSchema>;

export const AbnormalitySelectionSchema = z.object({
  choices: z.array(AbnormalityChoiceSchema),
  teamEmotionLevel: z.optional(z.number()),
  teamCoin: z.optional(z.number()),
  teamCoinMax: z.optional(z.number()),
  teamPositiveCoins: z.optional(z.number()),
  teamNegativeCoins: z.optional(z.number()),
});
export type AbnormalitySelection = z.infer<typeof AbnormalitySelectionSchema>;

// ── Librarian ────────────────────────────────────────────────────────────────

export const LibrarianEntrySchema = z.object({
  floorIndex: z.number(),
  unitIndex: z.number(),
  name: z.string(),
  keyPage: KeyPageSchema,
  /**
   * The librarian's immutable base (origin) key page — `UnitDataModel.defaultBook`
   * in the engine. The game auto-equips this whenever `_bookItem` is null, so
   * "unequip" is implemented as falling back to this page. Carries the same
   * shape as `keyPage`; when the librarian is currently on their base, the two
   * objects agree on `instanceId` (and all detail fields).
   *
   * Librarian-management only — battle-unit `keyPage` payloads never include
   * a sibling `baseKeyPage` field.
   */
  baseKeyPage: KeyPageSchema,
  passives: z.array(PassiveSchema),
  attributedPassives: z.optional(z.array(AttributedPassiveSchema)),
  passiveSlotCount: z.optional(z.number()),
  maxPassiveCost: z.optional(z.number()),
  currentPassiveCost: z.optional(z.number()),
  sourceKeyPageIds: z.optional(z.array(z.number())),
  /**
   * Per-deck-slot card lists for the librarian's equipped key page. Length 1
   * for single-deck books (the entry's `index === 0`, no `label`); length 4 for
   * multi-deck books (`keyPage.isMultiDeck === true`) with `index` 0..3.
   */
  decks: z.array(DeckPreviewSchema),
  /** Session ID of the player currently editing this librarian, or null. */
  lockedBy: z.nullable(z.string()),
  /**
   * Page-exclusive cards (CardOption.OnlyPage) belonging to this key page
   * that are currently in the shared inventory. Empty array when none exist.
   * Presented first in the deck editor's add-cards list.
   */
  onlyCards: z.optional(z.array(AvailableCardSchema)),
  /** True for sephirah (patron) librarians — name editing and face/hair customization disabled. */
  isSephirah: z.optional(z.boolean()),
  /** Appearance customization data (present for customizable librarians). */
  appearance: z.optional(AppearanceDataSchema),
  /** Per-type custom battle dialogue text (null = using a random game preset). */
  dialogue: z.optional(DialogueDataSchema),
  /** Title gift IDs for the name bar prefix and suffix. */
  titles: z.optional(TitleDataSchema),
  /**
   * ID of the custom core book currently used as an appearance projection, or -1 if none.
   * Set by EquipCustomCoreBook; persisted in save data as customcorebookInstanceId.
   */
  customBookId: z.optional(z.number()),
  /** Non-empty when customBookId refers to a workshop book; omitted otherwise. */
  customBookPackageId: z.optional(z.string()),
  /**
   * contentFolderIdx of the active workshop cloth-overlay skin; omitted when none.
   * Equipped via the CustomizingResourceLoader system (separate from fashionBook).
   */
  workshopSkin: z.optional(z.string()),
  /**
   * Active body type variant: "F" (female), "M" (male), or "N" (neutral).
   * Controls which _F/_M/_N prefab suffix is loaded in-game.
   */
  appearanceType: z.optional(z.string()),
  /**
   * SkinGender of the active skin source (fashion book or key page): "F" or "M".
   * Omitted when "N" (no gendered variants exist, body type toggle disabled).
   */
  skinGender: z.optional(z.string()),
  /** Equipped and available battle symbols (gifts). */
  gifts: z.optional(GiftInventoryDataSchema),
  /** Equipped key page has a body composite in fashionbodies/ (replacesHead behavior). */
  keyPageReplacesHead: z.optional(z.boolean()),
  /** Equipped key page has a front-layer composite in fashionbodies_front/. */
  keyPageHasFrontLayer: z.optional(z.boolean()),
  /** Equipped key page body has a head tilt (Z-axis rotation). */
  keyPageHeadTiltDeg: z.optional(z.number()),
  keyPagePivotFracX: z.optional(z.number()),
  keyPagePivotFracY: z.optional(z.number()),
  /** Equipped key page has a Hood sprite — back hair should be hidden. */
  keyPageHidesBackHair: z.optional(z.boolean()),
  /** SkinGender of the equipped key page skin: "F" or "M". */
  keyPageSkinGender: z.optional(z.string()),
  /** Feet-Y fraction of the equipped key page body PNG (see FashionBook.feetYFrac). */
  keyPageFeetYFrac: z.optional(z.number()),
  /** Pixel dimensions of the equipped key page body PNG (see FashionBook.bodyW/bodyH). */
  keyPageBodyW: z.optional(z.number()),
  keyPageBodyH: z.optional(z.number()),
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
  name: z.optional(z.string()),
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
  keyPage: z.optional(KeyPageSchema),
  /**
   * Light-pool triple — present on every battle unit because the engine carries
   * `PlayPoint`/`MaxPlayPoint` on `BattleUnitModel` regardless of faction. Most
   * enemies have `maxLight === 0`, and `LightDisplay` short-circuits on that so
   * the pip row stays hidden for those units. Allies and the (rarer) enemies
   * with a real pool share the same wire shape and render.
   */
  light: z.number(),
  maxLight: z.number(),
  reservedLight: z.number(),
  /**
   * False when the unit is dead or locked — only present in BattleSetting phase.
   * Undefined (absent) during battle means no restriction.
   */
  enabled: z.optional(z.boolean()),
  /** Deck card preview — only present in BattleSetting phase. */
  deckPreview: z.optional(z.array(DeckCardPreviewSchema)),
  /**
   * Per-unit speed-die inner-fill colour override (#rrggbb). Sampled at
   * runtime as the alpha-weighted mean of the unit's frame sprite texture
   * (CDC swaps this to themed sprites whose mean reflects the dim hex
   * interior). Absent when no override applies — consumers fall back to
   * the per-faction `--die-{ally,enemy}-fill` CSS vars.
   */
  dieColor: z.optional(z.string()),
  /**
   * Per-unit speed-die accent colour override (#rrggbb) — the tint a
   * colour mod sets on the inner roulette, which it also paints onto the
   * numeric digits in-game. Used for the speed-value numeral on the web;
   * the hex outline is derived from `dieColor` via CSS color-mix so all
   * three elements stay in the same colour family.
   */
  dieAccentColor: z.optional(z.string()),
  /**
   * False when a mind-control / charm buff has flipped the unit's
   * `IsControllable` off in-game. Reuses the unclaimed-unit affordance on
   * the frontend (dim dice, no beckon, click rejected) rather than drawing
   * a dedicated overlay — vanilla doesn't show one either; the unit simply
   * acts on its own.
   */
  controllable: z.optional(z.boolean()),
  /**
   * Per-actor target restriction. When this unit is the *attacker* and the
   * list is non-empty (e.g. BigBird_Eye's "Stared At" — only the inflicter
   * may be targeted), every alive opposing unit whose id is not in the list
   * should render as untargetable *while a die on this unit is selected*.
   * Mirrors the vanilla `BlockOtherUnitsDice` path that consults
   * `selectedUnit.GetFixedTargets()` at die-tap time. Omitted when empty.
   */
  fixedTargets: z.optional(z.array(z.number())),
});
export type Unit = z.infer<typeof UnitSchema>;

/** Ally-only extras: hand, deck, EGO. Light fields are inherited from UnitSchema. */
export const AllyUnitSchema = z.extend(UnitSchema, {
  /** Present when this ally is owned by the current session. */
  hand: z.optional(z.array(CardSchema)),
  deck: z.optional(z.array(CardSchema)),
  ego: z.optional(z.array(CardSchema)),
  teamHand: z.optional(z.array(CardSchema)),
  /**
   * Present instead of hand/deck/ego when this ally belongs to another
   * session. The server sends counts rather than card data to preserve privacy.
   */
  handCount: z.optional(z.number()),
  deckCount: z.optional(z.number()),
  egoCount: z.optional(z.number()),
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
  error: z.optional(z.string()),
});
export type ActionResult = z.infer<typeof ActionResultSchema>;

// ── Root state ───────────────────────────────────────────────────────────────

/**
 * Runtime-sampled visual constants the mod ships once it can read them
 * from the game prefabs. The frontend caches each block into CSS custom
 * properties so component styling stays declarative. Treated as one-shot
 * data: the same values flow through every state push but the client only
 * applies them on first receipt (and on subsequent value changes).
 */
export const ThemeSchema = z.object({
  /** Vanilla LoR's per-faction speed-die fill colour, sampled from SpeedDiceUI.Refs. */
  factionDieColors: z.optional(z.object({
    ally: z.string(),
    enemy: z.string(),
  })),
});
export type Theme = z.infer<typeof ThemeSchema>;

/** Top-level WebSocket `state` payload. */
export const GameStateSchema = z.object({
  scene: SceneNameSchema,
  /** Whether the server has finished extracting appearance/gift sprite assets. */
  assetsReady: z.optional(z.boolean()),
  /** Runtime-sampled visual constants; see ThemeSchema. */
  theme: z.optional(ThemeSchema),
  /** Active stage phase class name (e.g. "ApplyLibrarianCardPhase"). */
  phase: z.optional(z.string()),
  /** Active stage state enum value (e.g. "BattleSetting"). */
  stageState: z.optional(z.string()),
  uiPhase: z.optional(z.string()),
  stage: z.optional(StageInfoSchema),
  allies: z.optional(z.array(AllyUnitSchema)),
  enemies: z.optional(z.array(UnitSchema)),
  /** Only present during the key-page selection phase. */
  abnormalitySelection: z.optional(AbnormalitySelectionSchema),
  /** Present in main scene (non-BattleSetting) — floor roster with nested librarians. */
  floors: z.optional(z.array(FloorEntrySchema)),
  /** Key pages in the book inventory available to equip to a librarian. */
  availableKeyPages: z.optional(z.array(AvailableKeyPageSchema)),
  /** Cards in the shared inventory available to add to a librarian's deck. */
  availableCards: z.optional(z.array(AvailableCardSchema)),
  /** Global customization option tables (names, title lists, dialogue presets). */
  customizeOptions: z.optional(CustomizeOptionsSchema),
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
    /** One-shot visual constants; see ThemeSchema. */
    theme: z.optional(ThemeSchema),
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
    error: z.optional(z.string()),
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
    targetUnitId: z.optional(z.number()),
    targetDiceSlot: z.optional(z.number()),
    isEgo: z.optional(z.literal(1)),
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
    targetUnitId: z.optional(z.number()),
  }),
  // setCustomization carries the full appearance/dialogue/title payload for
  // a single librarian (floorIndex + unitIndex address it). Field list mirrors
  // CustomizePayloadSchema exactly — kept inline here (rather than via
  // `.extend()`) so the discriminated union remains a plain ZodObject.
  z.object({
    type: z.literal("setCustomization"),
    floorIndex: z.number(),
    unitIndex: z.number(),
    frontHairID: z.optional(z.number()),
    backHairID: z.optional(z.number()),
    eyeID: z.optional(z.number()),
    browID: z.optional(z.number()),
    mouthID: z.optional(z.number()),
    height: z.number(),
    hairR: z.optional(z.number()),
    hairG: z.optional(z.number()),
    hairB: z.optional(z.number()),
    skinR: z.optional(z.number()),
    skinG: z.optional(z.number()),
    skinB: z.optional(z.number()),
    eyeR: z.optional(z.number()),
    eyeG: z.optional(z.number()),
    eyeB: z.optional(z.number()),
    dlgStartBattle: z.optional(z.nullable(z.string())),
    dlgVictory: z.optional(z.nullable(z.string())),
    dlgDeath: z.optional(z.nullable(z.string())),
    dlgColleagueDeath: z.optional(z.nullable(z.string())),
    dlgKillsOpponent: z.optional(z.nullable(z.string())),
    prefixID: z.number(),
    postfixID: z.number(),
    customBookId: z.number(),
    customBookPackageId: z.optional(z.string()),
    workshopSkin: z.optional(z.string()),
    appearanceType: z.string(),
  }),
  // Key-page management. `unequipKeyPage` returns the librarian to their
  // immutable base (origin) key page — the engine implements unequip as
  // EquipBook(null), which falls back to defaultBook.
  z.object({
    type: z.literal("unequipKeyPage"),
    floorIndex: z.number(),
    unitIndex: z.number(),
  }),
  // Deck add/remove. Optional deckIndex (0..3) targets a specific slot on
  // multi-deck key pages; omitted/0 targets the active deck on single-deck books.
  z.object({
    type: z.literal("addCardToDeck"),
    floorIndex: z.number(),
    unitIndex: z.number(),
    cardId: z.number(),
    packageId: z.string(),
    deckIndex: z.optional(z.int().check(z.gte(0), z.lte(3))),
  }),
  z.object({
    type: z.literal("removeCardFromDeck"),
    floorIndex: z.number(),
    unitIndex: z.number(),
    cardId: z.number(),
    packageId: z.string(),
    deckIndex: z.optional(z.int().check(z.gte(0), z.lte(3))),
  }),
  // setGifts is a sparse batch update — callers send only the (position, key)
  // pairs that changed (see BattleSymbolsTab: one key per click). The server
  // reads keys `gift0`–`gift8` (id, or -1 to unequip) and `vis0`–`vis8`
  // (visibility, non-zero = shown). Every slot is optional; absent = no change.
  z.object({
    type: z.literal("setGifts"),
    floorIndex: z.number(),
    unitIndex: z.number(),
    gift0: z.optional(z.number()),
    gift1: z.optional(z.number()),
    gift2: z.optional(z.number()),
    gift3: z.optional(z.number()),
    gift4: z.optional(z.number()),
    gift5: z.optional(z.number()),
    gift6: z.optional(z.number()),
    gift7: z.optional(z.number()),
    gift8: z.optional(z.number()),
    vis0: z.optional(z.number()),
    vis1: z.optional(z.number()),
    vis2: z.optional(z.number()),
    vis3: z.optional(z.number()),
    vis4: z.optional(z.number()),
    vis5: z.optional(z.number()),
    vis6: z.optional(z.number()),
    vis7: z.optional(z.number()),
    vis8: z.optional(z.number()),
  }),
]);
export type ClientAction = z.infer<typeof ClientActionSchema>;

/**
 * The sparse key/value pairs sent in a `setGifts` action — everything except
 * the routing fields (`type`, `floorIndex`, `unitIndex`). Used as the parameter
 * type for `onSetGifts` callbacks throughout the librarian component tree so
 * that the discriminated-union variant can be reconstructed without spreading
 * an untyped `Record<string, number>`.
 */
export type SetGiftsPayload = Omit<
  Extract<ClientAction, { type: "setGifts" }>,
  "type" | "floorIndex" | "unitIndex"
>;
