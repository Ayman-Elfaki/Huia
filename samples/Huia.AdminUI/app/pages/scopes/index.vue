<script setup lang="ts">
import type { ScopeRequest, ScopeResponse } from '~~/shared/types/admin'

const filters = ref({})
const { items, hasPrevious, nextCursor, loading, error, refresh, goNext, goPrevious }
  = useAdminList<ScopeResponse>('scopes', filters)
await refresh()

const columns = [
  { accessorKey: 'name', header: 'Name' },
  { accessorKey: 'displayName', header: 'Display name' },
  { accessorKey: 'resources', header: 'Resources' },
  { id: 'actions', header: '' }
]

const isModalOpen = ref(false)
const editing = ref<ScopeResponse | null>(null)
const saving = ref(false)
const formError = ref<string | null>(null)

const form = reactive({
  name: '',
  displayName: '',
  description: '',
  resources: ''
})

function csvToArray(value: string): string[] {
  return value.split(',').map(v => v.trim()).filter(Boolean)
}

function openCreate() {
  editing.value = null
  form.name = ''
  form.displayName = ''
  form.description = ''
  form.resources = ''
  formError.value = null
  isModalOpen.value = true
}

function openEdit(scope: ScopeResponse) {
  editing.value = scope
  form.name = scope.name
  form.displayName = scope.displayName ?? ''
  form.description = scope.description ?? ''
  form.resources = scope.resources.join(', ')
  formError.value = null
  isModalOpen.value = true
}

async function submit() {
  saving.value = true
  formError.value = null
  const body: ScopeRequest = {
    name: form.name,
    displayName: form.displayName || null,
    description: form.description || null,
    resources: csvToArray(form.resources)
  }
  try {
    if (editing.value) {
      await $huiaAdmin(`scopes/${editing.value.id}`, { method: 'PUT', body })
    } else {
      await $huiaAdmin('scopes', { method: 'POST', body })
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

async function remove(scope: ScopeResponse) {
  deletingId.value = scope.id
  try {
    await $huiaAdmin(`scopes/${scope.id}`, { method: 'DELETE' })
    await refresh()
  } finally {
    deletingId.value = null
  }
}
</script>

<template>
  <UDashboardPanel
    id="scopes-panel"
    :ui="{ body: 'gap-4' }"
  >
    <template #header>
      <UDashboardNavbar title="Scopes">
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
          <UButton
            label="New scope"
            icon="i-lucide-plus"
            @click="openCreate"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
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
        <template #displayName-cell="{ row }">
          {{ row.original.displayName || '—' }}
        </template>
        <template #resources-cell="{ row }">
          <span class="text-sm text-muted">{{ row.original.resources.join(', ') || '—' }}</span>
        </template>
        <template #actions-cell="{ row }">
          <div class="flex gap-1 justify-end">
            <UButton
              icon="i-lucide-pencil"
              color="neutral"
              variant="ghost"
              size="sm"
              @click="openEdit(row.original)"
            />
            <UButton
              icon="i-lucide-trash-2"
              color="error"
              variant="ghost"
              size="sm"
              :loading="deletingId === row.original.id"
              @click="remove(row.original)"
            />
          </div>
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

  <UModal
    v-model:open="isModalOpen"
    :title="editing ? 'Edit scope' : 'New scope'"
  >
    <template #body>
      <UForm
        :state="form"
        class="flex flex-col gap-4"
        @submit="submit"
      >
        <UFormField
          label="Name"
          name="name"
        >
          <UInput
            v-model="form.name"
            :disabled="!!editing"
            class="w-full"
          />
        </UFormField>
        <UFormField
          label="Display name"
          name="displayName"
        >
          <UInput
            v-model="form.displayName"
            class="w-full"
          />
        </UFormField>
        <UFormField
          label="Description"
          name="description"
        >
          <UTextarea
            v-model="form.description"
            class="w-full"
            :rows="2"
          />
        </UFormField>
        <UFormField
          label="Resources (comma-separated)"
          name="resources"
        >
          <UInput
            v-model="form.resources"
            placeholder="todo-api"
            class="w-full"
          />
        </UFormField>

        <UAlert
          v-if="formError"
          color="error"
          variant="subtle"
          :title="formError"
        />

        <div class="flex justify-end gap-2">
          <UButton
            label="Cancel"
            color="neutral"
            variant="subtle"
            @click="isModalOpen = false"
          />
          <UButton
            label="Save"
            type="submit"
            :loading="saving"
          />
        </div>
      </UForm>
    </template>
  </UModal>
</template>
