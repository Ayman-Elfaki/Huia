<script setup lang="ts">
import type { CreateUserRequest, PagedResult, RoleResponse, UpdateUserRequest, UserResponse } from '~~/shared/types/admin'

const search = ref('')
const filters = computed(() => ({ search: search.value || undefined }))
const { items, hasPrevious, nextCursor, loading, error, refresh, goNext, goPrevious, reset }
  = useAdminList<UserResponse>('users', filters)
await refresh()
watch(search, reset)

const columns = [
  { accessorKey: 'email', header: 'Email' },
  { accessorKey: 'firstName', header: 'First name' },
  { accessorKey: 'lastName', header: 'Last name' },
  { accessorKey: 'roles', header: 'Roles' },
  { accessorKey: 'isLockedOut', header: 'Status' },
  { id: 'actions', header: '' }
]

// --- Create/edit ---
const isEditModalOpen = ref(false)
const editing = ref<UserResponse | null>(null)
const saving = ref(false)
const formError = ref<string | null>(null)
const form = reactive({ email: '', password: '', firstName: '', lastName: '', emailConfirmed: true })

function openCreate() {
  editing.value = null
  form.email = ''
  form.password = ''
  form.firstName = ''
  form.lastName = ''
  form.emailConfirmed = true
  formError.value = null
  isEditModalOpen.value = true
}

function openEdit(user: UserResponse) {
  editing.value = user
  form.email = user.email
  form.firstName = user.firstName ?? ''
  form.lastName = user.lastName ?? ''
  form.emailConfirmed = user.emailConfirmed
  formError.value = null
  isEditModalOpen.value = true
}

