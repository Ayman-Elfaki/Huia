<script setup lang="ts">
const { session } = useUserSession()
</script>

<template>
  <section>
    <h2>Home (public)</h2>
    <pre>{{ session }}</pre>
  </section>
</template>
