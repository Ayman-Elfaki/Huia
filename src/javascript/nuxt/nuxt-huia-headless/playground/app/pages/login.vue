<script setup lang="ts">
const route = useRoute()
const { login } = useHuia()

const email = ref('')
const password = ref('')
const error = ref<string | null>(null)
const busy = ref(false)

async function onSubmit() {
  busy.value = true
  error.value = null
  const result = await login({ email: email.value, password: password.value })
  busy.value = false

  if (!result.ok) {
    error.value = 'Sign-in failed. Check your email and password.'
    return
  }

  const returnTo = typeof route.query.returnTo === 'string' ? route.query.returnTo : '/'
  await navigateTo(returnTo)
}
</script>

<template>
  <section>
    <h2>Sign in</h2>
    <form style="display: flex; flex-direction: column; gap: 0.5rem; max-width: 20rem;" @submit.prevent="onSubmit">
      <input v-model="email" type="email" placeholder="Email" autocomplete="username" required>
      <input v-model="password" type="password" placeholder="Password" autocomplete="current-password" required>
      <button type="submit" :disabled="busy">
        {{ busy ? 'Signing in…' : 'Sign in' }}
      </button>
      <p v-if="error" style="color: crimson;">
        {{ error }}
      </p>
    </form>
  </section>
</template>
