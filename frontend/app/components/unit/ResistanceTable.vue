<!--
  ResistanceTable.vue

  Pure display component that renders a 2×3 resistance grid (HP/SP × Slash/Pierce/Blunt).
  Accepts the `resistances` sub-object from a unit's keyPage data.

  Props:
    resistances – keyPage.resistances (may be null/undefined; cells will be blank)
-->
<script setup lang="ts">
import type { Resistances } from "~/types/game";
defineProps<{ resistances: Resistances | undefined }>();
// resistColor is auto-imported from useBattleDisplay.ts
</script>

<template>
  <table class="resist-table">
    <tbody>
      <tr>
        <td :style="{ color: resistColor(resistances?.slashHp) }">
          <img
            src="/assets/stats/sHpResist.png"
            class="stat-icon"
            alt="Slash Damage Resistance"
          />
          {{ resistances?.slashHp }}
        </td>
        <td :style="{ color: resistColor(resistances?.slashBp) }">
          <img
            src="/assets/stats/sBpResist.png"
            class="stat-icon"
            alt="Slash Stagger Damage Resistance"
          />
          {{ resistances?.slashBp }}
        </td>
      </tr>
      <tr>
        <td :style="{ color: resistColor(resistances?.pierceHp) }">
          <img
            src="/assets/stats/pHpResist.png"
            class="stat-icon"
            alt="Pierce Damage Resistance"
          />
          {{ resistances?.pierceHp }}
        </td>
        <td :style="{ color: resistColor(resistances?.pierceBp) }">
          <img
            src="/assets/stats/pBpResist.png"
            class="stat-icon"
            alt="Pierce Stagger Damage Resistance"
          />
          {{ resistances?.pierceBp }}
        </td>
      </tr>
      <tr>
        <td :style="{ color: resistColor(resistances?.bluntHp) }">
          <img
            src="/assets/stats/bHpResist.png"
            class="stat-icon"
            alt="Blunt Damage Resistance"
          />
          {{ resistances?.bluntHp }}
        </td>
        <td :style="{ color: resistColor(resistances?.bluntBp) }">
          <img
            src="/assets/stats/bBpResist.png"
            class="stat-icon"
            alt="Blunt Stagger Damage Resistance"
          />
          {{ resistances?.bluntBp }}
        </td>
      </tr>
    </tbody>
  </table>
</template>

<style scoped>
.resist-table {
  margin-top: 0.3rem;
  border-collapse: collapse;
  width: 100%;
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
  height: 1rem;
  width: auto;
  vertical-align: middle;
  opacity: 0.85;
}
.resist-table thead th:first-child {
  width: 1.8rem;
}
.resist-table td {
  text-align: center;
  padding: 0.1rem 0.3rem;
  font-weight: bold;
}
</style>
