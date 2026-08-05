<script setup lang="ts">
import type {ManageInfoResponse, TwoFactorRequest, TwoFactorResponse, UpdateInfoRequest} from '~~/shared/types/manage'

const info = ref<ManageInfoResponse | null>(null)
const infoError = ref<string | null>(null)
const infoSaved = ref(false)
const infoSaving = ref(false)
const infoForm = reactive({firstName: '', lastName: ''})

async function loadInfo() {
  infoError.value = null
  try {
    info.value = await $fetch<ManageInfoResponse>('/api/manage/info')
    infoForm.firstName = info.value.firstName ?? ''
    infoForm.lastName = info.value.lastName ?? ''
  } catch (err) {
    infoError.value = adminErrorMessage(err)
  }
}

async function saveInfo() {
  infoSaving.value = true
  infoError.value = null
  infoSaved.value = false
  try {
    const body: UpdateInfoRequest = {firstName: infoForm.firstName, lastName: infoForm.lastName}
    info.value = await $fetch<ManageInfoResponse>('/api/manage/info', {method: 'POST', body})
    infoSaved.value = true
  } catch (err) {
    infoError.value = adminErrorMessage(err)
  } finally {
    infoSaving.value = false
  }
}

const passwordForm = reactive({oldPassword: '', newPassword: ''})
const passwordSaving = ref(false)
const passwordError = ref<string | null>(null)
const passwordSaved = ref(false)

async function changePassword() {
  passwordSaving.value = true
  passwordError.value = null
  passwordSaved.value = false
  try {
    const body: UpdateInfoRequest = {oldPassword: passwordForm.oldPassword, newPassword: passwordForm.newPassword}
    await $fetch('/api/manage/info', {method: 'POST', body})
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
    twoFactor.value = await $fetch<TwoFactorResponse>('/api/manage/2fa', {method: 'POST', body})
  } catch (err) {
    twoFactorError.value = adminErrorMessage(err)
  }
}

async function submitTwoFactor(body: TwoFactorRequest) {
  twoFactorBusy.value = true
  twoFactorError.value = null
  try {
    twoFactor.value = await $fetch<TwoFactorResponse>('/api/manage/2fa', {method: 'POST', body})
  } catch (err) {
    twoFactorError.value = adminErrorMessage(err)
  } finally {
    twoFactorBusy.value = false
  }
}

async function enableTwoFactor() {
  await submitTwoFactor({enable: true, twoFactorCode: twoFactorCode.value})
  twoFactorCode.value = ''
}

const disableTwoFactor = () => submitTwoFactor({enable: false})
const regenerateRecoveryCodes = () => submitTwoFactor({resetRecoveryCodes: true})

/** Groups a raw authenticator key into 4-char chunks — matches how authenticator apps display/expect a
 * manually-entered secret. */
function formatSharedKey(key: string): string {
  return (key.match(/.{1,4}/g) ?? [key]).join(' ')
}

const otpauthUrl = computed(() => {
  if (!twoFactor.value?.sharedKey || !info.value?.email) return null
  return `otpauth://totp/Huia%20Admin:${encodeURIComponent(info.value.email)}?secret=${twoFactor.value.sharedKey}&issuer=Huia%20Admin&digits=6`
})

await Promise.all([loadInfo(), loadTwoFactor()])
</script>

