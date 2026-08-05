<script setup lang="ts">
import type { RoleMemberResponse, RoleResponse } from '~~/shared/types/admin'

const query = ref({})
const { items, loading, error, refresh } = useAdminList<RoleResponse>('roles', query)
await refresh()

const columns = [
  { accessorKey: 'name', header: 'Name' },
  { id: 'actions', header: '' }
]

// --- Create/rename ---
const isEditModalOpen = ref(false)
const editing = ref<RoleResponse | null>(null)
const name = ref('')
const saving = ref(false)
const formError = ref<string | null>(null)

function openCreate() {
  editing.value = null
  name.value = ''
  formError.value = null
  isEditModalOpen.value = true
}

function openEdit(role: RoleResponse) {
  editing.value = role
  name.value = role.name
  formError.value = null
  isEditModalOpen.value = true
}

async function submit() {
  saving.value = true
  formError.value = null
  try {
    if (editing.value) {
      await $fetch(`/api/admin/roles/${editing.value.id}`, { method: 'PUT', body: { name: name.value } })
    } else {
      await $fetch('/api/admin/roles', { method: 'POST', body: { name: name.value } })
    }
    isEditModalOpen.value = false
    await refresh()
  } catch (err) {
    formError.value = adminErrorMessage(err)
  } finally {
    saving.value = false
  }
}

const deletingId = ref<string | null>(null)

async function remove(role: RoleResponse) {
  deletingId.value = role.id
  try {
    await $fetch(`/api/admin/roles/${role.id}`, { method: 'DELETE' })
    await refresh()
  } finally {
    deletingId.value = null
  }
}

// --- Members ---
const isMembersModalOpen = ref(false)
const membersTarget = ref<RoleResponse | null>(null)
const members = ref<RoleMemberResponse[]>([])
const membersLoading = ref(false)

async function openMembers(role: RoleResponse) {
  membersTarget.value = role
  isMembersModalOpen.value = true
  membersLoading.value = true
  try {
    members.value = await $fetch<RoleMemberResponse[]>(`/api/admin/roles/${role.id}/members`)
  } finally {
    membersLoading.value = false
  }
}
</script>

<template>
  <UDashboardPanel id="roles-panel" :ui="{ body: 'gap-4' }">
    <template #header>
      <UDashboardNavbar title="Roles">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton label="New role" icon="i-lucide-plus" @click="openCreate" />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <UAlert v-if="error && !loading" color="error" variant="subtle" :title="error" />

      <UTable :data="items" :columns="columns" :loading="loading">
        <template #actions-cell="{ row }">
          <div class="flex gap-1 justify-end">
            <UButton icon="i-lucide-users" color="neutral" variant="ghost" size="sm" title="Members"
              @click="openMembers(row.original)" />
            <UButton icon="i-lucide-pencil" color="neutral" variant="ghost" size="sm" @click="openEdit(row.original)" />
            <UButton icon="i-lucide-trash-2" color="error" variant="ghost" size="sm"
              :loading="deletingId === row.original.id" @click="remove(row.original)" />
          </div>
        </template>
      </UTable>
    </template>
  </UDashboardPanel>

  <UModal v-model:open="isEditModalOpen" :title="editing ? 'Rename role' : 'New role'">
    <template #body>
      <UForm :state="{ name }" class="flex flex-col gap-4" @submit="submit">
        <UFormField label="Name" name="name">
          <UInput v-model="name" class="w-full" />
        </UFormField>
        <UAlert v-if="formError" color="error" variant="subtle" :title="formError" />
        <div class="flex justify-end gap-2">
          <UButton label="Cancel" color="neutral" variant="subtle" @click="isEditModalOpen = false" />
          <UButton label="Save" type="submit" :loading="saving" />
        </div>
      </UForm>
    </template>
  </UModal>

  <UModal v-model:open="isMembersModalOpen" :title="`Members — ${membersTarget?.name ?? ''}`">
    <template #body>
      <div v-if="membersLoading" class="text-muted text-sm">
        Loading…
      </div>
      <ul v-else-if="members.length" class="flex flex-col gap-1">
        <li v-for="member in members" :key="member.id" class="text-sm">
          {{ member.email }}
        </li>
      </ul>
      <p v-else class="text-muted text-sm">
        No members yet.
      </p>
    </template>
  </UModal>
</template>
