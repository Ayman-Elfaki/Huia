<script setup lang="ts">
import QRCode from 'qrcode'
import { Skeleton } from '@/components/ui/skeleton'

const props = withDefaults(defineProps<{ value: string, size?: number }>(), { size: 176 })

const dataUrl = ref<string | null>(null)

watch(() => props.value, async (value) => {
  dataUrl.value = await QRCode.toDataURL(value, { width: props.size, margin: 1 })
}, { immediate: true })
</script>

<template>
  <img
    v-if="dataUrl"
    :src="dataUrl"
    :width="size"
    :height="size"
    alt=""
    class="rounded-md"
  >
  <Skeleton
    v-else
    class="rounded-md"
    :style="{ width: `${size}px`, height: `${size}px` }"
  />
</template>
