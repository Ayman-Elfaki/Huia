<script setup lang="ts">
const { user, loggedIn } = useUserSession()
const { login, logout } = useAuth()
</script>

<template>
  <div style="font-family: system-ui; max-width: 40rem; margin: 3rem auto;">
    <h1>huia-nuxt playground</h1>
    <p v-if="loggedIn">
      Signed in as <strong>{{ user?.name ?? user?.preferred_username ?? user?.sub }}</strong>
      <button style="margin-left: 1rem" @click="logout()">Sign out</button>
    </p>
    <p v-else>
      <button @click="login()">Sign in</button>
    </p>
    <nav style="display: flex; gap: 1rem; margin: 1rem 0;">
      <NuxtLink to="/">Home</NuxtLink>
      <NuxtLink to="/protected">Protected</NuxtLink>
    </nav>
    <NuxtPage />
  </div>
</template>
