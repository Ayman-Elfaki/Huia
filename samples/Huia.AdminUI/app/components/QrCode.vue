<script setup lang="ts">
import QRCode from 'qrcode'

const props = withDefaults(defineProps<{ value: string, size?: number }>(), { size: 176 })

const dataUrl = ref<string | null>(null)

watch(() => props.value, async (value) => {
  dataUrl.value = await QRCode.toDataURL(value, { width: props.size, margin: 1 })
}, { immediate: true })
</script>

<template>
  <img v-if="dataUrl" :src="dataUrl" :width="size" :height="size" alt="" class="rounded-md">
  <div v-else class="animate-pulse rounded-md bg-elevated" :style="{ width: `${size}px`, height: `${size}px` }" />
</template>
