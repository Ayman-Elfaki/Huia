<script setup lang="ts">
import { toast } from 'vue-sonner'

definePageMeta({ middleware: 'auth' })

interface ScopeRow {
  id: string | null
  name: string | null
  displayName: string | null
  description: string | null
  tenant: string | null
  origin: 'static' | 'dynamic'
}

const tenantOptions = useTenantOptions()

const tenantFilter = ref('')
const query = computed(() => (tenantFilter.value ? { tenant: tenantFilter.value } : {}))
const { data: scopes, pending, error, refresh } = useHuiaData<ScopeRow[]>('admin/scopes', {
  query,
  watch: [query],
  default: () => [],
})

type FormMode = 'create' | 'edit'
const form = reactive({
  open: false,
  mode: 'create' as FormMode,
  tenant: '',
  name: '',
  displayName: '',
  description: '',
  resources: '',
  busy: false,
  error: '',
})
const confirmDelete = ref<ScopeRow | null>(null)

function openCreate() {
  Object.assign(form, {
    open: true,
    mode: 'create',
    tenant: tenantFilter.value || tenantOptions.value[0]?.value || '',
    name: '',
    displayName: '',
    description: '',
    resources: '',
    error: '',
  })
}

function openEdit(row: ScopeRow) {
  Object.assign(form, {
    open: true,
    mode: 'edit',
    tenant: row.tenant ?? '',
    name: row.name ?? '',
    displayName: row.displayName ?? '',
    description: row.description ?? '',
    resources: '',
    error: '',
  })
}

function problem(e: any) {
  return e?.data?.detail ?? e?.data?.data?.detail ?? e?.data?.title ?? e?.data?.data?.title ?? 'The request failed.'
}

