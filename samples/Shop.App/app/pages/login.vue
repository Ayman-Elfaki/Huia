<script setup lang="ts">
const route = useRoute()
const { login, register, startPhoneLogin, verifyPhoneLogin, completePhoneProfile, externalLoginHref } = useHuia()

const tab = ref<'password' | 'phone'>('password')
const mode = ref<'login' | 'register'>('login')
const email = ref('')
const password = ref('')
// Shared with the phone tab's complete-profile step below — registration and phone sign-up are never
// in progress at the same time, so reusing one pair of refs is simpler than duplicating them.
const firstName = ref('')
const lastName = ref('')
const error = ref<string | null>(null)
const info = ref<string | null>(null)
const busy = ref(false)

const returnTo = computed(() => (typeof route.query.returnTo === 'string' ? route.query.returnTo : '/'))

async function onSubmit() {
  busy.value = true
  error.value = null
  info.value = null

  if (mode.value === 'register') {
    const result = await register({ email: email.value, password: password.value, firstName: firstName.value, lastName: lastName.value })
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
  <div class="max-w-sm mx-auto">
    <UCard>
      <div class="flex gap-1 mb-4 rounded-lg bg-elevated p-1">
        <UButton
          class="flex-1 justify-center"
          :color="tab === 'password' ? 'primary' : 'neutral'"
          :variant="tab === 'password' ? 'solid' : 'ghost'"
          size="sm"
          @click="tab = 'password'"
        >
          Email &amp; password
        </UButton>
        <UButton
          class="flex-1 justify-center"
          :color="tab === 'phone' ? 'primary' : 'neutral'"
          :variant="tab === 'phone' ? 'solid' : 'ghost'"
          size="sm"
          @click="tab = 'phone'"
        >
          Phone
        </UButton>
      </div>

      <template v-if="tab === 'password'">
        <h2 class="text-lg font-semibold text-highlighted mb-3">
          {{ mode === 'login' ? 'Sign in' : 'Create an account' }}
        </h2>
        <form class="space-y-3" @submit.prevent="onSubmit">
          <template v-if="mode === 'register'">
            <UFormField label="First name">
              <UInput v-model="firstName" type="text" placeholder="First name" autocomplete="given-name" required class="w-full" />
            </UFormField>
            <UFormField label="Last name">
              <UInput v-model="lastName" type="text" placeholder="Last name" autocomplete="family-name" required class="w-full" />
            </UFormField>
          </template>
          <UFormField label="Email">
            <UInput v-model="email" type="email" placeholder="Email" autocomplete="username" required class="w-full" />
          </UFormField>
          <UFormField label="Password">
            <UInput v-model="password" type="password" placeholder="Password" autocomplete="current-password" required class="w-full" />
          </UFormField>
          <UButton type="submit" block :loading="busy">
            {{ mode === 'login' ? 'Sign in' : 'Create account' }}
          </UButton>
          <UButton type="button" variant="link" color="neutral" block @click="mode = mode === 'login' ? 'register' : 'login'">
            {{ mode === 'login' ? "Don't have an account? Register" : 'Already have an account? Sign in' }}
          </UButton>
        </form>
      </template>

      <template v-else>
        <h2 class="text-lg font-semibold text-highlighted mb-3">
          Sign in with your phone
        </h2>

        <form v-if="phoneStep === 'number'" class="space-y-3" @submit.prevent="onPhoneStart">
          <UFormField label="Phone number">
            <UInput v-model="phoneNumber" type="tel" placeholder="+1 202 555 0123" autocomplete="tel" required class="w-full" />
          </UFormField>
          <UButton type="submit" block :loading="busy">
            Send code
          </UButton>
        </form>

        <form v-else-if="phoneStep === 'code'" class="space-y-3" @submit.prevent="onPhoneVerify">
          <p class="text-sm text-muted">
            We sent a code to {{ phoneNumber }}.
          </p>
          <UFormField label="Code">
            <UInput v-model="code" type="text" inputmode="numeric" placeholder="123456" autocomplete="one-time-code" required class="w-full" />
          </UFormField>
          <UButton type="submit" block :loading="busy">
            Verify
          </UButton>
        </form>

        <form v-else class="space-y-3" @submit.prevent="onPhoneCompleteProfile">
          <p class="text-sm text-muted">
            Almost done — what's your name?
          </p>
          <UFormField label="First name">
            <UInput v-model="firstName" type="text" placeholder="First name" required class="w-full" />
          </UFormField>
          <UFormField label="Last name">
            <UInput v-model="lastName" type="text" placeholder="Last name" required class="w-full" />
          </UFormField>
          <UButton type="submit" block :loading="busy">
            Finish
          </UButton>
        </form>
      </template>

      <UAlert v-if="error" color="error" variant="subtle" :description="error" class="mt-3" />
      <UAlert v-if="info" color="success" variant="subtle" :description="info" class="mt-3" />

      <template #footer>
        <UButton :to="externalLoginHref('HuiaExternal', returnTo)" external variant="outline" color="neutral" block>
          Sign in with Partner
        </UButton>
      </template>
    </UCard>
  </div>
</template>
