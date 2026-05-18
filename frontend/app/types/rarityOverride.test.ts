/**
 * Wire-contract drift test for the CustomRarityUtil colour overrides.
 *
 * Each of the seven affected schemas must accept a payload carrying all four
 * rarity*Color hex fields AND a payload omitting them entirely. The "omit"
 * case is the >99% real-world wire shape (vanilla rarities, or CustomRarityUtil
 * not loaded); the "all four" case is what custom-rarity content sends.
 */

import { describe, it, expect } from "vitest";
import { z } from "zod/mini";
import {
  CardSchema,
  SlottedCardEntrySchema,
  PassiveSchema,
  KeyPageSchema,
  DeckCardPreviewSchema,
  AvailableKeyPageSchema,
  AvailableCardSchema,
} from "./game";

const overrides = {
  rarityColor: "#ff0000",
  rarityRangeIconColor: "#ff8888",
  rarityAbilityColor: "#ffffff",
  rarityKeywordColor: "#ffaa00",
};

const baseCard = {
  id: { id: 1001, packageId: 0 },
  index: 0,
  name: "Sample Card",
  cost: 2,
  range: "Near",
};

const baseSlotted = {
  cardIndex: 0,
  slot: 0,
  name: "Sample Card",
  cost: 2,
  clash: false,
  range: "Near",
};

const basePassive = {
  id: { id: 2002, packageId: 0 },
  name: "Sample Passive",
};

const baseKeyPage = {
  name: "Sample Key Page",
};

const baseDeckPreview = {
  name: "Sample Deck Card",
  cost: 1,
  range: "Near",
  count: 2,
};

const baseAvailableKeyPage = {
  instanceId: 1,
  name: "Sample Key Page",
  speedMin: 3,
  speedMax: 7,
  bookId: { id: 1, packageId: "" },
  chapter: 1,
  bookIcon: "Chapter1",
  bookGroupName: "Sample Group",
  hp: 50,
  breakGauge: 30,
  equipRangeType: "Melee",
  resistances: {},
  passives: [],
};

const baseAvailableCard = {
  cardId: { id: 1, packageId: "" },
  name: "Sample Card",
  cost: 1,
  range: "Near",
  rarity: "Common",
  count: 3,
};

const CASES: ReadonlyArray<{
  name: string;
  schema: z.ZodMiniType<unknown, unknown>;
  base: object;
}> = [
  { name: "CardSchema", schema: CardSchema, base: baseCard },
  { name: "SlottedCardEntrySchema", schema: SlottedCardEntrySchema, base: baseSlotted },
  { name: "PassiveSchema", schema: PassiveSchema, base: basePassive },
  { name: "KeyPageSchema", schema: KeyPageSchema, base: baseKeyPage },
  { name: "DeckCardPreviewSchema", schema: DeckCardPreviewSchema, base: baseDeckPreview },
  { name: "AvailableKeyPageSchema", schema: AvailableKeyPageSchema, base: baseAvailableKeyPage },
  { name: "AvailableCardSchema", schema: AvailableCardSchema, base: baseAvailableCard },
];

describe("rarity colour overrides — wire contract", () => {
  for (const { name, schema, base } of CASES) {
    it(`${name} accepts a payload with all four overrides`, () => {
      const result = z.safeParse(schema, { ...base, ...overrides });
      if (!result.success) {
        throw new Error(
          `${name} rejected payload with overrides:\n` +
            JSON.stringify(result.error.issues, null, 2),
        );
      }
      const data = result.data as Record<string, unknown>;
      expect(data.rarityColor).toBe(overrides.rarityColor);
      expect(data.rarityRangeIconColor).toBe(overrides.rarityRangeIconColor);
      expect(data.rarityAbilityColor).toBe(overrides.rarityAbilityColor);
      expect(data.rarityKeywordColor).toBe(overrides.rarityKeywordColor);
    });

    it(`${name} accepts a payload that omits every override field`, () => {
      const result = z.safeParse(schema, base);
      if (!result.success) {
        throw new Error(
          `${name} rejected baseline payload:\n` +
            JSON.stringify(result.error.issues, null, 2),
        );
      }
      const data = result.data as Record<string, unknown>;
      expect(data.rarityColor).toBeUndefined();
      expect(data.rarityRangeIconColor).toBeUndefined();
      expect(data.rarityAbilityColor).toBeUndefined();
      expect(data.rarityKeywordColor).toBeUndefined();
    });
  }
});
