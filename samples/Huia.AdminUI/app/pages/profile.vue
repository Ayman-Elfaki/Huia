<script setup lang="ts">
import type {
  ExternalLoginsResponse,
  ManageInfoResponse,
  TwoFactorRequest,
  TwoFactorResponse,
  UpdateInfoRequest
} from '~~/shared/types/manage'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'

const info = ref<ManageInfoResponse | null>(null)
const infoError = ref<string | null>(null)
const infoSaved = ref(false)
const infoSaving = ref(false)
const infoForm = reactive({ firstName: '', lastName: '' })

async function loadInfo() {
  infoError.value = null
  try {
    info.value = await $huiaManage<ManageInfoResponse>('info')
    infoForm.firstName = info.value.firstName ?? ''
    infoForm.lastName = info.value.lastName ?? ''
  } catch (err) {
    infoError.value = adminErrorMessage(err)
  }
}

// A user signed in only through an external provider (Google, etc.) has no local password. The
// change-password form needs a current password to prove ownership, so it's hidden without one; 2FA setup
// is hidden alongside it too, to keep this page's own account-security section scoped to password accounts.
const hasPassword = ref(true)

async function loadHasPassword() {
  try {
    const externalLogins = await $huiaManage<ExternalLoginsResponse>('external-logins')
    hasPassword.value = externalLogins.hasPassword
  } catch {
    hasPassword.value = true
  }
}

async function saveInfo() {
  infoSaving.value = true
  infoError.value = null
  infoSaved.value = false
  try {
    const body: UpdateInfoRequest = { firstName: infoForm.firstName, lastName: infoForm.lastName }
    info.value = await $huiaManage<ManageInfoResponse>('info', { method: 'POST', body })
    infoSaved.value = true
  } catch (err) {
    infoError.value = adminErrorMessage(err)
  } finally {
    infoSaving.value = false
  }
}

const passwordForm = reactive({ oldPassword: '', newPassword: '' })
const passwordSaving = ref(false)
const passwordError = ref<string | null>(null)
const passwordSaved = ref(false)

async function changePassword() {
  passwordSaving.value = true
  passwordError.value = null
  passwordSaved.value = false
  try {
    const body: UpdateInfoRequest = { oldPassword: passwordForm.oldPassword, newPassword: passwordForm.newPassword }
    await $huiaManage('info', { method: 'POST', body })
    passwordSaved.value = true
    passwordForm.oldPassword = ''
    passwordForm.newPassword = ''
  } catch (err) {
    passwordError.value = adminErrorMessage(err)
  } finally {
    passwordSaving.value = false
  }
}

const twoFactor = ref<TwoFactorResponse | null>(null)
const twoFactorError = ref<string | null>(null)
const twoFactorBusy = ref(false)
const twoFactorCode = ref('')

async function loadTwoFactor() {
  twoFactorError.value = null
  try {
    const body: TwoFactorRequest = {}
    twoFactor.value = await $huiaManage<TwoFactorResponse>('2fa', { method: 'POST', body })
  } catch (err) {
    twoFactorError.value = adminErrorMessage(err)
  }
}

async function submitTwoFactor(body: TwoFactorRequest) {
  twoFactorBusy.value = true
  twoFactorError.value = null
  try {
    twoFactor.value = await $huiaManage<TwoFactorResponse>('2fa', { method: 'POST', body })
  } catch (err) {
    twoFactorError.value = adminErrorMessage(err)
  } finally {
    twoFactorBusy.value = false
  }
}

async function enableTwoFactor() {
  await submitTwoFactor({ enable: true, twoFactorCode: twoFactorCode.value })
  twoFactorCode.value = ''
}

const disableTwoFactor = () => submitTwoFactor({ enable: false })
const regenerateRecoveryCodes = () => submitTwoFactor({ resetRecoveryCodes: true })
const startTwoFactorSetup = () => submitTwoFactor({ resetSharedKey: true })

/** Groups a raw authenticator key into 4-char chunks — matches how authenticator apps display/expect a
 * manually-entered secret. */
function formatSharedKey(key: string): string {
  return (key.match(/.{1,4}/g) ?? [key]).join(' ')
}

const otpauthUrl = computed(() => {
  if (!twoFactor.value?.sharedKey || !info.value?.email) return null
  return `otpauth://totp/Huia%20Admin:${encodeURIComponent(info.value.email)}?secret=${twoFactor.value.sharedKey}&issuer=Huia%20Admin&digits=6`
})

await Promise.all([loadInfo(), loadHasPassword()])
if (hasPassword.value) {
  await loadTwoFactor()
}
</script>

