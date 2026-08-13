<script setup lang="ts">
import { ChevronLeft, ChevronRight } from '@lucide/vue'
import { Button } from '@/components/ui/button'

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
    <Button
      variant="outline"
      :disabled="!hasPrevious || loading"
      @click="emit('previous')"
    >
      <ChevronLeft data-icon="inline-start" />
      Previous
    </Button>
    <Button
      variant="outline"
      :disabled="!hasNext || loading"
      @click="emit('next')"
    >
      Next
      <ChevronRight data-icon="inline-end" />
    </Button>
  </div>
</template>