async function submit() {
  saving.value = true
  formError.value = null
  try {
    if (editing.value) {
      const body: UpdateUserRequest = {
        email: form.email,
        firstName: form.firstName || null,
        lastName: form.lastName || null,
        emailConfirmed: form.emailConfirmed
      }
      await $huiaAdmin(`users/${editing.value.id}`, { method: 'PUT', body })
    } else {
      const body: CreateUserRequest = {
        email: form.email,
        password: form.password,
        firstName: form.firstName || null,
        lastName: form.lastName || null,
        emailConfirmed: form.emailConfirmed
      }
      await $huiaAdmin('users', { method: 'POST', body })
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

async function remove(user: UserResponse) {
  deletingId.value = user.id
  try {
    await $huiaAdmin(`users/${user.id}`, { method: 'DELETE' })
    await refresh()
  } finally {
    deletingId.value = null
  }
}

const togglingLockoutId = ref<string | null>(null)

async function toggleLockout(user: UserResponse) {
  togglingLockoutId.value = user.id
  try {
    await $huiaAdmin(`users/${user.id}/lockout`, { method: 'POST', body: { locked: !user.isLockedOut } })
    await refresh()
  } finally {
    togglingLockoutId.value = null
  }
}

// --- Roles ---
const isRolesModalOpen = ref(false)
const rolesTarget = ref<UserResponse | null>(null)
const allRoles = ref<RoleResponse[]>([])
const rolesBusy = ref<string | null>(null)

async function openRoles(user: UserResponse) {
  rolesTarget.value = user
  isRolesModalOpen.value = true
  const result = await $huiaAdmin<PagedResult<RoleResponse>>('roles')
  allRoles.value = result.items
}

async function toggleRole(role: RoleResponse, add: boolean) {
  if (!rolesTarget.value) return
  rolesBusy.value = role.id
  try {
    const updated = await $huiaAdmin<UserResponse>(`users/${rolesTarget.value.id}/roles`, {
      method: 'POST',
      body: { role: role.name, add }
    })
    rolesTarget.value = updated
    await refresh()
  } finally {
    rolesBusy.value = null
  }
}

// --- Reset password ---
const isResetModalOpen = ref(false)
const resetTarget = ref<UserResponse | null>(null)
const newPassword = ref('')
const resetError = ref<string | null>(null)
const resetting = ref(false)

function openReset(user: UserResponse) {
  resetTarget.value = user
  newPassword.value = ''
  resetError.value = null
  isResetModalOpen.value = true
}

async function submitReset() {
  if (!resetTarget.value) return
  resetting.value = true
  resetError.value = null
  try {
    await $huiaAdmin(`users/${resetTarget.value.id}/reset-password`, {
      method: 'POST',
      body: { newPassword: newPassword.value }
    })
    isResetModalOpen.value = false
  } catch (err) {
    resetError.value = adminErrorMessage(err)
  } finally {
    resetting.value = false
  }
}
</script>

<template>
  <UDashboardPanel
    id="users-panel"
    :ui="{ body: 'gap-4' }"
  >
    <template #header>
      <UDashboardNavbar title="Users">
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
            label="New user"
            icon="i-lucide-plus"
            @click="openCreate"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <UInput
        v-model="search"
        placeholder="Search by email or username…"
        icon="i-lucide-search"
        class="max-w-sm"
      />

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
        <template #firstName-cell="{ row }">
          {{ row.original.firstName || '—' }}
        </template>
        <template #lastName-cell="{ row }">
          {{ row.original.lastName || '—' }}
        </template>
        <template #roles-cell="{ row }">
          <div class="flex gap-1 flex-wrap">
            <UBadge
              v-for="role in row.original.roles"
              :key="role"
              variant="subtle"
              color="primary"
            >
              {{ role }}
            </UBadge>
            <span
              v-if="!row.original.roles.length"
              class="text-muted text-sm"
            >—</span>
          </div>
        </template>
        <template #isLockedOut-cell="{ row }">
          <UBadge
            v-if="row.original.isLockedOut"
            color="error"
            variant="subtle"
          >
            Locked out
          </UBadge>
          <UBadge
            v-else
            color="success"
            variant="subtle"
          >
            Active
          </UBadge>
        </template>
        <template #actions-cell="{ row }">
          <div class="flex gap-1 justify-end">
            <UButton
              icon="i-lucide-shield"
              color="neutral"
              variant="ghost"
              size="sm"
              title="Roles"
              @click="openRoles(row.original)"
            />
            <UButton
              icon="i-lucide-key"
              color="neutral"
              variant="ghost"
              size="sm"
              title="Reset password"
              @click="openReset(row.original)"
            />
            <UButton
              :icon="row.original.isLockedOut ? 'i-lucide-lock-open' : 'i-lucide-lock'"
              color="neutral"
              variant="ghost"
              size="sm"
              :title="row.original.isLockedOut ? 'Unlock' : 'Lock out'"
              :loading="togglingLockoutId === row.original.id"
              @click="toggleLockout(row.original)"
            />
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
    v-model:open="isEditModalOpen"
    :title="editing ? 'Edit user' : 'New user'"
  >
    <template #body>
      <UForm
        :state="form"
        class="flex flex-col gap-4"
        @submit="submit"
      >
        <UFormField
          label="Email"
          name="email"
        >
          <UInput
            v-model="form.email"
            type="email"
            class="w-full"
          />
        </UFormField>
        <UFormField
          v-if="!editing"
          label="Password"
          name="password"
        >
          <UInput
            v-model="form.password"
            type="password"
            class="w-full"
          />
        </UFormField>
        <UFormField
          label="First name"
          name="firstName"
        >
          <UInput
            v-model="form.firstName"
            class="w-full"
          />
        </UFormField>
        <UFormField
          label="Last name"
          name="lastName"
        >
          <UInput
            v-model="form.lastName"
            class="w-full"
          />
        </UFormField>
        <UCheckbox
          v-model="form.emailConfirmed"
          label="Email confirmed"
        />

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
            @click="isEditModalOpen = false"
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

  <UModal
    v-model:open="isRolesModalOpen"
    :title="`Roles — ${rolesTarget?.email ?? ''}`"
  >
    <template #body>
      <div class="flex flex-col gap-2">
        <div
          v-for="role in allRoles"
          :key="role.id"
          class="flex items-center justify-between"
        >
          <span>{{ role.name }}</span>
          <USwitch
            :model-value="!!rolesTarget?.roles.includes(role.name)"
            :loading="rolesBusy === role.id"
            @update:model-value="(value: boolean) => toggleRole(role, value)"
          />
        </div>
        <p
          v-if="!allRoles.length"
          class="text-muted text-sm"
        >
          No roles exist yet — create one from the Roles page.
        </p>
      </div>
    </template>
  </UModal>

  <UModal
    v-model:open="isResetModalOpen"
    :title="`Reset password — ${resetTarget?.email ?? ''}`"
  >
    <template #body>
      <div class="flex flex-col gap-4">
        <UFormField
          label="New password"
          name="newPassword"
        >
          <UInput
            v-model="newPassword"
            type="password"
            class="w-full"
          />
        </UFormField>
        <UAlert
          v-if="resetError"
          color="error"
          variant="subtle"
          :title="resetError"
        />
        <div class="flex justify-end gap-2">
          <UButton
            label="Cancel"
            color="neutral"
            variant="subtle"
            @click="isResetModalOpen = false"
          />
          <UButton
            label="Reset"
            :loading="resetting"
            @click="submitReset"
          />
        </div>
      </div>
    </template>
  </UModal>
</template>
