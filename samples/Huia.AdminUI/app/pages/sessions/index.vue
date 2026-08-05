<script setup lang="ts">
import type { SessionResponse } from '~~/shared/types/admin'

const page = ref(1)
const pageSize = 25
const subject = ref('')
const query = computed(() => ({ page: page.value, pageSize, subject: subject.value || undefined }))
const { items, totalCount, loading, error, refresh } = useAdminList<SessionResponse>('sessions', query)
await refresh()
watch(page, refresh)
watch(subject, () => {
  page.value = 1
  refresh()
})

const columns = [
  { accessorKey: 'userId', header: 'User ID' },
  { accessorKey: 'applicationClientIds', header: 'Applications' },
  { accessorKey: 'ipAddress', header: 'IP address' },
  { accessorKey: 'userAgent', header: 'User agent' },
  { accessorKey: 'lastActivityAt', header: 'Last activity' },
  { accessorKey: 'revokedAt', header: 'Status' },
  { id: 'actions', header: '' }
]

const revoking = ref<string | null>(null)
const revokingAll = ref(false)

async function revoke(session: SessionResponse) {
  revoking.value = session.id
  try {
    await $fetch(`/api/admin/sessions/${session.id}/revoke`, { method: 'POST' })
    await refresh()
  } finally {
    revoking.value = null
  }
}

async function revokeAll() {
  if (!subject.value) return
  revokingAll.value = true
  try {
    await $fetch('/api/admin/sessions/revoke-all', { method: 'POST', body: { subject: subject.value } })
    await refresh()
  } finally {
    revokingAll.value = false
  }
}

function formatDate(value: string | null) {
  return value ? new Date(value).toLocaleString() : '—'
}
</script>

<template>
  <UDashboardPanel id="sessions-panel" :ui="{ body: 'gap-4' }">
    <template #header>
      <UDashboardNavbar title="Sessions">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <div class="flex gap-3 items-center">
        <UInput v-model="subject" placeholder="Filter by subject (user id)…" icon="i-lucide-search" class="max-w-xs" />
        <UButton label="Revoke all for subject" icon="i-lucide-ban" color="error" variant="subtle" :disabled="!subject"
          :loading="revokingAll" @click="revokeAll" />
      </div>

      <UAlert v-if="error" color="error" variant="subtle" :title="error" />

      <UTable :data="items" :columns="columns" :loading="loading">
        <template #applicationClientIds-cell="{ row }">
          <div v-if="row.original.applicationClientIds.length" class="flex flex-wrap gap-1">
            <UBadge v-for="clientId in row.original.applicationClientIds" :key="clientId" color="neutral"
              variant="subtle">
              {{ clientId }}
            </UBadge>
          </div>
          <span v-else class="text-sm text-muted">—</span>
        </template>
        <template #userAgent-cell="{ row }">
          <span class="text-sm text-muted truncate max-w-xs block">{{ row.original.userAgent || '—' }}</span>
        </template>
        <template #lastActivityAt-cell="{ row }">
          {{ formatDate(row.original.lastActivityAt) }}
        </template>
        <template #revokedAt-cell="{ row }">
          <UBadge v-if="row.original.revokedAt" color="neutral" variant="subtle">
            Revoked
          </UBadge>
          <UBadge v-else color="success" variant="subtle">
            Active
          </UBadge>
        </template>
        <template #actions-cell="{ row }">
          <UButton label="Revoke" icon="i-lucide-ban" color="error" variant="ghost" size="sm"
            :disabled="!!row.original.revokedAt" :loading="revoking === row.original.id"
            @click="revoke(row.original)" />
        </template>
      </UTable>

      <div class="flex justify-end">
        <UPagination v-model:page="page" :total="totalCount" :items-per-page="pageSize" />
      </div>
    </template>
  </UDashboardPanel>
</template>
