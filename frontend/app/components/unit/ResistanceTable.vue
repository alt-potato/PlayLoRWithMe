<!--
  ResistanceTable.vue

  Pure display component that renders a 2×3 resistance grid (HP/SP × Slash/Pierce/Blunt).
  Accepts the `resistances` sub-object from a unit's keyPage data.

  Each cell renders a damage-type icon plus a compact tier symbol (++ / + / · / − / −− / ∅).
  The full player-facing label ("Fatal (2.0×)" etc.) is exposed via `aria-label` on the cell
  and as a hover `title` on the symbol — symbols alone are quick to scan but not legible to
  screen readers, so the redundancy is intentional.

  Props:
    resistances – keyPage.resistances (may be null/undefined; cells will be blank)
-->
<script setup lang="ts">
import type { Resistances } from "~/types/game";
defineProps<{ resistances: Resistances | undefined }>();
// resistStyle / resistSymbol / resistLabel are auto-imported from useBattleDisplay.ts
</script>

<template>
  <table class="resist-table">
    <tbody>
      <tr>
        <td
          :style="resistStyle(resistances?.slashHp, 'hp')"
          :aria-label="`Slash damage: ${resistLabel(resistances?.slashHp)}`"
        >
          <img src="/assets/stats/sHpResist.png" class="stat-icon" alt="" />
          <span class="resist-symbol" :title="resistLabel(resistances?.slashHp)">
            {{ resistSymbol(resistances?.slashHp) }}
          </span>
        </td>
        <td
          :style="resistStyle(resistances?.slashBp, 'bp')"
          :aria-label="`Slash stagger: ${resistLabel(resistances?.slashBp)}`"
        >
          <img src="/assets/stats/sBpResist.png" class="stat-icon" alt="" />
          <span class="resist-symbol" :title="resistLabel(resistances?.slashBp)">
            {{ resistSymbol(resistances?.slashBp) }}
          </span>
        </td>
      </tr>
      <tr>
        <td
          :style="resistStyle(resistances?.pierceHp, 'hp')"
          :aria-label="`Pierce damage: ${resistLabel(resistances?.pierceHp)}`"
        >
          <img src="/assets/stats/pHpResist.png" class="stat-icon" alt="" />
          <span class="resist-symbol" :title="resistLabel(resistances?.pierceHp)">
            {{ resistSymbol(resistances?.pierceHp) }}
          </span>
        </td>
        <td
          :style="resistStyle(resistances?.pierceBp, 'bp')"
          :aria-label="`Pierce stagger: ${resistLabel(resistances?.pierceBp)}`"
        >
          <img src="/assets/stats/pBpResist.png" class="stat-icon" alt="" />
          <span class="resist-symbol" :title="resistLabel(resistances?.pierceBp)">
            {{ resistSymbol(resistances?.pierceBp) }}
          </span>
        </td>
      </tr>
      <tr>
        <td
          :style="resistStyle(resistances?.bluntHp, 'hp')"
          :aria-label="`Blunt damage: ${resistLabel(resistances?.bluntHp)}`"
        >
          <img src="/assets/stats/bHpResist.png" class="stat-icon" alt="" />
          <span class="resist-symbol" :title="resistLabel(resistances?.bluntHp)">
            {{ resistSymbol(resistances?.bluntHp) }}
          </span>
        </td>
        <td
          :style="resistStyle(resistances?.bluntBp, 'bp')"
          :aria-label="`Blunt stagger: ${resistLabel(resistances?.bluntBp)}`"
        >
          <img src="/assets/stats/bBpResist.png" class="stat-icon" alt="" />
          <span class="resist-symbol" :title="resistLabel(resistances?.bluntBp)">
            {{ resistSymbol(resistances?.bluntBp) }}
          </span>
        </td>
      </tr>
    </tbody>
  </table>
</template>

<style scoped>
.resist-table {
  margin-top: 0.3rem;
  border-collapse: collapse;
  /*
   * The table sits in a flex column (`.kp-detail`), whose default
   * `align-items: stretch` would otherwise widen the table to the panel
   * and space the HP/BP cells apart. Shrink-wrap to content and opt out
   * of cross-axis stretch so each row hugs the left edge.
   */
  width: fit-content;
  align-self: flex-start;
  font-size: 0.68rem;
  font-family: var(--font-body);
}
.resist-table th {
  color: var(--text-3);
  font-weight: normal;
  text-align: center;
  padding: 0.1rem 0.3rem;
}

.stat-icon {
  width: 1.1rem;
  height: 1.1rem;
  object-fit: contain;
  vertical-align: middle;
  opacity: 0.85;
}
.resist-table thead th:first-child {
  width: 1.8rem;
}
.resist-table td {
  text-align: left;
  padding: 0.1rem 0.3rem;
  font-weight: bold;
  white-space: nowrap;
}

/*
 * Symbol cell. min-width keeps the column predictable across rows
 * regardless of which tier is rendered (1- vs 2-char symbols).
 */
.resist-symbol {
  display: inline-block;
  min-width: 1.4rem;
  text-align: left;
  margin-left: 0.15rem;
  font-size: 0.85rem;
  line-height: 1;
}
</style>
