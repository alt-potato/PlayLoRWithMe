import { describe, it, expect } from "vitest";
import type { StoryLogEntry } from "~/types/game";
import {
  AUTOSCROLL_THRESHOLD_PX,
  MONOLOGUE_TELLER,
  PORTRAIT_BASE_PATH,
  isChoiceEntry,
  isPinnedToBottom,
  portraitUrl,
  showsPortraitFrame,
  showsSpeaker,
} from "./storyLog";

const spoken = (over: Partial<StoryLogEntry> = {}): StoryLogEntry => ({
  content: "A line.",
  teller: "Roland",
  ...over,
});

const choice = (over: Partial<StoryLogEntry> = {}): StoryLogEntry => ({
  content: "Forgive",
  isChoice: true,
  choiceIsRed: false,
  ...over,
});

describe("isChoiceEntry", () => {
  it("is true only when the flag is explicitly set", () => {
    expect(isChoiceEntry(choice())).toBe(true);
    expect(isChoiceEntry(spoken())).toBe(false);
    expect(isChoiceEntry(spoken({ isChoice: false }))).toBe(false);
  });
});

describe("showsSpeaker", () => {
  it("shows the name for an ordinary spoken line", () => {
    expect(showsSpeaker(spoken())).toBe(true);
  });

  it("suppresses the name for monologue rows, matching the in-game log", () => {
    expect(showsSpeaker(spoken({ teller: MONOLOGUE_TELLER }))).toBe(false);
  });

  it("suppresses the name for choice rows", () => {
    expect(showsSpeaker(choice())).toBe(false);
    // Even if a teller somehow rides along on a choice row.
    expect(showsSpeaker(choice({ teller: "Roland" }))).toBe(false);
  });

  it("suppresses the name when the teller is absent or empty", () => {
    expect(showsSpeaker({ content: "x" })).toBe(false);
    expect(showsSpeaker(spoken({ teller: "" }))).toBe(false);
  });
});

describe("showsPortraitFrame", () => {
  it("reserves the frame for a spoken line even with no portrait asset", () => {
    // The in-game log disables the image but leaves the hex standing, and it
    // keeps the text column aligned down the list.
    expect(showsPortraitFrame(spoken())).toBe(true);
    expect(showsPortraitFrame(spoken({ portrait: "Roland_3a1f77c2" }))).toBe(true);
  });

  it("reserves the frame for monologue rows", () => {
    expect(showsPortraitFrame(spoken({ teller: MONOLOGUE_TELLER }))).toBe(true);
  });

  it("omits the frame for choice rows, which vanilla renders through another slot", () => {
    expect(showsPortraitFrame(choice())).toBe(false);
  });
});

describe("portraitUrl", () => {
  it("builds a path under the portrait asset directory", () => {
    expect(portraitUrl(spoken({ portrait: "Roland_3a1f77c2" }))).toBe(
      `${PORTRAIT_BASE_PATH}Roland_3a1f77c2.png`,
    );
  });

  it("returns null when the speaker has no portrait", () => {
    expect(portraitUrl(spoken())).toBeNull();
    expect(portraitUrl(spoken({ portrait: "" }))).toBeNull();
  });

  it("returns null for choice rows", () => {
    expect(portraitUrl(choice({ portrait: "Roland_3a1f77c2" }))).toBeNull();
  });

  it("encodes the slug so a malformed value cannot escape the path segment", () => {
    expect(portraitUrl(spoken({ portrait: "../../secret" }))).toBe(
      `${PORTRAIT_BASE_PATH}..%2F..%2Fsecret.png`,
    );
  });
});

describe("isPinnedToBottom", () => {
  it("is true at the exact bottom", () => {
    expect(
      isPinnedToBottom({ scrollTop: 400, scrollHeight: 1000, clientHeight: 600 }),
    ).toBe(true);
  });

  it("is true within the threshold", () => {
    expect(
      isPinnedToBottom({
        scrollTop: 400 - AUTOSCROLL_THRESHOLD_PX,
        scrollHeight: 1000,
        clientHeight: 600,
      }),
    ).toBe(true);
  });

  it("is false once the reader has scrolled past the threshold", () => {
    expect(
      isPinnedToBottom({
        scrollTop: 400 - AUTOSCROLL_THRESHOLD_PX - 1,
        scrollHeight: 1000,
        clientHeight: 600,
      }),
    ).toBe(false);
  });

  it("is true when the content is shorter than the viewport", () => {
    // Nothing to scroll, so an arriving line should still be revealed.
    expect(
      isPinnedToBottom({ scrollTop: 0, scrollHeight: 200, clientHeight: 600 }),
    ).toBe(true);
  });
});
