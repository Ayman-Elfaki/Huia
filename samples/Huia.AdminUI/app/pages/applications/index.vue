<script setup lang="ts">
import type { ApplicationKind, ApplicationRequest, ApplicationResponse } from '~~/shared/types/admin'

const page = ref(1)
const pageSize = 25
const query = computed(() => ({ page: page.value, pageSize }))
const { items, totalCount, loading, error, refresh } = useAdminList<ApplicationResponse>('applications', query)
await refresh()
watch(page, refresh)

const kinds: { label: string, value: ApplicationKind }[] = [
  { label: 'Single-page application', value: 'spa' },
  { label: 'Native', value: 'native' },
  { label: 'Server-side web', value: 'web' },
  { label: 'Machine-to-machine', value: 'm2m' },
  { label: 'Device', value: 'device' }
]

const columns = [
  { accessorKey: 'clientId', header: 'Client ID' },
  { accessorKey: 'displayName', header: 'Display name' },
  { accessorKey: 'kind', header: 'Kind' },
  { accessorKey: 'redirectUris', header: 'Redirect URIs' },
  { id: 'actions', header: '' }
]

const isModalOpen = ref(false)
const editing = ref<ApplicationResponse | null>(null)
const saving = ref(false)
const formError = ref<string | null>(null)

const form = reactive({
  kind: 'spa' as ApplicationKind,
  clientId: '',
  displayName: '',
  clientSecret: '',
  requiresPkce: true,
  requireConsent: false,
  redirectUris: '',
  postLogoutRedirectUris: '',
  scopes: ''
})

function resetForm() {
  form.kind = 'spa'
  form.clientId = ''
  form.displayName = ''
  form.clientSecret = ''
  form.requiresPkce = true
  form.requireConsent = false
  form.redirectUris = ''
  form.postLogoutRedirectUris = ''
  form.scopes = ''
  formError.value = null
}

function openCreate() {
  editing.value = null
  resetForm()
  isModalOpen.value = true
}

function openEdit(app: ApplicationResponse) {
  editing.value = app
  form.kind = app.kind as ApplicationKind
  form.clientId = app.clientId
  form.displayName = app.displayName ?? ''
  form.clientSecret = ''
  form.requiresPkce = app.requirements.includes('ft:pkce')
  form.requireConsent = app.consentType === 'explicit'
  form.redirectUris = app.redirectUris.join('\n')
  form.postLogoutRedirectUris = app.postLogoutRedirectUris.join('\n')
  form.scopes = app.permissions
    .filter(p => p.startsWith('scp:'))
    .map(p => p.slice(4))
    .join(', ')
  formError.value = null
  isModalOpen.value = true
}

function linesToArray(value: string): string[] {
  return value.split('\n').map(v => v.trim()).filter(Boolean)
}

function csvToArray(value: string): string[] {
  return value.split(',').map(v => v.trim()).filter(Boolean)
}

async function submit() {
  saving.value = true
  formError.value = null
  const body: ApplicationRequest = {
    kind: form.kind,
    clientId: form.clientId,
    displayName: form.displayName || null,
    clientSecret: form.clientSecret || null,
    requiresPkce: form.requiresPkce,
    requireConsent: form.requireConsent,
    redirectUris: linesToArray(form.redirectUris),
    postLogoutRedirectUris: linesToArray(form.postLogoutRedirectUris),
    scopes: csvToArray(form.scopes)
  }
  try {
    if (editing.value) {
      await $fetch(`/api/admin/applications/${editing.value.id}`, { method: 'PUT', body })
    } else {
      await $fetch('/api/admin/applications', { method: 'POST', body })
    }
    isModalOpen.value = false
    await refresh()
  } catch (err) {
    formError.value = adminErrorMessage(err)
  } finally {
    saving.value = false
  }
}

const deletingId = ref<string | null>(null)

async function remove(app: ApplicationResponse) {
  deletingId.value = app.id
  try {
    await $fetch(`/api/admin/applications/${app.id}`, { method: 'DELETE' })
    await refresh()
  } finally {
    deletingId.value = null
  }
}

const needsSecret = computed(() => form.kind === 'web' || form.kind === 'm2m')
</script>

<template>
  <UDashboardPanel id="applications-panel" :ui="{ body: 'gap-4' }">
    <template #header>
      <UDashboardNavbar title="Applications">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton label="New application" icon="i-lucide-plus" @click="openCreate" />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <UAlert v-if="error" color="error" variant="subtle" :title="error" />

      <UTable :data="items" :columns="columns" :loading="loading">
        <template #displayName-cell="{ row }">
          {{ row.original.displayName || '—' }}
        </template>
        <template #kind-cell="{ row }">
          <UBadge variant="subtle" color="neutral">
            {{ row.original.kind }}
          </UBadge>
        </template>
        <template #redirectUris-cell="{ row }">
          <span class="text-sm text-muted">{{ row.original.redirectUris.join(', ') || '—' }}</span>
        </template>
        <template #actions-cell="{ row }">
          <div class="flex gap-1 justify-end">
            <UButton icon="i-lucide-pencil" color="neutral" variant="ghost" size="sm" @click="openEdit(row.original)" />
            <UButton icon="i-lucide-trash-2" color="error" variant="ghost" size="sm"
              :loading="deletingId === row.original.id" @click="remove(row.original)" />
          </div>
        </template>
      </UTable>

      <div class="flex justify-end">
        <UPagination v-model:page="page" :total="totalCount" :items-per-page="pageSize" />
      </div>
    </template>
  </UDashboardPanel>

  <UModal v-model:open="isModalOpen" :title="editing ? 'Edit application' : 'New application'">
    <template #body>
      <UForm :state="form" class="flex flex-col gap-4" @submit="submit">
        <UFormField label="Kind" name="kind">
          <USelect v-model="form.kind" :items="kinds" :disabled="!!editing" value-key="value" class="w-full" />
        </UFormField>
        <UFormField label="Client ID" name="clientId">
          <UInput v-model="form.clientId" :disabled="!!editing" class="w-full" />
        </UFormField>
        <UFormField label="Display name" name="displayName">
          <UInput v-model="form.displayName" class="w-full" />
        </UFormField>
        <UFormField v-if="needsSecret"
          :label="editing ? 'Client secret (leave blank to keep current)' : 'Client secret'" name="clientSecret">
          <UInput v-model="form.clientSecret" type="password" class="w-full" />
        </UFormField>
        <div class="flex gap-6">
          <UCheckbox v-model="form.requiresPkce" label="Requires PKCE" />
          <UCheckbox v-model="form.requireConsent" label="Requires consent" />
        </div>
        <UFormField label="Redirect URIs (one per line)" name="redirectUris">
          <UTextarea v-model="form.redirectUris" class="w-full" :rows="3" />
        </UFormField>
        <UFormField label="Post-logout redirect URIs (one per line)" name="postLogoutRedirectUris">
          <UTextarea v-model="form.postLogoutRedirectUris" class="w-full" :rows="2" />
        </UFormField>
        <UFormField label="Allowed scopes (comma-separated)" name="scopes">
          <UInput v-model="form.scopes" placeholder="profile, email, todos" class="w-full" />
        </UFormField>

        <UAlert v-if="formError" color="error" variant="subtle" :title="formError" />

        <div class="flex justify-end gap-2">
          <UButton label="Cancel" color="neutral" variant="subtle" @click="isModalOpen = false" />
          <UButton label="Save" type="submit" :loading="saving" />
        </div>
      </UForm>
    </template>
  </UModal>
</template>
