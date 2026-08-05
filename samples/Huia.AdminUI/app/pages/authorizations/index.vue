<script setup lang="ts">
import type { AuthorizationResponse } from '~~/shared/types/admin'

const page = ref(1)
const pageSize = 25
const subject = ref('')
const applicationId = ref('')
const query = computed(() => ({
  page: page.value,
  pageSize,
  subject: subject.value || undefined,
  applicationId: applicationId.value || undefined
}))
const { items, totalCount, loading, error, refresh } = useAdminList<AuthorizationResponse>('authorizations', query)
await refresh()
watch(page, refresh)
watch([subject, applicationId], () => {
  page.value = 1
  refresh()
})

const columns = [
  { accessorKey: 'subject', header: 'Subject' },
  { accessorKey: 'applicationId', header: 'Application ID' },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'scopes', header: 'Scopes' },
  { accessorKey: 'creationDate', header: 'Created' },
  { id: 'actions', header: '' }
]

const revoking = ref<string | null>(null)

async function revoke(authorization: AuthorizationResponse) {
  revoking.value = authorization.id
  try {
    await $fetch(`/api/admin/authorizations/${authorization.id}/revoke`, { method: 'POST' })
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
  <UDashboardPanel id="authorizations-panel" :ui="{ body: 'gap-4' }">
    <template #header>
      <UDashboardNavbar title="Authorizations">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <div class="flex gap-3">
        <UInput v-model="subject" placeholder="Filter by subject (user id)…" icon="i-lucide-search" class="max-w-xs" />
        <UInput v-model="applicationId" placeholder="Filter by application id…" icon="i-lucide-search"
          class="max-w-xs" />
      </div>

      <UAlert v-if="error" color="error" variant="subtle" :title="error" />

      <UTable :data="items" :columns="columns" :loading="loading">
        <template #status-cell="{ row }">
          <UBadge :color="row.original.status === 'valid' ? 'success' : 'neutral'" variant="subtle">
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
          <UButton label="Revoke" icon="i-lucide-ban" color="error" variant="ghost" size="sm"
            :disabled="row.original.status !== 'valid'" :loading="revoking === row.original.id"
            @click="revoke(row.original)" />
        </template>
      </UTable>

      <div class="flex justify-end">
        <UPagination v-model:page="page" :total="totalCount" :items-per-page="pageSize" />
      </div>
    </template>
  </UDashboardPanel>
</template>
