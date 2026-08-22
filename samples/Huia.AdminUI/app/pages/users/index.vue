<script setup lang="ts">
import { Key, Lock, LockOpen, Pencil, Plus, RefreshCw, Search, Shield, Trash2 } from '@lucide/vue'
import type { AuthenticationFlowsResponse, CreateUserRequest, KeysetPage, RoleResponse, UpdateUserRequest, UserResponse } from '~~/shared/types/admin'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { InputGroup, InputGroupAddon, InputGroupInput } from '@/components/ui/input-group'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@/components/ui/table'

const search = ref('')
const filters = computed(() => ({ search: search.value || undefined }))
const { items, hasPrevious, hasNext, loading, error, refresh, goNext, goPrevious, reset }
  = useAdminList<UserResponse>('users', filters)
await refresh()
watch(search, reset)

// Which identifiers a new account can be created with - only offer what the app can actually sign a user
// back in with (see huia.Authentication.Use...Flow() server-side; UsersEndpoints.CreateAsync enforces the
// same rule regardless of what this dialog offers).
const authFlows = await $huiaAdmin<AuthenticationFlowsResponse>('authentication-flows')

// --- Create/edit ---
const isEditModalOpen = ref(false)
const editing = ref<UserResponse | null>(null)
const saving = ref(false)
const formError = ref<string | null>(null)
const createMode = ref<'email' | 'phone'>(authFlows.emailAndPasswordFlowEnabled ? 'email' : 'phone')
const form = reactive({ email: '', password: '', phoneNumber: '', firstName: '', lastName: '', emailConfirmed: true })

function openCreate() {
  editing.value = null
  createMode.value = authFlows.emailAndPasswordFlowEnabled ? 'email' : 'phone'
  form.email = ''
  form.password = ''
  form.phoneNumber = ''
  form.firstName = ''
  form.lastName = ''
  form.emailConfirmed = true
  formError.value = null
  isEditModalOpen.value = true
}

