<script setup lang="ts">
const route = useRoute()
const { login, register, startPhoneLogin, verifyPhoneLogin, completePhoneProfile } = useHuia()

const tab = ref<'password' | 'phone'>('password')
const mode = ref<'login' | 'register'>('login')
const email = ref('')
const password = ref('')
const error = ref<string | null>(null)
const info = ref<string | null>(null)
const busy = ref(false)

const returnTo = computed(() => (typeof route.query.returnTo === 'string' ? route.query.returnTo : '/'))

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

  await navigateTo(returnTo.value)
}

/* ── phone tab ─────────────────────────────────────────────────────────── */
const phoneStep = ref<'number' | 'code' | 'profile'>('number')
const phoneNumber = ref('')
const code = ref('')
const flowId = ref('')
const firstName = ref('')
const lastName = ref('')

async function onPhoneStart() {
  busy.value = true
  error.value = null
  const result = await startPhoneLogin({ phoneNumber: phoneNumber.value })
  busy.value = false
  if (!result.ok || !result.flowId) {
    error.value = 'Could not send a code. Check the number.'
    return
  }
  flowId.value = result.flowId
  phoneStep.value = 'code'
}

async function onPhoneVerify() {
  busy.value = true
  error.value = null
  const result = await verifyPhoneLogin({ flowId: flowId.value, code: code.value })
  busy.value = false
  if (!result.ok) {
    error.value = 'That code did not work.'
    return
  }
  if (result.requiresProfile) {
    flowId.value = result.flowId
    phoneStep.value = 'profile'
    return
  }
  await navigateTo(returnTo.value)
}

async function onPhoneCompleteProfile() {
  busy.value = true
  error.value = null
  const result = await completePhoneProfile({ flowId: flowId.value, firstName: firstName.value, lastName: lastName.value })
  busy.value = false
  if (!result.ok) {
    error.value = 'Could not finish creating the account.'
    return
  }
  await navigateTo(returnTo.value)
}
</script>

<template>
  <section>
    <div style="display: flex; gap: 1rem; margin-bottom: 1rem; border-bottom: 1px solid #ddd;">
      <button
        type="button"
        :style="{ fontWeight: tab === 'password' ? 'bold' : 'normal', border: 'none', background: 'none', cursor: 'pointer', padding: '0.5rem 0' }"
        @click="tab = 'password'"
      >
        Email &amp; password
      </button>
      <button
        type="button"
        :style="{ fontWeight: tab === 'phone' ? 'bold' : 'normal', border: 'none', background: 'none', cursor: 'pointer', padding: '0.5rem 0' }"
        @click="tab = 'phone'"
      >
        Phone
      </button>
    </div>

    <template v-if="tab === 'password'">
      <h2>{{ mode === 'login' ? 'Sign in' : 'Create an account' }}</h2>
      <form style="display: flex; flex-direction: column; gap: 0.5rem; max-width: 20rem;" @submit.prevent="onSubmit">
        <input v-model="email" type="email" placeholder="Email" autocomplete="username" required>
        <input v-model="password" type="password" placeholder="Password" autocomplete="current-password" required>
        <button type="submit" :disabled="busy">
          {{ busy ? 'Please wait…' : mode === 'login' ? 'Sign in' : 'Create account' }}
        </button>
        <button type="button" style="background: none; border: none; text-decoration: underline; cursor: pointer;" @click="mode = mode === 'login' ? 'register' : 'login'">
          {{ mode === 'login' ? "Don't have an account? Register" : 'Already have an account? Sign in' }}
        </button>
      </form>
    </template>

    <template v-else>
      <h2>Sign in with your phone</h2>

      <form v-if="phoneStep === 'number'" style="display: flex; flex-direction: column; gap: 0.5rem; max-width: 20rem;" @submit.prevent="onPhoneStart">
        <input v-model="phoneNumber" type="tel" placeholder="+1 202 555 0123" autocomplete="tel" required>
        <button type="submit" :disabled="busy">
          {{ busy ? 'Sending…' : 'Send code' }}
        </button>
      </form>

      <form v-else-if="phoneStep === 'code'" style="display: flex; flex-direction: column; gap: 0.5rem; max-width: 20rem;" @submit.prevent="onPhoneVerify">
        <p>We sent a code to {{ phoneNumber }}.</p>
        <input v-model="code" type="text" inputmode="numeric" placeholder="123456" autocomplete="one-time-code" required>
        <button type="submit" :disabled="busy">
          {{ busy ? 'Verifying…' : 'Verify' }}
        </button>
      </form>

      <form v-else style="display: flex; flex-direction: column; gap: 0.5rem; max-width: 20rem;" @submit.prevent="onPhoneCompleteProfile">
        <p>Almost done — what's your name?</p>
        <input v-model="firstName" type="text" placeholder="First name" required>
        <input v-model="lastName" type="text" placeholder="Last name" required>
        <button type="submit" :disabled="busy">
          {{ busy ? 'Creating…' : 'Finish' }}
        </button>
      </form>
    </template>

    <p v-if="error" style="color: crimson;">
      {{ error }}
    </p>
    <p v-if="info" style="color: seagreen;">
      {{ info }}
    </p>
  </section>
</template>
