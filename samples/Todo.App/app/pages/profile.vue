<script setup lang="ts">
import { AsYouType, parsePhoneNumberFromString } from 'libphonenumber-js'

definePageMeta({ middleware: 'auth' })

interface Profile {
  firstName: string
  lastName: string
  email: string | null
  phoneNumber: string | null
  phoneNumberConfirmed: boolean
}

interface Phone {
  phoneNumber: string | null
  confirmed: boolean
}

interface ExternalLogin {
  provider: string
  providerKey: string
  shortName: string
  displayName: string | null
}

interface ExternalLogins {
  logins: ExternalLogin[]
  available: string[]
  canRemoveAny: boolean
}

const { t } = useI18n()
const { data: profile, refresh } = await useHuiaData<Profile>('manage/profile')
const { data: phone, refresh: refreshPhone } = await useHuiaData<Phone>('manage/phone')
const { data: externalLogins, refresh: refreshExternal } = await useHuiaData<ExternalLogins>('manage/external-logins')

const externalError = ref('')
const manageProvidersUrl = `${useRuntimeConfig().public.huiaBaseUrl}/todo/identity/account/externallogins`

async function unlinkProvider(login: ExternalLogin) {
  externalError.value = ''
  try {
    await $huia(`manage/external-logins/${encodeURIComponent(login.provider)}/${encodeURIComponent(login.providerKey)}`, {
      method: 'DELETE',
    })
    await refreshExternal()
  }
  catch (e: any) {
    externalError.value = e?.data?.errors?.externalLogin?.[0]
      ?? e?.data?.detail
      ?? e?.data?.data?.detail
      ?? t('profile.unlinkError')
  }
}

const first = ref('')
const last = ref('')
const saved = ref(false)
const error = ref('')

watchEffect(() => {
  if (profile.value) {
    first.value = profile.value.firstName
    last.value = profile.value.lastName
  }
})

async function save() {
  error.value = ''
  saved.value = false
  try {
    await $huia('manage/profile', { method: 'PUT', body: { firstName: first.value, lastName: last.value } })
    saved.value = true
    await refresh()
  }
  catch (e: any) {
    error.value = e?.data?.detail ?? e?.data?.data?.detail ?? t('profile.saveError')
  }
}

// --- phone management ---
const newNumber = ref('')
const code = ref('')
const codeSent = ref(false)
const phoneMessage = ref('')
const phoneError = ref('')

// Client-side E.164 validation/formatting; the Huia identity provider validates again server-side.
const phoneValid = computed(() => {
  if (!newNumber.value.trim()) return true
  return parsePhoneNumberFromString(newNumber.value)?.isValid() ?? false
})

function formatNumber() {
  if (!newNumber.value.trim()) return
  newNumber.value = new AsYouType().input(newNumber.value)
}

async function sendCode() {
  phoneError.value = ''
  phoneMessage.value = ''

  const parsed = parsePhoneNumberFromString(newNumber.value)
  if (!parsed?.isValid()) {
    phoneError.value = t('profile.phoneInvalid')
    return
  }

  try {
    await $huia('manage/phone', { method: 'PUT', body: { phoneNumber: parsed.number } })
    codeSent.value = true
    phoneMessage.value = t('profile.codeSent', { number: parsed.formatInternational() })
  }
  catch (e: any) {
    phoneError.value = e?.data?.errors?.phoneNumber?.[0]
      ?? e?.data?.detail
      ?? e?.data?.data?.detail
      ?? t('profile.sendCodeError')
  }
}

async function confirmCode() {
  phoneError.value = ''
  try {
    await $huia('manage/phone/confirm', { method: 'POST', body: { code: code.value } })
    codeSent.value = false
    code.value = ''
    newNumber.value = ''
    phoneMessage.value = t('profile.phoneVerified')
    await Promise.all([refreshPhone(), refresh()])
  }
  catch (e: any) {
    phoneError.value = e?.data?.error === 'no_pending_change'
      ? t('profile.noPending')
      : t('profile.codeWrong')
  }
}

async function removePhone() {
  phoneError.value = ''
  await $huia('manage/phone', { method: 'DELETE' })
  phoneMessage.value = t('profile.phoneRemoved')
  await Promise.all([refreshPhone(), refresh()])
}
</script>

