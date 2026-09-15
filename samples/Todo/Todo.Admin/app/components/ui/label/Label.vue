<script setup lang="ts">
import type { HTMLAttributes } from "vue"
import { cn } from "@/lib/utils"

// `for` is passed through as a fallthrough attribute (it is a JS keyword, so not a declared prop).
const props = defineProps<{
  class?: HTMLAttributes["class"]
}>()
</script>

<template>
  <label :class="cn('text-sm font-medium leading-none', props.class)">
    <slot />
  </label>
</template>
