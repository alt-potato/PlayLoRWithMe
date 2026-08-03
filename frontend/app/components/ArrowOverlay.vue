<!--
  ArrowOverlay.vue

  SVG overlay that draws targeting arrows between speed dice. It is
  absolutely positioned inside the scrolling stage content (not fixed to
  the viewport): arrow endpoints are stored relative to the SVG's own
  origin, and since the offset between two elements in the same scrolled
  content never changes during a scroll, the arrows ride the scroll
  natively on the compositor with zero lag and no scroll listener.

  Arrows use S-curve Bezier routing: M x1 y1 C midX y1, midX y2, x2 y2.
  Because each arrow has a unique (y1, y2), curves naturally fan out in the
  center gap and never stack on top of each other.

  Props:
    allies        – ally unit array in display order (to derive faction
                    from unit id; pass the sorted array so manual reorders
                    change prop identity and trigger a recompute)
    enemies       – enemy unit array in display order
    showIncoming  – red  one-sided enemy→ally arrows
    showClash     – gold mutual-targeting (clash) arrows
    showOutgoing  – blue one-sided ally→enemy arrows
    focusUnitId   – when set, dim arrows not involving this unit id

  Die elements are found by [data-die="unitId-slot"] attributes.
-->
<script setup lang="ts">
import type { Unit } from "~/types/game";

const props = defineProps<{
  allies: Unit[];
  enemies: Unit[];
  showIncoming: boolean;
  showClash: boolean;
  showOutgoing: boolean;
  focusUnitId?: number | null;
}>();

interface Arrow {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  type: "incoming" | "clash" | "outgoing";
  dashed: boolean;
  srcUnitId: number;
  tgtUnitId: number;
}

const arrows = ref<Arrow[]>([]);
const svgEl = ref<SVGSVGElement | null>(null);

// ARROW_COLORS is auto-imported from useBattleDisplay.ts

/** Center of the inner edge of a die: right edge for enemies, left edge for allies. */
function diePoint(
  unitId: number,
  slot: number,
  allyIds: Set<number>,
  dieMap: Map<string, Element>,
  origin: { x: number; y: number },
): { x: number; y: number } | null {
  const el = dieMap.get(`${unitId}-${slot}`);
  if (!el) return null;
  const r = el.getBoundingClientRect();
  // subtracting the overlay's own viewport origin converts both rects into
  // the svg's local space, which is scroll-invariant.
  return {
    x: (allyIds.has(unitId) ? r.left : r.right) - origin.x,
    y: (r.top + r.bottom) / 2 - origin.y,
  };
}

async function recompute() {
  await nextTick();

  // both the svg's and the dice's rects must come from the same layout pass;
  // their difference is then stable no matter how far the page has scrolled.
  const svgRect = svgEl.value?.getBoundingClientRect();
  if (!svgRect) return;
  const origin = { x: svgRect.left, y: svgRect.top };

  const allyIds = new Set<number>(props.allies.map((a) => a.id));
  const allUnits = [...props.allies, ...props.enemies];
  const result: Arrow[] = [];
  const clashSeen = new Set<string>();

  // single dom walk: cache every [data-die] element by its key so the inner
  // loop avoids N+M repeated `querySelector` calls per pass.
  const dieMap = new Map<string, Element>();
  for (const el of document.querySelectorAll("[data-die]")) {
    const k = el.getAttribute("data-die");
    if (k) dieMap.set(k, el);
  }

  for (const unit of allUnits) {
    if (unit.hp <= 0) continue;
    const isAlly = allyIds.has(unit.id);
    for (const sc of unit.slottedCards ?? []) {
      if (sc.targetUnitId == null) continue;

      const type: Arrow["type"] = sc.clash
        ? "clash"
        : isAlly
          ? "outgoing"
          : "incoming";

      // Clash: deduplicate — only draw ally→enemy direction
      if (type === "clash") {
        if (!isAlly) continue;
        const key = `${unit.id}-${sc.slot}-${sc.targetUnitId}-${sc.targetSlot}`;
        if (clashSeen.has(key)) continue;
        clashSeen.add(key);
      }

      const src = diePoint(unit.id, sc.slot, allyIds, dieMap, origin);
      const tgt = diePoint(sc.targetUnitId, sc.targetSlot!, allyIds, dieMap, origin);
      if (src && tgt) {
        result.push({
          x1: src.x,
          y1: src.y,
          x2: tgt.x,
          y2: tgt.y,
          type,
          dashed: false,
          srcUnitId: unit.id,
          tgtUnitId: sc.targetUnitId,
        });
      }

      // Sub-targets (mass attacks) — same source die, dashed stroke
      if (src) {
        for (const st of sc.subTargets ?? []) {
          const stTgt = diePoint(st.targetUnitId, st.targetSlot, allyIds, dieMap, origin);
          if (stTgt)
            result.push({
              x1: src.x,
              y1: src.y,
              x2: stTgt.x,
              y2: stTgt.y,
              type: isAlly ? "outgoing" : "incoming",
              dashed: true,
              srcUnitId: unit.id,
              tgtUnitId: st.targetUnitId,
            });
        }
      }
    }
  }

  arrows.value = result;
}

