<script setup lang="ts">
// This page is CSR-only (see the `/auth/callback` entry in nuxt.config.ts's `routeRules` for why —
// server-rendering the exchange call breaks it two different ways).

const route = useRoute()
const { exchangeExternalCode, completeExternalProfile } = useHuia()

const code = typeof route.query.code === 'string' ? route.query.code : null
const returnTo = typeof route.query.returnTo === 'string' ? route.query.returnTo : '/'

const step = ref<'working' | 'profile' | 'error'>('working')
const flowCode = ref('')
const firstName = ref('')
const lastName = ref('')
const busy = ref(false)
const error = ref<string | null>(null)

async function run() {
  if (!code) {
    step.value = 'error'
    error.value = 'No sign-in code was returned.'
    return
  }

  const result = await exchangeExternalCode(code)
  if (!result.ok) {
    step.value = 'error'
    error.value = 'Sign-in did not complete. Please try again.'
    return
  }

  if (result.requiresProfile) {
    flowCode.value = result.flowId
    firstName.value = result.firstName ?? ''
    lastName.value = result.lastName ?? ''
    step.value = 'profile'
    return
  }

  await navigateTo(returnTo)
}

async function onCompleteProfile() {
  busy.value = true
  error.value = null
  const result = await completeExternalProfile({ code: flowCode.value, firstName: firstName.value, lastName: lastName.value })
  busy.value = false
  if (!result.ok) {
    error.value = 'Could not finish creating the account.'
    return
  }
  await navigateTo(returnTo)
}

await run()
</script>

<template>
  <div class="max-w-sm mx-auto">
    <UCard>
      <template v-if="step === 'working'">
        <div class="flex items-center gap-3 text-muted">
          <UIcon name="i-lucide-loader-circle" class="animate-spin size-5" />
          Completing sign-in…
        </div>
      </template>

      <template v-else-if="step === 'profile'">
        <h2 class="text-lg font-semibold text-highlighted mb-3">
          Almost done — what's your name?
        </h2>
        <form class="space-y-3" @submit.prevent="onCompleteProfile">
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
      <UButton v-if="step === 'error'" to="/login" variant="link" class="mt-3">
        Back to sign in
      </UButton>
    </UCard>
  </div>
</template>