function openEdit(user: UserResponse) {
  editing.value = user
  form.email = user.email ?? ''
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
    } else if (createMode.value === 'phone') {
      const body: CreateUserRequest = {
        phoneNumber: form.phoneNumber,
        firstName: form.firstName || null,
        lastName: form.lastName || null,
        emailConfirmed: false
      }
      await $huiaAdmin('users', { method: 'POST', body })
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
  const result = await $huiaAdmin<KeysetPage<RoleResponse>>('roles')
  allRoles.value = result.data
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
  <div>
    <div class="flex flex-col gap-4 p-4 md:p-6">
      <div class="flex items-center justify-between gap-4">
        <h1 class="text-2xl font-semibold tracking-tight">
          Users
        </h1>
        <div class="flex gap-2">
          <Button
            variant="outline"
            size="icon"
            :disabled="loading"
            @click="refresh"
          >
            <RefreshCw :class="{ 'animate-spin': loading }" />
          </Button>
          <Button @click="openCreate">
            <Plus data-icon="inline-start" />
            New user
          </Button>
        </div>
      </div>

      <InputGroup class="max-w-sm">
        <InputGroupAddon>
          <Search />
        </InputGroupAddon>
        <InputGroupInput
          v-model="search"
          placeholder="Search by email or username…"
        />
      </InputGroup>

      <Alert
        v-if="error && !loading"
        variant="destructive"
      >
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <div class="rounded-lg border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Username</TableHead>
              <TableHead>Auth methods</TableHead>
              <TableHead>First name</TableHead>
              <TableHead>Last name</TableHead>
              <TableHead>Roles</TableHead>
              <TableHead>Status</TableHead>
              <TableHead class="w-0" />
            </TableRow>
          </TableHeader>
          <TableBody>
            <template v-if="loading && !items.length">
              <TableRow
                v-for="n in 5"
                :key="n"
              >
                <TableCell
                  v-for="col in 7"
                  :key="col"
                >
                  <Skeleton class="h-5 w-full" />
                </TableCell>
              </TableRow>
            </template>
            <TableEmpty
              v-else-if="!items.length"
              :colspan="7"
            >
              <Empty>
                <EmptyHeader>
                  <EmptyMedia variant="icon">
                    <RefreshCw />
                  </EmptyMedia>
                  <EmptyTitle>No users found</EmptyTitle>
                  <EmptyDescription>Try a different search, or create a new user.</EmptyDescription>
                </EmptyHeader>
              </Empty>
            </TableEmpty>
            <TableRow
              v-for="user in items"
              :key="user.id"
            >
              <TableCell class="font-medium">
                {{ user.userName || '—' }}
              </TableCell>
              <TableCell>
                <div class="flex flex-wrap gap-1">
                  <Badge
                    v-for="method in user.authenticationMethods"
                    :key="method"
                    variant="outline"
                  >
                    {{ method }}
                  </Badge>
                  <span
                    v-if="!user.authenticationMethods.length"
                    class="text-sm text-muted-foreground"
                  >—</span>
                </div>
              </TableCell>
              <TableCell>{{ user.firstName || '—' }}</TableCell>
              <TableCell>{{ user.lastName || '—' }}</TableCell>
              <TableCell>
                <div class="flex flex-wrap gap-1">
                  <Badge
                    v-for="role in user.roles"
                    :key="role"
                    variant="outline"
                  >
                    {{ role }}
                  </Badge>
                  <span
                    v-if="!user.roles.length"
                    class="text-sm text-muted-foreground"
                  >—</span>
                </div>
              </TableCell>
              <TableCell>
                <Badge
                  v-if="user.isLockedOut"
                  variant="destructive"
                >
                  Locked out
                </Badge>
                <Badge
                  v-else
                  variant="secondary"
                >
                  Active
                </Badge>
              </TableCell>
              <TableCell>
                <div class="flex justify-end gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    title="Roles"
                    @click="openRoles(user)"
                  >
                    <Shield />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    title="Reset password"
                    @click="openReset(user)"
                  >
                    <Key />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    :title="user.isLockedOut ? 'Unlock' : 'Lock out'"
                    :disabled="togglingLockoutId === user.id"
                    @click="toggleLockout(user)"
                  >
                    <component :is="user.isLockedOut ? LockOpen : Lock" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    @click="openEdit(user)"
                  >
                    <Pencil />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    :disabled="deletingId === user.id"
                    @click="remove(user)"
                  >
                    <Trash2 class="text-destructive" />
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>

      <AdminPager
        :has-previous="hasPrevious"
        :has-next="hasNext"
        :loading="loading"
        @previous="goPrevious"
        @next="goNext"
      />
    </div>

    <Dialog v-model:open="isEditModalOpen">
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{{ editing ? 'Edit user' : 'New user' }}</DialogTitle>
        </DialogHeader>

        <form
          class="flex flex-col gap-4"
          @submit.prevent="submit"
        >
          <div
            v-if="!editing && authFlows.emailAndPasswordFlowEnabled && authFlows.passwordlessFlowEnabled"
            class="flex gap-2"
          >
            <Button
              type="button"
              size="sm"
              :variant="createMode === 'email' ? 'default' : 'outline'"
              @click="createMode = 'email'"
            >
              Email
            </Button>
            <Button
              type="button"
              size="sm"
              :variant="createMode === 'phone' ? 'default' : 'outline'"
              @click="createMode = 'phone'"
            >
              Phone
            </Button>
          </div>

          <FieldGroup>
            <Field v-if="editing || createMode === 'email'">
              <FieldLabel>Email</FieldLabel>
              <Input
                v-model="form.email"
                type="email"
              />
            </Field>
            <Field v-if="!editing && createMode === 'email'">
              <FieldLabel>Password</FieldLabel>
              <Input
                v-model="form.password"
                type="password"
              />
            </Field>
            <Field v-if="!editing && createMode === 'phone'">
              <FieldLabel>Phone number</FieldLabel>
              <Input
                v-model="form.phoneNumber"
                type="tel"
                placeholder="+15551234567"
              />
            </Field>
            <Field>
              <FieldLabel>First name</FieldLabel>
              <Input v-model="form.firstName" />
            </Field>
            <Field>
              <FieldLabel>Last name</FieldLabel>
              <Input v-model="form.lastName" />
            </Field>
            <label
              v-if="editing || createMode === 'email'"
              class="flex items-center gap-2 text-sm"
            >
              <Checkbox v-model="form.emailConfirmed" />
              Email confirmed
            </label>
          </FieldGroup>

          <Alert
            v-if="formError"
            variant="destructive"
          >
            <AlertDescription>{{ formError }}</AlertDescription>
          </Alert>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              @click="isEditModalOpen = false"
            >
              Cancel
            </Button>
            <Button
              type="submit"
              :disabled="saving"
            >
              Save
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="isRolesModalOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Roles — {{ rolesTarget?.userName ?? rolesTarget?.email ?? '' }}</DialogTitle>
        </DialogHeader>

        <div class="flex flex-col gap-2">
          <div
            v-for="role in allRoles"
            :key="role.id"
            class="flex items-center justify-between"
          >
            <span class="text-sm">{{ role.name }}</span>
            <Switch
              :model-value="!!rolesTarget?.roles.includes(role.name)"
              :disabled="rolesBusy === role.id"
              @update:model-value="(value: boolean) => toggleRole(role, value)"
            />
          </div>
          <p
            v-if="!allRoles.length"
            class="text-sm text-muted-foreground"
          >
            No roles exist yet — create one from the Roles page.
          </p>
        </div>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="isResetModalOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Reset password — {{ resetTarget?.userName ?? resetTarget?.email ?? '' }}</DialogTitle>
        </DialogHeader>

        <div class="flex flex-col gap-4">
          <FieldGroup>
            <Field>
              <FieldLabel>New password</FieldLabel>
              <Input
                v-model="newPassword"
                type="password"
              />
            </Field>
          </FieldGroup>
          <Alert
            v-if="resetError"
            variant="destructive"
          >
            <AlertDescription>{{ resetError }}</AlertDescription>
          </Alert>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              @click="isResetModalOpen = false"
            >
              Cancel
            </Button>
            <Button
              :disabled="resetting"
              @click="submitReset"
            >
              Reset
            </Button>
          </DialogFooter>
        </div>
      </DialogContent>
    </Dialog>
  </div>
</template>
