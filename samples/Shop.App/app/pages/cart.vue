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
  <section>
    <h2>Cart</h2>
    <ul v-if="items?.length">
      <li v-for="item in items" :key="item.productId">
        {{ item.productId }} × {{ item.quantity }}
      </li>
    </ul>
    <p v-else>
      Your cart is empty.
    </p>
    <button :disabled="!items?.length || busy" @click="checkout">
      {{ busy ? 'Placing order…' : 'Checkout' }}
    </button>
    <p v-if="order" style="color: seagreen;">
      Order {{ order.orderId }} placed — total ${{ order.total.toFixed(2) }}.
    </p>
    <p v-if="error" style="color: crimson;">
      {{ error }}
    </p>
  </section>
</template>