<template>
  <div class="mx-auto flex w-full max-w-lg flex-col gap-4 p-4 md:p-6">
    <h1 class="text-2xl font-semibold tracking-tight">
      Profile
    </h1>

    <Card>
      <CardHeader>
        <CardTitle>Account info</CardTitle>
      </CardHeader>
      <CardContent>
        <form
          class="flex flex-col gap-4"
          @submit.prevent="saveInfo"
        >
          <FieldGroup>
            <Field>
              <FieldLabel>Email</FieldLabel>
              <Input
                :model-value="info?.email"
                disabled
              />
            </Field>
            <Field>
              <FieldLabel>First name</FieldLabel>
              <Input v-model="infoForm.firstName" />
            </Field>
            <Field>
              <FieldLabel>Last name</FieldLabel>
              <Input v-model="infoForm.lastName" />
            </Field>
          </FieldGroup>
          <Alert
            v-if="infoError"
            variant="destructive"
          >
            <AlertDescription>{{ infoError }}</AlertDescription>
          </Alert>
          <p
            v-if="infoSaved"
            class="text-sm text-muted-foreground"
          >
            Saved.
          </p>
          <Button
            type="submit"
            :disabled="infoSaving"
            class="self-start"
          >
            Save
          </Button>
        </form>
      </CardContent>
    </Card>

    <Card v-if="hasPassword">
      <CardHeader>
        <CardTitle>Password</CardTitle>
      </CardHeader>
      <CardContent>
        <form
          class="flex flex-col gap-4"
          @submit.prevent="changePassword"
        >
          <FieldGroup>
            <Field>
              <FieldLabel>Current password</FieldLabel>
              <Input
                v-model="passwordForm.oldPassword"
                type="password"
              />
            </Field>
            <Field>
              <FieldLabel>New password</FieldLabel>
              <Input
                v-model="passwordForm.newPassword"
                type="password"
              />
            </Field>
          </FieldGroup>
          <Alert
            v-if="passwordError"
            variant="destructive"
          >
            <AlertDescription>{{ passwordError }}</AlertDescription>
          </Alert>
          <p
            v-if="passwordSaved"
            class="text-sm text-muted-foreground"
          >
            Password changed.
          </p>
          <Button
            type="submit"
            :disabled="passwordSaving"
            class="self-start"
          >
            Change password
          </Button>
        </form>
      </CardContent>
    </Card>

    <Card v-if="hasPassword">
      <CardHeader>
        <CardTitle>Two-factor authentication</CardTitle>
      </CardHeader>
      <CardContent>
        <div class="flex flex-col gap-4">
          <div class="flex items-center gap-2">
            <span class="text-sm">Status</span>
            <Badge :variant="twoFactor?.isTwoFactorEnabled ? 'default' : 'secondary'">
              {{ twoFactor?.isTwoFactorEnabled ? 'Enabled' : 'Disabled' }}
            </Badge>
          </div>

          <Alert
            v-if="twoFactorError"
            variant="destructive"
          >
            <AlertDescription>{{ twoFactorError }}</AlertDescription>
          </Alert>

          <div
            v-if="twoFactor?.recoveryCodes"
            class="flex flex-col gap-2 rounded-lg border border-destructive/50 bg-destructive/5 p-3"
          >
            <p class="text-sm font-medium">
              Save your recovery codes
            </p>
            <p class="text-xs text-muted-foreground">
              Each code can be used once if you lose access to your authenticator app. This is the only
              time they'll be shown.
            </p>
            <ul class="grid grid-cols-2 gap-1 font-mono text-xs">
              <li
                v-for="code in twoFactor.recoveryCodes"
                :key="code"
              >
                {{ code }}
              </li>
            </ul>
          </div>

          <template v-if="twoFactor?.isTwoFactorEnabled">
            <p class="text-sm text-muted-foreground">
              {{ twoFactor.recoveryCodesLeft }} recovery codes left
            </p>
            <div class="flex flex-wrap gap-2">
              <Button
                variant="secondary"
                :disabled="twoFactorBusy"
                @click="regenerateRecoveryCodes"
              >
                Regenerate recovery codes
              </Button>
              <Button
                variant="secondary"
                :disabled="twoFactorBusy"
                @click="disableTwoFactor"
              >
                Disable two-factor authentication
              </Button>
            </div>
          </template>

          <template v-else-if="twoFactor?.sharedKey">
            <div class="flex flex-col gap-2">
              <p class="text-sm text-muted-foreground">
                Scan this QR code with your authenticator app, or enter the key manually:
              </p>
              <QrCode
                v-if="otpauthUrl"
                :value="otpauthUrl"
              />
              <code class="rounded-md bg-muted px-2 py-1 text-xs break-all">{{ formatSharedKey(twoFactor.sharedKey) }}</code>
              <a
                v-if="otpauthUrl"
                :href="otpauthUrl"
                class="text-xs text-muted-foreground underline underline-offset-2"
              >
                Open in authenticator app
              </a>
            </div>

            <FieldGroup>
              <Field>
                <FieldLabel>6-digit code</FieldLabel>
                <Input v-model="twoFactorCode" />
              </Field>
            </FieldGroup>
            <Button
              :disabled="twoFactorBusy"
              class="self-start"
              @click="enableTwoFactor"
            >
              Enable two-factor authentication
            </Button>
          </template>

          <template v-else>
            <p class="text-sm text-muted-foreground">
              Set up an authenticator app to require a code at sign-in.
            </p>
            <Button
              variant="secondary"
              :disabled="twoFactorBusy"
              class="self-start"
              @click="startTwoFactorSetup"
            >
              Set up two-factor authentication
            </Button>
          </template>
        </div>
      </CardContent>
    </Card>
  </div>
</template>
