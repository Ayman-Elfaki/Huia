<script setup lang="ts">
import type { HTMLAttributes } from "vue"
import { cn } from "@/lib/utils"

// A lightly-styled native <select>. The stock shadcn-vue Select is a reka-ui listbox; a native control
// keeps the admin sample small and trivially driveable from Playwright.
const props = defineProps<{
  modelValue?: string
  class?: HTMLAttributes["class"]
}>()

const emits = defineEmits<{
  (e: "update:modelValue", payload: string): void
}>()
</script>

<template>
  <select
    :value="modelValue"
    :class="cn(
      'flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 [&>option]:bg-background [&>option]:text-foreground',
      props.class,
    )"
    @change="emits('update:modelValue', ($event.target as HTMLSelectElement).value)"
  >
    <slot />
  </select>
</template>
