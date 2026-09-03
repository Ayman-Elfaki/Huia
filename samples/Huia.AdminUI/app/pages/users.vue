<script setup lang="ts">
import { toast } from 'vue-sonner'

definePageMeta({ middleware: 'auth' })

interface UserRow {
  id: string
  tenantId: string
  userName: string | null
  email: string | null
  emailConfirmed: boolean
  phoneNumber: string | null
  phoneNumberConfirmed: boolean
  roles: string[]
}

const tenantOptions = useTenantOptions()
const { rows, hasNext, hasPrevious, pending, error, tenant, setTenant, next, prev, refresh }
  = useKeysetList<UserRow>('admin/users')

type FormMode = 'create' | 'edit'
const form = reactive({
  open: false,
  mode: 'create' as FormMode,
  id: '',
  tenant: '',
  kind: 'password' as 'password' | 'phone',
  email: '',
  password: '',
  phoneNumber: '',
  firstName: '',
  lastName: '',
  emailConfirmed: true,
  lockoutEnabled: false,
  busy: false,
  error: '',
})
const confirmDelete = ref<UserRow | null>(null)

function openCreate() {
  Object.assign(form, {
    open: true,
    mode: 'create',
    id: '',
    tenant: tenant.value || tenantOptions.value[0]?.value || '',
    kind: 'password',
    email: '',
    password: '',
    phoneNumber: '',
    firstName: '',
    lastName: '',
    emailConfirmed: true,
    lockoutEnabled: false,
    error: '',
  })
}

function openEdit(row: UserRow) {
  Object.assign(form, {
    open: true,
    mode: 'edit',
    id: row.id,
    tenant: row.tenantId,
    firstName: '',
    lastName: '',
    emailConfirmed: row.emailConfirmed,
    lockoutEnabled: false,
    error: '',
  })
}

function problem(e: any) {
  return e?.data?.detail ?? e?.data?.data?.detail ?? e?.data?.title ?? e?.data?.data?.title ?? 'The request failed.'
}

async function submit() {
  form.error = ''
  form.busy = true
  try {
    if (form.mode === 'create') {
      const body = form.kind === 'phone'
        ? { tenant: form.tenant, phoneNumber: form.phoneNumber, firstName: form.firstName, lastName: form.lastName }
        : {
            tenant: form.tenant,
            email: form.email,
            password: form.password,
            firstName: form.firstName,
            lastName: form.lastName,
            emailConfirmed: form.emailConfirmed,
          }
      await $huia('admin/users', { method: 'POST', body })
      toast.success('User created.')
    }
    else {
      await $huia(`admin/users/${encodeURIComponent(form.id)}`, {
        method: 'PUT',
        body: {
          ...(form.firstName.trim() ? { firstName: form.firstName.trim() } : {}),
          ...(form.lastName.trim() ? { lastName: form.lastName.trim() } : {}),
          emailConfirmed: form.emailConfirmed,
          lockoutEnabled: form.lockoutEnabled,
        },
      })
      toast.success('User updated.')
    }
    form.open = false
    await refresh()
  }
  catch (e: any) {
    form.error = problem(e)
  }
  finally {
    form.busy = false
  }
}

async function remove(row: UserRow) {
  try {
    await $huia(`admin/users/${encodeURIComponent(row.id)}`, { method: 'DELETE' })
    toast.success('User deleted.')
    confirmDelete.value = null
    await refresh()
  }
  catch (e: any) {
    toast.error(problem(e))
    confirmDelete.value = null
  }
}

// --- role assignment ---
const roleEditor = ref<{ userId: string, available: string[] } | null>(null)
const roleToAdd = ref('')

async function openRoleEditor(row: UserRow) {
  roleToAdd.value = ''
  try {
    const page = await $huia<{ data: { name: string }[] }>('admin/roles', { query: { tenant: row.tenantId, size: 100 } })
    roleEditor.value = { userId: row.id, available: page.data.map(r => r.name).filter(n => !row.roles.includes(n)) }
  }
  catch (e: any) {
    toast.error(problem(e))
  }
}

async function addRole(row: UserRow) {
  if (!roleToAdd.value) return
  try {
    await $huia(`admin/users/${encodeURIComponent(row.id)}/roles`, { method: 'POST', body: { role: roleToAdd.value } })
    toast.success(`Added ${roleToAdd.value}.`)
    roleEditor.value = null
    await refresh()
  }
  catch (e: any) {
    toast.error(problem(e))
  }
}

