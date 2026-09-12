<script setup lang="ts">
const { user, loggedIn, logout } = useHuia()

const displayName = computed(() => {
  const name = [user.value?.firstName, user.value?.lastName].filter(Boolean).join(' ')
  return user.value?.email ?? (name || user.value?.sub)
})
</script>

<template>
  <UApp>
    <div class="min-h-screen bg-default">
      <header class="border-b border-default">
        <UContainer class="flex items-center justify-between py-4">
          <NuxtLink to="/" class="text-xl font-bold text-highlighted no-underline">
            Huia Shop
          </NuxtLink>

          <div v-if="loggedIn" class="flex items-center gap-3">
            <span class="text-sm text-muted">{{ displayName }}</span>
            <UButton color="neutral" variant="soft" size="sm" @click="logout()">
              Sign out
            </UButton>
          </div>
          <UButton v-else to="/login" size="sm">
            Sign in
          </UButton>
        </UContainer>
      </header>

      <UContainer class="py-8">
        <nav class="flex gap-4 mb-6">
          <UButton to="/" variant="link" color="neutral" active-class="text-primary">
            Products
          </UButton>
          <UButton to="/cart" variant="link" color="neutral" active-class="text-primary">
            Cart
          </UButton>
        </nav>

        <NuxtPage />
      </UContainer>
    </div>
  </UApp>
</template>
