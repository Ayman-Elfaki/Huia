<script setup lang="ts">
import { toast } from 'vue-sonner'

definePageMeta({ middleware: 'auth' })

interface KeyRow {
  id: string
  tenantId: string
  keyId: string
  algorithm: string
  status: string
  createdAt: string
}

const tenantOptions = useTenantOptions()
const { rows, hasNext, hasPrevious, pending, error, tenant, setTenant, next, prev, refresh }
  = useKeysetList<KeyRow>('admin/keys')

const statusVariant: Record<string, 'default' | 'secondary' | 'outline' | 'destructive'> = {
  Active: 'default',
  Pending: 'secondary',
  Rotated: 'outline',
  Retired: 'destructive',
}

const form = reactive({ open: false, tenant: '', activate: false, busy: false, error: '' })
const confirmRevoke = ref<KeyRow | null>(null)

function openCreate() {
  Object.assign(form, {
    open: true,
    tenant: tenant.value || tenantOptions.value[0]?.value || '',
    activate: false,
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
    await $huia('admin/keys', { method: 'POST', body: { tenant: form.tenant, activate: form.activate } })
    toast.success(form.activate ? 'Key created and activated.' : 'Pending key created.')
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

async function revoke(row: KeyRow) {
  try {
    await $huia(`admin/keys/${encodeURIComponent(row.id)}/revoke`, { method: 'POST' })
    toast.success('Key revoked.')
    confirmRevoke.value = null
    await refresh()
  }
  catch (e: any) {
    toast.error(problem(e))
    confirmRevoke.value = null
  }
}
</script>

<template>
  <section class="flex flex-col gap-6">
    <div class="flex items-center justify-between gap-4">
      <h1 class="text-2xl font-bold tracking-tight">Signing keys</h1>
      <Button data-testid="key-create" @click="openCreate">New key</Button>
    </div>

    <Card v-if="form.open" data-testid="key-form">
      <CardHeader>
        <CardTitle>New signing key</CardTitle>
        <CardDescription>
          A pending key is published in the JWKS but does not sign yet. Activating one demotes the tenant's
          current active key to <em>rotated</em>.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form class="flex flex-col gap-4" @submit.prevent="submit">
          <div class="flex flex-col gap-1.5 sm:max-w-xs">
            <Label for="key-tenant">Tenant</Label>
            <Select id="key-tenant" v-model="form.tenant" data-testid="key-tenant">
              <option value="" disabled>Select a tenant…</option>
              <option v-for="t in tenantOptions" :key="t.value" :value="t.value">{{ t.label }}</option>
            </Select>
          </div>
          <label class="flex items-center gap-2 text-sm">
            <input v-model="form.activate" type="checkbox" data-testid="key-activate">
            Activate immediately
          </label>
          <p v-if="form.error" class="text-sm text-destructive" data-testid="key-form-error">{{ form.error }}</p>
          <div class="flex items-center gap-3">
            <Button type="submit" data-testid="key-submit" :disabled="form.busy || !form.tenant">Create</Button>
            <Button type="button" variant="ghost" @click="form.open = false">Cancel</Button>
          </div>
        </form>
      </CardContent>
    </Card>

    <Card>
      <CardHeader class="flex-row items-center justify-between gap-4 space-y-0">
        <CardDescription>Per-tenant token signing keys and where they are in the rotation lifecycle.</CardDescription>
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
          {{ (error as any)?.data?.detail ?? (error as any)?.data?.data?.detail ?? 'Could not load keys (are you an administrator?).' }}
        </p>
        <Table v-else data-testid="key-table">
          <TableHeader>
            <TableRow>
              <TableHead>Key ID</TableHead>
              <TableHead>Algorithm</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Tenant</TableHead>
              <TableHead>Created</TableHead>
              <TableHead class="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-if="pending && rows.length === 0">
              <TableCell colspan="6" class="text-muted-foreground">Loading…</TableCell>
            </TableRow>
            <TableRow v-else-if="rows.length === 0">
              <TableCell colspan="6" class="text-muted-foreground">No keys.</TableCell>
            </TableRow>
            <template v-for="row in rows" :key="row.id">
              <TableRow :data-testid="`key-${row.keyId}`">
                <TableCell class="font-mono text-xs">{{ row.keyId }}</TableCell>
                <TableCell>{{ row.algorithm }}</TableCell>
                <TableCell>
                  <Badge :variant="statusVariant[row.status] ?? 'outline'">{{ row.status }}</Badge>
                </TableCell>
                <TableCell class="text-muted-foreground">{{ row.tenantId }}</TableCell>
                <TableCell class="text-muted-foreground">{{ new Date(row.createdAt).toLocaleString() }}</TableCell>
                <TableCell class="text-right">
                  <Button
                    variant="destructive"
                    size="sm"
                    :data-testid="`key-revoke-${row.keyId}`"
                    :disabled="row.status === 'Retired'"
                    @click="confirmRevoke = row"
                  >
                    Revoke
                  </Button>
                </TableCell>
              </TableRow>
              <TableRow v-if="confirmRevoke && confirmRevoke.id === row.id" :data-testid="`key-confirm-${row.keyId}`">
                <TableCell colspan="6">
                  <div class="flex items-center justify-between gap-4 rounded-md bg-muted px-3 py-2 text-sm">
                    <span>Revoke key <strong>{{ row.keyId }}</strong> for tenant <strong>{{ row.tenantId }}</strong>? It stops signing and validating immediately.</span>
                    <div class="flex gap-2">
                      <Button variant="destructive" size="sm" :data-testid="`key-confirm-revoke-${row.keyId}`" @click="revoke(row)">Revoke</Button>
                      <Button variant="ghost" size="sm" @click="confirmRevoke = null">Cancel</Button>
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
