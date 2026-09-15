<script setup lang="ts">
import { toast } from 'vue-sonner'

definePageMeta({ middleware: 'auth' })

interface RoleRow {
  id: string
  tenantId: string
  name: string | null
  origin: 'static' | 'dynamic'
}

const tenantOptions = useTenantOptions()
const { rows, hasNext, hasPrevious, pending, error, tenant, setTenant, next, prev, refresh }
  = useKeysetList<RoleRow>('admin/roles')

type FormMode = 'create' | 'edit'
const form = reactive({
  open: false,
  mode: 'create' as FormMode,
  id: '',
  tenant: '',
  name: '',
  busy: false,
  error: '',
})
const confirmDelete = ref<RoleRow | null>(null)

function openCreate() {
  Object.assign(form, {
    open: true,
    mode: 'create',
    id: '',
    tenant: tenant.value || tenantOptions.value[0]?.value || '',
    name: '',
    error: '',
  })
}

function openEdit(row: RoleRow) {
  Object.assign(form, { open: true, mode: 'edit', id: row.id, tenant: row.tenantId, name: row.name ?? '', error: '' })
}

function problem(e: any) {
  return e?.data?.detail ?? e?.data?.data?.detail ?? e?.data?.title ?? e?.data?.data?.title ?? 'The request failed.'
}

async function submit() {
  form.error = ''
  form.busy = true
  try {
    if (form.mode === 'create') {
      await $huia('admin/roles', { method: 'POST', body: { tenant: form.tenant, name: form.name } })
      toast.success(`Role ${form.name} created.`)
    }
    else {
      await $huia(`admin/roles/${encodeURIComponent(form.id)}`, { method: 'PUT', body: { name: form.name } })
      toast.success(`Role renamed to ${form.name}.`)
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

async function remove(row: RoleRow) {
  try {
    await $huia(`admin/roles/${encodeURIComponent(row.id)}`, { method: 'DELETE' })
    toast.success(`Role ${row.name} deleted.`)
    confirmDelete.value = null
    await refresh()
  }
  catch (e: any) {
    toast.error(problem(e))
    confirmDelete.value = null
  }
}
</script>

<template>
  <section class="flex flex-col gap-6">
    <div class="flex items-center justify-between gap-4">
      <h1 class="text-2xl font-bold tracking-tight">Roles</h1>
      <Button data-testid="role-create" @click="openCreate">New role</Button>
    </div>

    <Card v-if="form.open" data-testid="role-form">
      <CardHeader>
        <CardTitle>{{ form.mode === 'create' ? 'New role' : `Rename ${form.name}` }}</CardTitle>
        <CardDescription>Roles are per tenant; a user's roles become <code>role</code> claims in their tokens.</CardDescription>
      </CardHeader>
      <CardContent>
        <form class="flex flex-col gap-4" @submit.prevent="submit">
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="flex flex-col gap-1.5">
              <Label for="role-tenant">Tenant</Label>
              <Select id="role-tenant" v-model="form.tenant" data-testid="role-tenant" :disabled="form.mode === 'edit'">
                <option value="" disabled>Select a tenant…</option>
                <option v-for="t in tenantOptions" :key="t.value" :value="t.value">{{ t.label }}</option>
              </Select>
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="role-name">Name</Label>
              <Input id="role-name" v-model="form.name" data-testid="role-name" placeholder="billing.viewer" />
            </div>
          </div>
          <p v-if="form.error" class="text-sm text-destructive" data-testid="role-form-error">{{ form.error }}</p>
          <div class="flex items-center gap-3">
            <Button type="submit" data-testid="role-submit" :disabled="form.busy || !form.tenant || !form.name">
              {{ form.mode === 'create' ? 'Create' : 'Save' }}
            </Button>
            <Button type="button" variant="ghost" @click="form.open = false">Cancel</Button>
          </div>
        </form>
      </CardContent>
    </Card>

    <Card>
      <CardHeader class="flex-row items-center justify-between gap-4 space-y-0">
        <CardDescription>Per-tenant roles. <strong>Static</strong> roles are defined in code and cannot be renamed or deleted here.</CardDescription>
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
          {{ (error as any)?.data?.detail ?? (error as any)?.data?.data?.detail ?? 'Could not load roles (are you an administrator?).' }}
        </p>
        <Table v-else data-testid="role-table">
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Tenant</TableHead>
              <TableHead>Origin</TableHead>
              <TableHead class="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-if="pending && rows.length === 0">
              <TableCell colspan="4" class="text-muted-foreground">Loading…</TableCell>
            </TableRow>
            <TableRow v-else-if="rows.length === 0">
              <TableCell colspan="4" class="text-muted-foreground">No roles.</TableCell>
            </TableRow>
            <template v-for="row in rows" :key="row.id">
              <TableRow :data-testid="`role-row-${row.name}`">
                <TableCell class="font-medium">{{ row.name }}</TableCell>
                <TableCell class="text-muted-foreground">{{ row.tenantId }}</TableCell>
                <TableCell>
                  <Badge :variant="row.origin === 'dynamic' ? 'default' : 'outline'">{{ row.origin }}</Badge>
                </TableCell>
                <TableCell class="text-right">
                  <div class="flex justify-end gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      :data-testid="`role-edit-${row.name}`"
                      :disabled="row.origin === 'static'"
                      :title="row.origin === 'static' ? 'Defined in code' : undefined"
                      @click="openEdit(row)"
                    >
                      Rename
                    </Button>
                    <Button
                      variant="destructive"
                      size="sm"
                      :data-testid="`role-delete-${row.name}`"
                      :disabled="row.origin === 'static'"
                      :title="row.origin === 'static' ? 'Defined in code' : undefined"
                      @click="confirmDelete = row"
                    >
                      Delete
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
              <TableRow v-if="confirmDelete && confirmDelete.id === row.id" :data-testid="`role-confirm-${row.name}`">
                <TableCell colspan="4">
                  <div class="flex items-center justify-between gap-4 rounded-md bg-muted px-3 py-2 text-sm">
                    <span>Delete role <strong>{{ row.name }}</strong> from tenant <strong>{{ row.tenantId }}</strong>? Members must be unassigned first.</span>
                    <div class="flex gap-2">
                      <Button variant="destructive" size="sm" :data-testid="`role-confirm-delete-${row.name}`" @click="remove(row)">Delete</Button>
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
