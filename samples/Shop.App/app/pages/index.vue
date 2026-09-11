<script setup lang="ts">
interface Product { id: string, name: string, price: number }

const { data: products } = await useFetch<Product[]>('/api/products')
const { loggedIn } = useHuia()
const adding = ref<string | null>(null)
const message = ref<string | null>(null)

async function addToCart(productId: string) {
  adding.value = productId
  message.value = null
  try {
    await $fetch('/api/cart/items', { method: 'POST', body: { productId, quantity: 1 } })
    message.value = 'Added to cart.'
  }
  catch {
    message.value = 'Could not add to cart.'
  }
  finally {
    adding.value = null
  }
}
</script>

<template>
  <section>
    <h2>Products</h2>
    <ul style="list-style: none; padding: 0; display: flex; flex-direction: column; gap: 0.5rem;">
      <li v-for="product in products" :key="product.id" style="display: flex; justify-content: space-between; border: 1px solid #ddd; padding: 0.5rem 1rem; border-radius: 0.5rem;">
        <span>{{ product.name }} — ${{ product.price.toFixed(2) }}</span>
        <button v-if="loggedIn" :disabled="adding === product.id" @click="addToCart(product.id)">
          {{ adding === product.id ? 'Adding…' : 'Add to cart' }}
        </button>
        <NuxtLink v-else to="/login">
          Sign in to buy
        </NuxtLink>
      </li>
    </ul>
    <p v-if="message">
      {{ message }}
    </p>
  </section>
</template>