async function removeRole(row: UserRow, role: string) {
  try {
    await $huia(`admin/users/${encodeURIComponent(row.id)}/roles/${encodeURIComponent(role)}`, { method: 'DELETE' })
    await refresh()
  }
  catch (e: any) {
    toast.error(problem(e))
  }
}
</script>

<template>
  <section class="flex flex-col gap-6">
    <div class="flex items-center justify-between gap-4">
      <h1 class="text-2xl font-bold tracking-tight">Users</h1>
      <Button data-testid="user-create" @click="openCreate">New user</Button>
    </div>

    <Card v-if="form.open" data-testid="user-form">
      <CardHeader>
        <CardTitle>{{ form.mode === 'create' ? 'New user' : 'Edit user' }}</CardTitle>
        <CardDescription>
          A password account has an email + password; a phone account has an E.164 number (also its username).
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form class="flex flex-col gap-4" @submit.prevent="submit">
          <template v-if="form.mode === 'create'">
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="flex flex-col gap-1.5">
                <Label for="user-tenant">Tenant</Label>
                <Select id="user-tenant" v-model="form.tenant" data-testid="user-tenant">
                  <option value="" disabled>Select a tenant…</option>
                  <option v-for="t in tenantOptions" :key="t.value" :value="t.value">{{ t.label }}</option>
                </Select>
              </div>
              <div class="flex flex-col gap-1.5">
                <Label for="user-kind">Account type</Label>
                <Select id="user-kind" v-model="form.kind" data-testid="user-kind">
                  <option value="password">Password (email)</option>
                  <option value="phone">Phone (SMS one-time code)</option>
                </Select>
              </div>
              <template v-if="form.kind === 'password'">
                <div class="flex flex-col gap-1.5">
                  <Label for="user-email">Email</Label>
                  <Input id="user-email" v-model="form.email" type="email" data-testid="user-email" />
                </div>
                <div class="flex flex-col gap-1.5">
                  <Label for="user-password">Password</Label>
                  <Input id="user-password" v-model="form.password" type="password" data-testid="user-password" />
                </div>
              </template>
              <div v-else class="flex flex-col gap-1.5">
                <Label for="user-phone">Phone number</Label>
                <Input id="user-phone" v-model="form.phoneNumber" data-testid="user-phone" placeholder="+15005550123" />
              </div>
            </div>
          </template>

          <div class="grid gap-4 sm:grid-cols-2">
            <div class="flex flex-col gap-1.5">
              <Label for="user-first">First name</Label>
              <Input id="user-first" v-model="form.firstName" data-testid="user-first" />
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="user-last">Last name</Label>
              <Input id="user-last" v-model="form.lastName" data-testid="user-last" />
            </div>
          </div>

          <label v-if="form.mode === 'create' && form.kind === 'password'" class="flex items-center gap-2 text-sm">
            <input v-model="form.emailConfirmed" type="checkbox" data-testid="user-email-confirmed">
            Mark the email as already confirmed
          </label>
          <label v-if="form.mode === 'edit'" class="flex items-center gap-2 text-sm">
            <input v-model="form.emailConfirmed" type="checkbox" data-testid="user-email-confirmed-edit">
            Email confirmed
          </label>
          <label v-if="form.mode === 'edit'" class="flex items-center gap-2 text-sm">
            <input v-model="form.lockoutEnabled" type="checkbox" data-testid="user-lockout">
            Lockout enabled
          </label>

          <p v-if="form.error" class="text-sm text-destructive" data-testid="user-form-error">{{ form.error }}</p>
          <div class="flex items-center gap-3">
            <Button type="submit" data-testid="user-submit" :disabled="form.busy || (form.mode === 'create' && !form.tenant)">
              {{ form.mode === 'create' ? 'Create' : 'Save' }}
            </Button>
            <Button type="button" variant="ghost" @click="form.open = false">Cancel</Button>
          </div>
        </form>
      </CardContent>
    </Card>

    <Card>
      <CardHeader class="flex-row items-center justify-between gap-4 space-y-0">
        <CardDescription>Every account across all tenants (bypasses the per-tenant filter).</CardDescription>
        <Select
          :model-value="tenant"
          class="w-56"
          data-testid="tenant-filter"
          @update:model-value="setTenant"
        >
          <option value="">All tenants</option>
          <option v-for="t in tenantOptions" :key="t.value" :value="t.value">{{ t.label }}</option>
        </Select>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <p v-if="error" class="text-sm text-destructive">
          {{ (error as any)?.data?.detail ?? (error as any)?.data?.data?.detail ?? 'Could not load users (are you an administrator?).' }}
        </p>
        <Table v-else data-testid="user-table">
          <TableHeader>
            <TableRow>
              <TableHead>Username</TableHead>
              <TableHead>Email</TableHead>
              <TableHead>Phone</TableHead>
              <TableHead>Tenant</TableHead>
              <TableHead>Roles</TableHead>
              <TableHead class="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-if="pending && rows.length === 0">
              <TableCell colspan="6" class="text-muted-foreground">Loading…</TableCell>
            </TableRow>
            <TableRow v-else-if="rows.length === 0">
              <TableCell colspan="6" class="text-muted-foreground">No users.</TableCell>
            </TableRow>
            <template v-for="row in rows" :key="row.id">
              <TableRow :data-testid="`user-${row.id}`">
                <TableCell class="font-medium">{{ row.userName ?? '—' }}</TableCell>
                <TableCell>
                  {{ row.email ?? '—' }}
                  <Badge v-if="row.email && row.emailConfirmed" variant="secondary" class="ml-1">verified</Badge>
                </TableCell>
                <TableCell>
                  {{ row.phoneNumber ?? '—' }}
                  <Badge v-if="row.phoneNumber && row.phoneNumberConfirmed" variant="secondary" class="ml-1">verified</Badge>
                </TableCell>
                <TableCell class="text-muted-foreground">{{ row.tenantId }}</TableCell>
                <TableCell>
                  <div class="flex flex-wrap items-center gap-1" :data-testid="`user-roles-${row.id}`">
                    <Badge v-for="r in row.roles" :key="r" variant="outline" class="gap-1">
                      {{ r }}
                      <button type="button" class="text-muted-foreground hover:text-foreground" :data-testid="`user-role-remove-${row.id}-${r}`" @click="removeRole(row, r)">×</button>
                    </Badge>
                    <span v-if="!row.roles.length" class="text-muted-foreground">—</span>
                  </div>
                </TableCell>
                <TableCell class="text-right">
                  <div class="flex justify-end gap-2">
                    <Button variant="ghost" size="sm" :data-testid="`user-roles-edit-${row.id}`" @click="openRoleEditor(row)">Roles</Button>
                    <Button variant="outline" size="sm" :data-testid="`user-edit-${row.id}`" @click="openEdit(row)">Edit</Button>
                    <Button variant="destructive" size="sm" :data-testid="`user-delete-${row.id}`" @click="confirmDelete = row">Delete</Button>
                  </div>
                </TableCell>
              </TableRow>
              <TableRow v-if="roleEditor && roleEditor.userId === row.id" :data-testid="`user-role-editor-${row.id}`">
                <TableCell colspan="6">
                  <div class="flex items-center gap-2 rounded-md bg-muted px-3 py-2 text-sm">
                    <span>Add a role:</span>
                    <Select v-model="roleToAdd" class="w-56" :data-testid="`user-role-select-${row.id}`">
                      <option value="" disabled>Select a role…</option>
                      <option v-for="r in roleEditor.available" :key="r" :value="r">{{ r }}</option>
                    </Select>
                    <Button size="sm" :data-testid="`user-role-add-${row.id}`" :disabled="!roleToAdd" @click="addRole(row)">Add</Button>
                    <Button variant="ghost" size="sm" @click="roleEditor = null">Close</Button>
                  </div>
                </TableCell>
              </TableRow>
              <TableRow v-if="confirmDelete && confirmDelete.id === row.id" :data-testid="`user-confirm-${row.id}`">
                <TableCell colspan="6">
                  <div class="flex items-center justify-between gap-4 rounded-md bg-muted px-3 py-2 text-sm">
                    <span>Delete <strong>{{ row.userName }}</strong> from tenant <strong>{{ row.tenantId }}</strong>?</span>
                    <div class="flex gap-2">
                      <Button variant="destructive" size="sm" :data-testid="`user-confirm-delete-${row.id}`" @click="remove(row)">Delete</Button>
                      <Button variant="ghost" size="sm" @click="confirmDelete = null">Cancel</Button>
                    </div>
                  </div>
                </TableCell>
              </TableRow>
            </template>
          </TableBody>
        </Table>

        <div class="flex items-center justify-end gap-2">
          <Button variant="outline" size="sm" data-testid="page-prev" :disabled="!hasPrevious || pending" @click="prev">
            Previous
          </Button>
          <Button variant="outline" size="sm" data-testid="page-next" :disabled="!hasNext || pending" @click="next">
            Next
          </Button>
        </div>
      </CardContent>
    </Card>
  </section>
</template>