<template>
  <section class="flex flex-col gap-6">
    <h1 class="text-2xl font-bold tracking-tight">{{ t('profile.title') }}</h1>

    <Card>
      <CardHeader>
        <CardTitle>{{ t('profile.detailsTitle') }}</CardTitle>
        <CardDescription>{{ t('profile.detailsSubtitle') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form class="flex flex-col gap-4" @submit.prevent="save">
          <div class="grid grid-cols-2 gap-3">
            <label class="flex flex-col gap-1 text-sm">
              <span class="font-medium">{{ t('profile.firstName') }}</span>
              <Input v-model="first" />
            </label>
            <label class="flex flex-col gap-1 text-sm">
              <span class="font-medium">{{ t('profile.lastName') }}</span>
              <Input v-model="last" />
            </label>
          </div>
          <p class="text-sm text-muted-foreground" data-testid="profile-email">{{ t('profile.email', { email: profile?.email ?? '—' }) }}</p>
          <div class="flex items-center gap-3">
            <Button type="submit">{{ t('profile.save') }}</Button>
            <span v-if="saved" class="text-sm text-primary" data-testid="name-saved">{{ t('profile.saved') }}</span>
            <span v-if="error" class="text-sm text-destructive">{{ error }}</span>
          </div>
        </form>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle>{{ t('profile.phoneTitle') }}</CardTitle>
        <CardDescription>{{ t('profile.phoneSubtitle') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex flex-col gap-4" data-testid="phone-widget">
          <p class="text-sm">
            {{ t('profile.phoneCurrent') }}
            <span class="font-medium">{{ phone?.phoneNumber ?? t('profile.phoneNone') }}</span>
            <Badge v-if="phone?.confirmed" variant="secondary" class="ml-2" data-testid="phone-verified">{{ t('profile.verified') }}</Badge>
            <Badge v-else-if="phone?.phoneNumber" variant="outline" class="ml-2">{{ t('profile.unverified') }}</Badge>
          </p>

          <div v-if="!codeSent" class="flex flex-col gap-1">
            <div class="flex gap-2">
              <Input v-model="newNumber" type="tel" placeholder="+1 555 010 0123" @blur="formatNumber" />
              <Button type="button" data-testid="phone-send" :disabled="!newNumber || !phoneValid" @click="sendCode">{{ t('profile.sendCode') }}</Button>
              <Button v-if="phone?.phoneNumber" type="button" variant="destructive" data-testid="phone-remove" @click="removePhone">{{ t('profile.remove') }}</Button>
            </div>
            <p v-if="newNumber && !phoneValid" class="text-sm text-amber-500" data-testid="phone-format-hint">
              {{ t('profile.formatHint') }}
            </p>
          </div>

          <div v-else class="flex gap-2">
            <Input v-model="code" :placeholder="t('profile.codePlaceholder')" />
            <Button type="button" data-testid="phone-confirm" :disabled="!code" @click="confirmCode">{{ t('profile.confirm') }}</Button>
            <Button type="button" variant="ghost" @click="codeSent = false">{{ t('profile.cancel') }}</Button>
          </div>

          <p v-if="phoneMessage" class="text-sm text-primary" data-testid="phone-message">{{ phoneMessage }}</p>
          <p v-if="phoneError" class="text-sm text-destructive" data-testid="phone-error">{{ phoneError }}</p>
        </div>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle>{{ t('profile.linkedTitle') }}</CardTitle>
        <CardDescription>{{ t('profile.linkedSubtitle') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex flex-col gap-3" data-testid="linked-accounts">
          <p v-if="!externalLogins?.logins.length" class="text-sm text-muted-foreground" data-testid="linked-none">
            {{ t('profile.linkedNone') }}
          </p>
          <ul v-else class="flex flex-col gap-2">
            <li v-for="login in externalLogins.logins" :key="login.provider" class="flex items-center justify-between gap-3 text-sm">
              <span class="font-medium">{{ login.displayName ?? login.shortName }}</span>
              <Button
                type="button"
                variant="outline"
                size="sm"
                :data-testid="`unlink-${login.shortName}`"
                :disabled="!externalLogins.canRemoveAny"
                @click="unlinkProvider(login)"
              >
                {{ t('profile.unlink') }}
              </Button>
            </li>
          </ul>
          <a
            :href="manageProvidersUrl"
            class="text-sm text-primary underline"
            data-testid="manage-providers"
          >{{ t('profile.manageProviders') }}</a>
          <p v-if="externalError" class="text-sm text-destructive" data-testid="linked-error">{{ externalError }}</p>
        </div>
      </CardContent>
    </Card>
  </section>
</template>
