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
  <div class="space-y-4">
    <h2 class="text-lg font-semibold text-highlighted">
      Products
    </h2>

    <UAlert v-if="message" color="success" variant="subtle" :description="message" />

    <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <UCard v-for="product in products" :key="product.id">
        <template #header>
          <span class="font-medium text-highlighted">{{ product.name }}</span>
        </template>

        <p class="text-2xl font-semibold text-highlighted">
          ${{ product.price.toFixed(2) }}
        </p>

        <template #footer>
          <UButton
            v-if="loggedIn"
            block
            :loading="adding === product.id"
            @click="addToCart(product.id)"
          >
            Add to cart
          </UButton>
          <UButton v-else to="/login" block variant="soft">
            Sign in to buy
          </UButton>
        </template>
      </UCard>
    </div>
  </div>
</template>
