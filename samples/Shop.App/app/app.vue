<script setup lang="ts">
const { user, loggedIn, logout } = useHuia()
</script>

<template>
  <div style="font-family: system-ui; max-width: 40rem; margin: 3rem auto;">
    <header style="display: flex; justify-content: space-between; align-items: center;">
      <h1>Huia Shop</h1>
      <p v-if="loggedIn">
        {{ user?.email ?? user?.sub }}
        <button style="margin-left: 1rem" @click="logout()">
          Sign out
        </button>
      </p>
      <NuxtLink v-else to="/login">
        Sign in
      </NuxtLink>
    </header>
    <nav style="display: flex; gap: 1rem; margin: 1rem 0;">
      <NuxtLink to="/">
        Products
      </NuxtLink>
      <NuxtLink to="/cart">
        Cart
      </NuxtLink>
    </nav>
    <NuxtPage />
  </div>
</template>
