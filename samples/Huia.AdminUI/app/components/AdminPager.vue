<script setup lang="ts">
/** Next/Previous controls for a useAdminList()-backed table - cursor pagination has no page count or total to show. */
defineProps<{
  hasPrevious: boolean
  hasNext: boolean
  loading?: boolean
}>()

const emit = defineEmits<{ previous: [], next: [] }>()
</script>

<template>
  <div class="flex justify-end gap-2">
    <UButton
      label="Previous"
      icon="i-lucide-chevron-left"
      color="neutral"
      variant="subtle"
      :disabled="!hasPrevious || loading"
      @click="emit('previous')"
    />
    <UButton
      label="Next"
      icon="i-lucide-chevron-right"
      trailing
      color="neutral"
      variant="subtle"
      :disabled="!hasNext || loading"
      @click="emit('next')"
    />
  </div>
</template>
