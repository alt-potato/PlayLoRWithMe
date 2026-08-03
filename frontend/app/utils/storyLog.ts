/**
 * Presentation rules for mirrored cutscene dialogue.
 *
 * Kept out of the component so the rules that carry actual behaviour — which rows
 * show a speaker, which resolve a portrait, and when a new line is allowed to move
 * the viewport — are unit-testable without mounting Vue.
 */

import type { StoryLogEntry } from "~/types/game";

/** Directory the mod's IconCache writes extracted story portraits to. */
export const PORTRAIT_BASE_PATH = "/assets/portraits/";

/**
 * Teller value the game uses for narration rather than a speaking character.
 * Vanilla's own log blanks the entire name line for these rows, so we do too.
 */
export const MONOLOGUE_TELLER = "Monologue";

/**
 * Distance from the bottom, in pixels, within which the reader still counts as
 * following the newest line. Beyond it they are reading back through earlier
 * dialogue and an arriving line must not yank the viewport away from them.
 */
export const AUTOSCROLL_THRESHOLD_PX = 32;

/** Whether an entry is a story-choice outcome rather than a spoken line. */
export function isChoiceEntry(entry: StoryLogEntry): boolean {
  return entry.isChoice === true;
}

/** Whether an entry is a place caption marking a change of location. */
export function isPlaceEntry(entry: StoryLogEntry): boolean {
  return entry.isPlace === true;
}

/**
 * Whether a row is an ordinary spoken line — the only kind that carries a speaker
 * and a portrait. Choice outcomes and place captions are both interstitials.
 */
export function isSpokenEntry(entry: StoryLogEntry): boolean {
  return !isChoiceEntry(entry) && !isPlaceEntry(entry);
}

/**
 * Whether a row reserves its portrait frame. Spoken lines always do, even when the
 * speaker has no portrait asset — the in-game log disables only the image and
 * leaves its hex frame standing, which also keeps rows aligned down the column.
 */
export function showsPortraitFrame(entry: StoryLogEntry): boolean {
  return isSpokenEntry(entry);
}

/**
 * Whether a row reserves its name line. Every spoken row does, including monologue
 * rows that render no text into it — the in-game layout keeps the slot, so
 * collapsing it would make monologue rows sit at a different height than the rest.
 */
export function reservesNameRow(entry: StoryLogEntry): boolean {
  return isSpokenEntry(entry);
}

/**
 * Whether a row renders a name line. Choice rows have no speaker, and monologue
 * rows deliberately suppress theirs to match the in-game log.
 */
export function showsSpeaker(entry: StoryLogEntry): boolean {
  if (!isSpokenEntry(entry)) return false;
  return !!entry.teller && entry.teller !== MONOLOGUE_TELLER;
}

/**
 * Resolves an entry's portrait asset URL, or null when it has none. The slug is
 * already ASCII-safe by construction on the mod side, but it is encoded anyway so
 * that a malformed value cannot break out of the path segment.
 */
export function portraitUrl(entry: StoryLogEntry): string | null {
  if (!isSpokenEntry(entry) || !entry.portrait) return null;
  return `${PORTRAIT_BASE_PATH}${encodeURIComponent(entry.portrait)}.png`;
}

/** Scroll geometry needed to decide whether the reader is following along. */
export interface ScrollPosition {
  scrollTop: number;
  scrollHeight: number;
  clientHeight: number;
}

/**
 * Whether the viewport is close enough to the bottom that an arriving line should
 * scroll it into view.
 */
export function isPinnedToBottom(pos: ScrollPosition): boolean {
  const distance = pos.scrollHeight - pos.scrollTop - pos.clientHeight;
  return distance <= AUTOSCROLL_THRESHOLD_PX;
}
