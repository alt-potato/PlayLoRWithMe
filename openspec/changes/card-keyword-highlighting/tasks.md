## 1. keywordHighlight utility

- [x] 1.1 Create `frontend/app/utils/keywordHighlight.ts` exporting a pure `splitKeywordSegments(text: string): KeywordSegment[]` where `KeywordSegment = { text: string; isKeyword: boolean }`. Uses `/\[[^\]]+\]/g`; **strips brackets from the emitted keyword text** to match the in-game view. Empty input returns `[]`; input with no brackets returns a single non-keyword segment.
- [x] 1.2 Add `frontend/app/utils/keywordHighlight.test.ts` covering: empty string, no-bracket text, single keyword, multiple keywords, leading/trailing keyword (no empty padding segments), keyword-only text, unclosed-bracket treated as plain, text containing `]` without `[` treated as plain.

## 2. KeywordText component

- [x] 2.1 Create `frontend/app/components/KeywordText.vue` — props `{ text: string }`, iterates `splitKeywordSegments(text)`, emits one `<span>` per segment with `.keyword` class applied only when the segment is bracketed. Inline layout (no block wrapper); scoped style sets `.keyword { color: var(--gold-bright); font-weight: 600 }`.

## 3. Wire KeywordText into render sites

- [x] 3.1 In `frontend/app/components/CardDetail.vue` replace `{{ card.abilityDesc }}` (ability-desc `<p>`) with `<KeywordText :text="card.abilityDesc" />`.
- [x] 3.2 In `frontend/app/components/CardDetail.vue` replace `{{ d.desc }}` (die-desc `<span>`) with `<KeywordText :text="d.desc" />`.
- [x] 3.3 In `frontend/app/components/HandCard.vue` replace `{{ card.abilityDesc }}` (hcard-detail-ability `<p>`) with `<KeywordText :text="card.abilityDesc" />`.
- [x] 3.4 In `frontend/app/components/HandCard.vue` replace `{{ d.desc }}` (hcard-die-desc `<p>`) with `<KeywordText :text="d.desc" />`.

## 4. Validation

- [x] 4.1 Run `cd frontend && npm test` — expect all tests passing including the new `keywordHighlight.test.ts`. 56/56 green across 9 test files.
- [x] 4.2 Run `cd mod && dotnet build` — expect `0 Warning(s) 0 Error(s)`.
- [x] 4.3 Visually verify in the browser (`cd frontend && npm run dev`, `?mock=battle-sampler`) that bracketed keywords in the hand card's detail pane and the card detail modal render in bright gold, and non-bracketed text renders in its existing tint.
