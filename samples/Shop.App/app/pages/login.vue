<script setup lang="ts">
const route = useRoute()
const { login, register } = useHuia()

const mode = ref<'login' | 'register'>('login')
const email = ref('')
const password = ref('')
const error = ref<string | null>(null)
const info = ref<string | null>(null)
const busy = ref(false)

async function onSubmit() {
  busy.value = true
  error.value = null
  info.value = null

  if (mode.value === 'register') {
    const result = await register({ email: email.value, password: password.value })
    busy.value = false
    if (!result.ok) {
      error.value = 'Registration failed. Check the password requirements.'
      return
    }
    info.value = 'Account created — sign in below.'
    mode.value = 'login'
    return
  }

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
    <h2>{{ mode === 'login' ? 'Sign in' : 'Create an account' }}</h2>
    <form style="display: flex; flex-direction: column; gap: 0.5rem; max-width: 20rem;" @submit.prevent="onSubmit">
      <input v-model="email" type="email" placeholder="Email" autocomplete="username" required>
      <input v-model="password" type="password" placeholder="Password" autocomplete="current-password" required>
      <button type="submit" :disabled="busy">
        {{ busy ? 'Please wait…' : mode === 'login' ? 'Sign in' : 'Create account' }}
      </button>
      <p v-if="error" style="color: crimson;">
        {{ error }}
      </p>
      <p v-if="info" style="color: seagreen;">
        {{ info }}
      </p>
      <button type="button" style="background: none; border: none; text-decoration: underline; cursor: pointer;" @click="mode = mode === 'login' ? 'register' : 'login'">
        {{ mode === 'login' ? "Don't have an account? Register" : 'Already have an account? Sign in' }}
      </button>
    </form>
  </section>
</template>