/** S-curve: horizontal tangents at both endpoints; arrows fan naturally by y. */
function bezierPath(a: Arrow): string {
  const midX = (a.x1 + a.x2) / 2;
  return `M ${a.x1} ${a.y1} C ${midX} ${a.y1}, ${midX} ${a.y2}, ${a.x2} ${a.y2}`;
}

/** True when a focus unit is set and this arrow doesn't involve it. */
function isDimmed(a: Arrow): boolean {
  if (props.focusUnitId == null) return false;
  return a.srcUnitId !== props.focusUnitId && a.tgtUnitId !== props.focusUnitId;
}

// ResizeObserver can fire in bursts during layout changes, and each
// `recompute` runs N getBoundingClientRect reads (a forced layout). Coalesce
// into a single rAF pass so at most one recompute runs per frame.
let rafId = 0;
function scheduleRecompute() {
  if (rafId) return;
  rafId = requestAnimationFrame(() => {
    rafId = 0;
    recompute();
  });
}

// Watch the two sources directly (not wrapped in a new array literal, which
// would compare unequal every tick and fire on every reactive change) and route
// through scheduleRecompute so state-driven recomputes are rAF-batched and read
// the DOM after Vue has flushed the new die positions. Manual unit reorders
// arrive here too: Stage passes the sorted arrays, whose identity changes on
// every reorder.
watch([() => props.allies, () => props.enemies], scheduleRecompute);

onMounted(() => {
  recompute();
  // observing the svg itself covers stage-content reflows (hand expansion,
  // viewport resize) because the overlay is stretched to the stage's size.
  // no scroll listener: endpoints are in the svg's local space, so scrolling
  // moves the whole overlay with the content and nothing needs recomputing.
  const ro = new ResizeObserver(scheduleRecompute);
  if (svgEl.value) ro.observe(svgEl.value);
  onUnmounted(() => {
    ro.disconnect();
    if (rafId) cancelAnimationFrame(rafId);
  });
});

function visible(a: Arrow): boolean {
  if (a.type === "incoming") return props.showIncoming;
  if (a.type === "clash") return props.showClash;
  return props.showOutgoing;
}
</script>

<template>
  <svg ref="svgEl" class="arrow-svg" xmlns="http://www.w3.org/2000/svg">
    <defs>
      <marker
        v-for="t in ['incoming', 'clash', 'outgoing'] as const"
        :key="t"
        :id="`ah-${t}`"
        markerWidth="8"
        markerHeight="6"
        refX="7"
        refY="3"
        orient="auto"
      >
        <polygon points="0 0, 8 3, 0 6" :fill="ARROW_COLORS[t]" />
      </marker>
      <!-- Reverse marker for clash: arrowhead at the source end -->
      <marker
        id="ah-clash-start"
        markerWidth="8"
        markerHeight="6"
        refX="7"
        refY="3"
        orient="auto-start-reverse"
      >
        <polygon points="0 0, 8 3, 0 6" :fill="ARROW_COLORS['clash']" />
      </marker>
    </defs>

    <path
      v-for="(a, i) in arrows"
      :key="i"
      v-show="visible(a)"
      :d="bezierPath(a)"
      :stroke="ARROW_COLORS[a.type]"
      :stroke-width="a.dashed ? 1.5 : 2"
      :stroke-dasharray="a.dashed ? '5 4' : undefined"
      :opacity="isDimmed(a) ? 0.1 : 1"
      stroke-linecap="round"
      fill="none"
      style="transition: opacity 0.2s"
      :marker-start="a.type === 'clash' ? 'url(#ah-clash-start)' : undefined"
      :marker-end="`url(#ah-${a.type})`"
    />
  </svg>
</template>

<style scoped>
.arrow-svg {
  /* stretched over the positioned stage container so arrows live in the
     scrolled content and track it natively; see the header comment. */
  position: absolute;
  inset: 0;
  pointer-events: none;
  z-index: var(--z-arrows);
  /* arrowheads at a die's very edge may poke past the stage bounds */
  overflow: visible;
}
</style>
