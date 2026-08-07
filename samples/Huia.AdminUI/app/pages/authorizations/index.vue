<script setup lang="ts">
import type { AuthorizationResponse } from '~~/shared/types/admin'

const subject = ref('')
const clientId = ref('')
const filters = computed(() => ({
  subject: subject.value || undefined,
  clientId: clientId.value || undefined
}))
const { items, hasPrevious, nextCursor, loading, error, refresh, goNext, goPrevious, reset }
  = useAdminList<AuthorizationResponse>('authorizations', filters)
await refresh()
watch([subject, clientId], reset)

const columns = [
  { accessorKey: 'subject', header: 'Subject' },
  { accessorKey: 'applicationClientId', header: 'Client ID' },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'scopes', header: 'Scopes' },
  { accessorKey: 'creationDate', header: 'Created' },
  { id: 'actions', header: '' }
]

const revoking = ref<string | null>(null)

async function revoke(authorization: AuthorizationResponse) {
  revoking.value = authorization.id
  try {
    await $huiaAdmin(`authorizations/${authorization.id}/revoke`, { method: 'POST' })
    await refresh()
  } finally {
    revoking.value = null
  }
}

function formatDate(value: string | null) {
  return value ? new Date(value).toLocaleString() : '—'
}
</script>

<template>
  <UDashboardPanel
    id="authorizations-panel"
    :ui="{ body: 'gap-4' }"
  >
    <template #header>
      <UDashboardNavbar title="Authorizations">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="subtle"
            :loading="loading"
            @click="refresh"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <div class="flex gap-3">
        <UInput
          v-model="subject"
          placeholder="Filter by subject (user id)…"
          icon="i-lucide-search"
          class="max-w-xs"
        />
        <UInput
          v-model="clientId"
          placeholder="Filter by client id…"
          icon="i-lucide-search"
          class="max-w-xs"
        />
      </div>

      <UAlert
        v-if="error && !loading"
        color="error"
        variant="subtle"
        :title="error"
      />

      <UTable
        :data="items"
        :columns="columns"
        :loading="loading"
      >
        <template #status-cell="{ row }">
          <UBadge
            :color="row.original.status === 'valid' ? 'success' : 'neutral'"
            variant="subtle"
          >
            {{ row.original.status || '—' }}
          </UBadge>
        </template>
        <template #scopes-cell="{ row }">
          <span class="text-sm text-muted">{{ row.original.scopes.join(', ') || '—' }}</span>
        </template>
        <template #creationDate-cell="{ row }">
          {{ formatDate(row.original.creationDate) }}
        </template>
        <template #actions-cell="{ row }">
          <UButton
            label="Revoke"
            icon="i-lucide-ban"
            color="error"
            variant="ghost"
            size="sm"
            :disabled="row.original.status !== 'valid'"
            :loading="revoking === row.original.id"
            @click="revoke(row.original)"
          />
        </template>
      </UTable>

      <AdminPager
        :has-previous="hasPrevious"
        :has-next="!!nextCursor"
        :loading="loading"
        @previous="goPrevious"
        @next="goNext"
      />
    </template>
  </UDashboardPanel>
</template>
