<!--
  ResistanceTable.vue

  Pure display component that renders a 2×3 resistance grid (HP/SG × Slash/Pierce/Blunt).
  Accepts the `resistances` sub-object from a unit's keyPage data.

  Props:
    resistances – keyPage.resistances (may be null/undefined; cells will be blank)
-->
<script setup lang="ts">
defineProps<{ resistances: any }>();
// resistColor is auto-imported from useBattleDisplay.ts
</script>

<template>
  <table class="resist-table">
    <thead>
      <tr>
        <th></th>
        <th>Slash</th>
        <th>Pierce</th>
        <th>Blunt</th>
      </tr>
    </thead>
    <tbody>
      <tr>
        <th>HP</th>
        <td :style="{ color: resistColor(resistances?.slashHp) }">
          {{ resistances?.slashHp }}
        </td>
        <td :style="{ color: resistColor(resistances?.pierceHp) }">
          {{ resistances?.pierceHp }}
        </td>
        <td :style="{ color: resistColor(resistances?.bluntHp) }">
          {{ resistances?.bluntHp }}
        </td>
      </tr>
      <tr>
        <th>SG</th>
        <td :style="{ color: resistColor(resistances?.slashBp) }">
          {{ resistances?.slashBp }}
        </td>
        <td :style="{ color: resistColor(resistances?.pierceBp) }">
          {{ resistances?.pierceBp }}
        </td>
        <td :style="{ color: resistColor(resistances?.bluntBp) }">
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
  font-family: var(--font-mono);
}
.resist-table th {
  color: var(--text-3);
  font-weight: normal;
  text-align: center;
  padding: 0.1rem 0.3rem;
  font-family: var(--font-display);
  font-size: 0.55rem;
  letter-spacing: 0.06em;
  text-transform: uppercase;
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