async function submit() {
  form.error = ''
  form.busy = true
  const resources = form.resources.split(',').map(r => r.trim()).filter(Boolean)
  try {
    if (form.mode === 'create') {
      await $huia('admin/scopes', {
        method: 'POST',
        body: {
          tenant: form.tenant,
          name: form.name,
          displayName: form.displayName || null,
          description: form.description || null,
          resources,
        },
      })
      toast.success(`Scope ${form.name} created.`)
    }
    else {
      await $huia(`admin/scopes/${encodeURIComponent(form.name)}`, {
        method: 'PUT',
        body: {
          tenant: form.tenant,
          displayName: form.displayName || null,
          description: form.description || null,
          // Only replace resources when the field was filled in; an empty field leaves them untouched.
          ...(form.resources.trim() ? { resources } : {}),
        },
      })
      toast.success(`Scope ${form.name} updated.`)
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

async function remove(row: ScopeRow) {
  try {
    await $huia(`admin/scopes/${encodeURIComponent(row.name ?? '')}`, {
      method: 'DELETE',
      query: { tenant: row.tenant ?? '' },
    })
    toast.success(`Scope ${row.name} deleted.`)
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
      <h1 class="text-2xl font-bold tracking-tight">Scopes</h1>
      <Button data-testid="scope-create" @click="openCreate">New scope</Button>
    </div>

    <Card v-if="form.open" data-testid="scope-form">
      <CardHeader>
        <CardTitle>{{ form.mode === 'create' ? 'New scope' : `Edit ${form.name}` }}</CardTitle>
        <CardDescription>
          Scopes belong to a single tenant. Code-defined scopes cannot be edited here.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form class="flex flex-col gap-4" @submit.prevent="submit">
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="flex flex-col gap-1.5">
              <Label for="scope-tenant">Tenant</Label>
              <Select id="scope-tenant" v-model="form.tenant" data-testid="scope-tenant" :disabled="form.mode === 'edit'">
                <option value="" disabled>Select a tenant…</option>
                <option v-for="t in tenantOptions" :key="t.value" :value="t.value">{{ t.label }}</option>
              </Select>
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="scope-name">Name</Label>
              <Input id="scope-name" v-model="form.name" data-testid="scope-name" :disabled="form.mode === 'edit'" placeholder="billing:read" />
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="scope-display">Display name</Label>
              <Input id="scope-display" v-model="form.displayName" data-testid="scope-display" />
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="scope-resources">Resources</Label>
              <Input id="scope-resources" v-model="form.resources" data-testid="scope-resources" placeholder="billing-api, reports-api" />
            </div>
          </div>
          <div class="flex flex-col gap-1.5">
            <Label for="scope-description">Description</Label>
            <Textarea id="scope-description" v-model="form.description" data-testid="scope-description" />
          </div>
          <p v-if="form.error" class="text-sm text-destructive" data-testid="scope-form-error">{{ form.error }}</p>
          <div class="flex items-center gap-3">
            <Button type="submit" data-testid="scope-submit" :disabled="form.busy || !form.tenant || !form.name">
              {{ form.mode === 'create' ? 'Create' : 'Save' }}
            </Button>
            <Button type="button" variant="ghost" @click="form.open = false">Cancel</Button>
          </div>
        </form>
      </CardContent>
    </Card>

    <Card>
      <CardHeader class="flex-row items-center justify-between gap-4 space-y-0">
        <CardDescription>Per-tenant OAuth scopes.</CardDescription>
        <Select v-model="tenantFilter" class="w-56" data-testid="tenant-filter">
          <option value="">All tenants</option>
          <option v-for="t in tenantOptions" :key="t.value" :value="t.value">{{ t.label }}</option>
        </Select>
      </CardHeader>
      <CardContent>
        <p v-if="error" class="text-sm text-destructive">
          {{ (error as any)?.data?.detail ?? (error as any)?.data?.data?.detail ?? 'Could not load scopes (are you an administrator?).' }}
        </p>
        <Table v-else data-testid="scope-table">
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Display name</TableHead>
              <TableHead>Tenant</TableHead>
              <TableHead>Origin</TableHead>
              <TableHead class="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-if="pending && scopes.length === 0">
              <TableCell colspan="5" class="text-muted-foreground">Loading…</TableCell>
            </TableRow>
            <TableRow v-else-if="scopes.length === 0">
              <TableCell colspan="5" class="text-muted-foreground">No scopes.</TableCell>
            </TableRow>
            <template v-for="row in scopes" :key="row.id ?? row.name">
              <TableRow :data-testid="`scope-row-${row.name}`">
                <TableCell class="font-medium">{{ row.name }}</TableCell>
                <TableCell>{{ row.displayName ?? '—' }}</TableCell>
                <TableCell class="text-muted-foreground">{{ row.tenant ?? '—' }}</TableCell>
                <TableCell>
                  <Badge :variant="row.origin === 'dynamic' ? 'default' : 'outline'">{{ row.origin }}</Badge>
                </TableCell>
                <TableCell class="text-right">
                  <div class="flex justify-end gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      :data-testid="`scope-edit-${row.name}`"
                      :disabled="row.origin === 'static'"
                      :title="row.origin === 'static' ? 'Defined in code' : undefined"
                      @click="openEdit(row)"
                    >
                      Edit
                    </Button>
                    <Button
                      variant="destructive"
                      size="sm"
                      :data-testid="`scope-delete-${row.name}`"
                      :disabled="row.origin === 'static'"
                      :title="row.origin === 'static' ? 'Defined in code' : undefined"
                      @click="confirmDelete = row"
                    >
                      Delete
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
              <TableRow v-if="confirmDelete && confirmDelete.name === row.name" :data-testid="`scope-confirm-${row.name}`">
                <TableCell colspan="5">
                  <div class="flex items-center justify-between gap-4 rounded-md bg-muted px-3 py-2 text-sm">
                    <span>Delete scope <strong>{{ row.name }}</strong> from tenant <strong>{{ row.tenant }}</strong>?</span>
                    <div class="flex gap-2">
                      <Button variant="destructive" size="sm" :data-testid="`scope-confirm-delete-${row.name}`" @click="remove(row)">
                        Delete
                      </Button>
                      <Button variant="ghost" size="sm" @click="confirmDelete = null">Cancel</Button>
                    </div>
                  </div>
                </TableCell>
              </TableRow>
            </template>
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  </section>
</template>
