<script setup lang="ts">
definePageMeta({ middleware: 'auth' })

interface CartItem { productId: string, quantity: number }
interface CheckoutResult { orderId: string, total: number }

const { data: items, refresh } = await useFetch<CartItem[]>('/api/cart')
const order = ref<CheckoutResult | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)

async function checkout() {
  busy.value = true
  error.value = null
  try {
    order.value = await $fetch<CheckoutResult>('/api/checkout', { method: 'POST' })
    await refresh()
  }
  catch {
    error.value = 'Checkout failed — is the cart empty?'
  }
  finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="space-y-4 max-w-md">
    <h2 class="text-lg font-semibold text-highlighted">
      Cart
    </h2>

    <UCard>
      <ul v-if="items?.length" class="divide-y divide-default">
        <li v-for="item in items" :key="item.productId" class="py-2">
          {{ item.productId }} × {{ item.quantity }}
        </li>
      </ul>
      <p v-else class="text-muted">
        Your cart is empty.
      </p>

      <template #footer>
        <UButton block :disabled="!items?.length" :loading="busy" @click="checkout">
          Checkout
        </UButton>
      </template>
    </UCard>

    <UAlert
      v-if="order"
      color="success"
      variant="subtle"
      :description="`Order ${order.orderId} placed — total $${order.total.toFixed(2)}`"
    />
    <UAlert v-if="error" color="error" variant="subtle" :description="error" />
  </div>
</template>
