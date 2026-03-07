<!--
  ArrowOverlay.vue

  Fixed full-viewport SVG that draws targeting arrows between speed dice.
  getBoundingClientRect() coords map 1:1 to the SVG coordinate space
  because the SVG fills the viewport with position:fixed.

  Arrows use S-curve Bezier routing: M x1 y1 C midX y1, midX y2, x2 y2.
  Because each arrow has a unique (y1, y2), curves naturally fan out in the
  center gap and never stack on top of each other.

  Props:
    allies        – ally unit array (to derive faction from unit id)
    enemies       – enemy unit array
    showIncoming  – red  one-sided enemy→ally arrows
    showClash     – gold mutual-targeting (clash) arrows
    showOutgoing  – blue one-sided ally→enemy arrows
    focusUnitId   – when set, dim arrows not involving this unit id

  Die elements are found by [data-die="unitId-slot"] attributes.
-->
<script setup lang="ts">
const props = defineProps<{
  allies: any[];
  enemies: any[];
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

// ARROW_COLORS is auto-imported from useBattleDisplay.ts

/** Center of the inner edge of a die: right edge for enemies, left edge for allies. */
function diePoint(
  unitId: number,
  slot: number,
  allyIds: Set<number>,
): { x: number; y: number } | null {
  const el = document.querySelector(`[data-die="${unitId}-${slot}"]`);
  if (!el) return null;
  const r = el.getBoundingClientRect();
  return {
    x: allyIds.has(unitId) ? r.left : r.right,
    y: (r.top + r.bottom) / 2,
  };
}

async function recompute() {
  await nextTick();

  const allyIds = new Set<number>(props.allies.map((a: any) => a.id));
  const allUnits = [...props.allies, ...props.enemies];
  const result: Arrow[] = [];
  const clashSeen = new Set<string>();

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

      const src = diePoint(unit.id, sc.slot, allyIds);
      const tgt = diePoint(sc.targetUnitId, sc.targetSlot, allyIds);
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
          const stTgt = diePoint(st.targetUnitId, st.targetSlot, allyIds);
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

watch(() => [props.allies, props.enemies], recompute, { deep: true });

onMounted(() => {
  recompute();
  const ro = new ResizeObserver(recompute);
  ro.observe(document.documentElement);
  window.addEventListener("scroll", recompute, { passive: true });
  onUnmounted(() => {
    ro.disconnect();
    window.removeEventListener("scroll", recompute);
  });
});

function visible(a: Arrow): boolean {
  if (a.type === "incoming") return props.showIncoming;
  if (a.type === "clash") return props.showClash;
  return props.showOutgoing;
}
</script>

<template>
  <svg class="arrow-svg" xmlns="http://www.w3.org/2000/svg">
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
  position: fixed;
  inset: 0;
  width: 100vw;
  height: 100vh;
  pointer-events: none;
  z-index: 50;
}
</style>
