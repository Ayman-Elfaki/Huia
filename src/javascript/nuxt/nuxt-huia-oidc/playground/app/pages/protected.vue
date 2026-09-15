<script setup lang="ts">
definePageMeta({ middleware: 'auth' })

const { data } = await useFetch('/api/whoami')
</script>

<template>
  <section>
    <h2>Protected</h2>
    <p>Only reachable when signed in. Server said:</p>
    <pre>{{ data }}</pre>
  </section>
</template>
