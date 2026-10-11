<script setup lang="ts">
const route = useRoute()
const { login, register, startPhoneLogin, resendPhoneLogin, verifyPhoneLogin, completePhoneProfile, externalLoginHref } = useHuia()

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
    const result = await register({ email: email.value, password: password.value, firstName: firstName.value, lastName: lastName.value, phoneNumber: phoneNumber.value })
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
const maskedPhone = ref('')
const codeLength = ref(6)
const resendIn = ref(0)
let resendTimer: ReturnType<typeof setInterval> | undefined

type PhoneProblem = { error?: string, retryAfterSeconds?: number | null, attemptsRemaining?: number | null }

function problemOf(err: unknown): PhoneProblem {
  return (err && typeof err === 'object' ? err : {}) as PhoneProblem
}

function describe(err: unknown, fallback: string): string {
  const p = problemOf(err)
  switch (p.error) {
    case 'rate_limited':
      return p.retryAfterSeconds ? `Too many attempts. Try again in ${p.retryAfterSeconds}s.` : 'Too many attempts. Try again shortly.'
    case 'daily_limit':
      return 'This number has reached its daily sign-in limit. Try again tomorrow.'
    case 'resend_too_soon':
      return `Please wait ${p.retryAfterSeconds ?? 'a few'}s before requesting a new code.`
    case 'expired':
      return 'That code has expired. Send a new one.'
    case 'too_many_attempts':
      return 'Too many incorrect attempts. Send a new code.'
    case 'invalid':
      return p.attemptsRemaining != null ? `That code is not correct. ${p.attemptsRemaining} attempts left.` : 'That code is not correct.'
    default:
      return fallback
  }
}

function startCountdown(seconds: number) {
  clearInterval(resendTimer)
  resendIn.value = seconds
  if (seconds <= 0) return
  resendTimer = setInterval(() => {
    resendIn.value = Math.max(0, resendIn.value - 1)
    if (resendIn.value === 0) clearInterval(resendTimer)
  }, 1000)
}
onBeforeUnmount(() => clearInterval(resendTimer))

async function onPhoneStart() {
  busy.value = true
  error.value = null
  const result = await startPhoneLogin({ phoneNumber: phoneNumber.value })
  busy.value = false
  if (!result.ok || !result.flowId) {
    error.value = describe(result.error, 'Could not send a code. Check the number.')
    return
  }
  flowId.value = result.flowId
  maskedPhone.value = result.maskedPhoneNumber ?? phoneNumber.value
  codeLength.value = result.codeLength ?? 6
  startCountdown(result.resendAvailableInSeconds ?? 0)
  code.value = ''
  phoneStep.value = 'code'
}

async function onPhoneResend() {
  busy.value = true
  error.value = null
  info.value = null
  const result = await resendPhoneLogin({ flowId: flowId.value })
  busy.value = false
  if (!result.ok) {
    const retry = problemOf(result.error).retryAfterSeconds
    if (retry) startCountdown(retry)
    error.value = describe(result.error, 'Could not send a new code.')
    return
  }
  startCountdown(result.resendAvailableInSeconds ?? 0)
  info.value = 'We sent you a new code.'
}

function onChangeNumber() {
  clearInterval(resendTimer)
  error.value = null
  info.value = null
  code.value = ''
  phoneStep.value = 'number'
}

async function onPhoneVerify() {
  busy.value = true
  error.value = null
  const result = await verifyPhoneLogin({ flowId: flowId.value, code: code.value })
  busy.value = false
  if (!result.ok) {
    error.value = describe(result.error, 'That code did not work.')
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
          <template v-if="mode === 'register'">
            <UFormField label="Phone number">
              <UInput v-model="phoneNumber" type="tel" placeholder="Phone number" autocomplete="tel" required class="w-full" />
            </UFormField>
          </template>
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
            We sent a {{ codeLength }}-digit code to {{ maskedPhone }}.
          </p>
          <UFormField label="Code">
            <UInput
              v-model="code"
              type="text"
              inputmode="numeric"
              :maxlength="codeLength"
              :placeholder="'0'.repeat(codeLength)"
              autocomplete="one-time-code"
              required
              class="w-full"
            />
          </UFormField>
          <UButton type="submit" block :loading="busy">
            Verify
          </UButton>
          <div class="flex justify-between">
            <UButton type="button" variant="link" color="neutral" size="sm" @click="onChangeNumber">
              Use a different number
            </UButton>
            <UButton type="button" variant="link" size="sm" :disabled="busy || resendIn > 0" @click="onPhoneResend">
              {{ resendIn > 0 ? `Send a new code in ${resendIn}s` : 'Send a new code' }}
            </UButton>
          </div>
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
        <UButton :to="externalLoginHref('huia', returnTo)" external variant="outline" color="neutral" block>
          Sign in with Partner
        </UButton>
      </template>
    </UCard>
  </div>
</template>