<template>
  <UDashboardPanel
    id="profile-panel"
    :ui="{ body: 'gap-4' }"
  >
    <template #header>
      <UDashboardNavbar title="Profile">
        <template #leading>
          <UDashboardSidebarCollapse/>
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <div class="mx-auto flex w-full max-w-lg flex-col gap-4">
        <UCard>
          <template #header>
            <span class="font-semibold">Account info</span>
          </template>

          <UForm
            :state="infoForm"
            class="flex flex-col gap-4"
            @submit="saveInfo"
          >
            <UFormField label="Email">
              <UInput
                :model-value="info?.email"
                disabled
                class="w-full"
              />
            </UFormField>
            <UFormField
              label="First name"
              name="firstName"
            >
              <UInput
                v-model="infoForm.firstName"
                class="w-full"
              />
            </UFormField>
            <UFormField
              label="Last name"
              name="lastName"
            >
              <UInput
                v-model="infoForm.lastName"
                class="w-full"
              />
            </UFormField>
            <UAlert
              v-if="infoError"
              color="error"
              variant="subtle"
              :title="infoError"
            />
            <p
              v-if="infoSaved"
              class="text-sm text-muted"
            >
              Saved.
            </p>
            <UButton
              label="Save"
              type="submit"
              :loading="infoSaving"
              class="self-start"
            />
          </UForm>
        </UCard>

        <UCard>
          <template #header>
            <span class="font-semibold">Password</span>
          </template>

          <UForm
            :state="passwordForm"
            class="flex flex-col gap-4"
            @submit="changePassword"
          >
            <UFormField
              label="Current password"
              name="oldPassword"
            >
              <UInput
                v-model="passwordForm.oldPassword"
                type="password"
                class="w-full"
              />
            </UFormField>
            <UFormField
              label="New password"
              name="newPassword"
            >
              <UInput
                v-model="passwordForm.newPassword"
                type="password"
                class="w-full"
              />
            </UFormField>
            <UAlert
              v-if="passwordError"
              color="error"
              variant="subtle"
              :title="passwordError"
            />
            <p
              v-if="passwordSaved"
              class="text-sm text-muted"
            >
              Password changed.
            </p>
            <UButton
              label="Change password"
              type="submit"
              :loading="passwordSaving"
              class="self-start"
            />
          </UForm>
        </UCard>

        <UCard>
          <template #header>
            <span class="font-semibold">Two-factor authentication</span>
          </template>

          <div class="flex flex-col gap-4">
            <div class="flex items-center gap-2">
              <span class="text-sm">Status</span>
              <UBadge
                :color="twoFactor?.isTwoFactorEnabled ? 'success' : 'neutral'"
                variant="subtle"
              >
                {{ twoFactor?.isTwoFactorEnabled ? 'Enabled' : 'Disabled' }}
              </UBadge>
            </div>

            <UAlert
              v-if="twoFactorError"
              color="error"
              variant="subtle"
              :title="twoFactorError"
            />

            <div
              v-if="twoFactor?.recoveryCodes"
              class="flex flex-col gap-2 rounded-lg border border-error/50 bg-error/5 p-3"
            >
              <p class="text-sm font-medium">
                Save your recovery codes
              </p>
              <p class="text-xs text-muted">
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
              <p class="text-sm text-muted">
                {{ twoFactor.recoveryCodesLeft }} recovery codes left
              </p>
              <div class="flex flex-wrap gap-2">
                <UButton
                  label="Regenerate recovery codes"
                  color="neutral"
                  variant="subtle"
                  :loading="twoFactorBusy"
                  @click="regenerateRecoveryCodes"
                />
                <UButton
                  label="Disable two-factor authentication"
                  color="neutral"
                  variant="subtle"
                  :loading="twoFactorBusy"
                  @click="disableTwoFactor"
                />
              </div>
            </template>

            <template v-else>
              <div
                v-if="twoFactor?.sharedKey"
                class="flex flex-col gap-1"
              >
                <p class="text-sm text-muted">
                  Scan this key with your authenticator app, or enter it manually:
                </p>
                <code class="rounded-md bg-elevated px-2 py-1 text-xs break-all">{{
                    formatSharedKey(twoFactor.sharedKey)
                  }}</code>
                <a
                  v-if="otpauthUrl"
                  :href="otpauthUrl"
                  class="text-xs text-muted underline underline-offset-2"
                >
                  Open in authenticator app
                </a>
              </div>

              <UFormField
                label="6-digit code"
                name="code"
              >
                <UInput
                  v-model="twoFactorCode"
                  class="w-full"
                />
              </UFormField>
              <UButton
                label="Enable two-factor authentication"
                :loading="twoFactorBusy"
                class="self-start"
                @click="enableTwoFactor"
              />
            </template>
          </div>
        </UCard>
      </div>
    </template>
  </UDashboardPanel>
</template>
