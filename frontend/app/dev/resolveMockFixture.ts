/**
 * Resolves the active mock-fixture name for this page load. Priority:
 *   1. `?mock=<name>` query param — wins when present, writes to localStorage
 *      so a refresh without the query param preserves the selection; an empty
 *      value (`?mock=`) clears both the param's effect and localStorage.
 *   2. `localStorage["plwm_mock_fixture"]` — fallback when no query param.
 *
 * Returns null when no fixture is selected, or when called in a non-browser
 * environment (SSR, tests without DOM).
 */

const STORAGE_KEY = "plwm_mock_fixture";
const QUERY_PARAM = "mock";

export function resolveMockFixture(): string | null {
  if (typeof window === "undefined") return null;

  const params = new URLSearchParams(location.search);
  if (params.has(QUERY_PARAM)) {
    const raw = params.get(QUERY_PARAM) ?? "";
    if (raw === "") {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
    localStorage.setItem(STORAGE_KEY, raw);
    return raw;
  }
  return localStorage.getItem(STORAGE_KEY);
}
