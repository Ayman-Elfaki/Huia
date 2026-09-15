<script setup lang="ts">
import { toast } from 'vue-sonner'

definePageMeta({ middleware: 'auth' })

interface ClientRow {
  id: string
  clientId: string | null
  displayName: string | null
  clientType: string | null
  tenant: string | null
  origin: 'static' | 'dynamic'
}

const CLIENT_KINDS = [
  'ServerSideWebApplication',
  'SinglePageApplication',
  'NativeApplication',
  'Device',
  'MachineToMachine',
] as const

const tenantOptions = useTenantOptions()
const { rows, hasNext, hasPrevious, pending, error, next, prev, refresh }
  = useKeysetList<ClientRow>('admin/clients')

type FormMode = 'create' | 'edit'
const form = reactive({
  open: false,
  mode: 'create' as FormMode,
  id: '',
  tenant: '',
  clientId: '',
  displayName: '',
  kind: 'ServerSideWebApplication' as (typeof CLIENT_KINDS)[number],
  clientSecret: '',
  redirectUris: '',
  postLogoutRedirectUris: '',
  homeUris: '',
  scopes: '',
  requirePkce: false,
  requireConsent: false,
  requirePushedAuthorizationRequests: false,
  busy: false,
  error: '',
})
const confirmDelete = ref<ClientRow | null>(null)

const lines = (value: string) => value.split(/[\n,]/).map(v => v.trim()).filter(Boolean)

function openCreate() {
  Object.assign(form, {
    open: true,
    mode: 'create',
    id: '',
    tenant: tenantOptions.value[0]?.value || '',
    clientId: '',
    displayName: '',
    kind: 'ServerSideWebApplication',
    clientSecret: '',
    redirectUris: '',
    postLogoutRedirectUris: '',
    homeUris: '',
    scopes: '',
    requirePkce: false,
    requireConsent: false,
    requirePushedAuthorizationRequests: false,
    error: '',
  })
}

function openEdit(row: ClientRow) {
  Object.assign(form, {
    open: true,
    mode: 'edit',
    id: row.id,
    tenant: row.tenant ?? '',
    clientId: row.clientId ?? '',
    displayName: row.displayName ?? '',
    kind: 'ServerSideWebApplication',
    clientSecret: '',
    redirectUris: '',
    postLogoutRedirectUris: '',
    homeUris: '',
    scopes: '',
    requirePkce: false,
    requireConsent: false,
    requirePushedAuthorizationRequests: false,
    error: '',
  })
}

function problem(e: any) {
  return e?.data?.detail ?? e?.data?.data?.detail ?? e?.data?.title ?? e?.data?.data?.title ?? 'The request failed.'
}

async function submit() {
  form.error = ''
  form.busy = true
  const body: Record<string, unknown> = {
    tenant: form.tenant,
    clientId: form.clientId,
    displayName: form.displayName || null,
    kind: form.kind,
    clientSecret: form.clientSecret || null,
    redirectUris: lines(form.redirectUris),
    postLogoutRedirectUris: lines(form.postLogoutRedirectUris),
    homeUris: lines(form.homeUris),
    scopes: lines(form.scopes),
    requirePkce: form.requirePkce,
    requireConsent: form.requireConsent,
    requirePushedAuthorizationRequests: form.requirePushedAuthorizationRequests,
  }
  try {
    if (form.mode === 'create') {
      await $huia('admin/clients', { method: 'POST', body })
      toast.success(`Client ${form.clientId} created.`)
    }
    else {
      await $huia(`admin/clients/${encodeURIComponent(form.id)}`, { method: 'PUT', body })
      toast.success(`Client ${form.clientId} updated.`)
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

async function remove(row: ClientRow) {
  try {
    await $huia(`admin/clients/${encodeURIComponent(row.id)}`, { method: 'DELETE' })
    toast.success(`Client ${row.clientId} deleted.`)
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
      <h1 class="text-2xl font-bold tracking-tight">Clients</h1>
      <Button data-testid="client-create" @click="openCreate">New client</Button>
    </div>

    <Card v-if="form.open" data-testid="client-form">
      <CardHeader>
        <CardTitle>{{ form.mode === 'create' ? 'New client' : `Edit ${form.clientId}` }}</CardTitle>
        <CardDescription>
          Static clients are defined in code and cannot be edited here. A PUT replaces the whole definition.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form class="flex flex-col gap-4" @submit.prevent="submit">
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="flex flex-col gap-1.5">
              <Label for="client-tenant">Tenant</Label>
              <Select id="client-tenant" v-model="form.tenant" data-testid="client-tenant" :disabled="form.mode === 'edit'">
                <option value="" disabled>Select a tenant…</option>
                <option v-for="t in tenantOptions" :key="t.value" :value="t.value">{{ t.label }}</option>
              </Select>
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="client-id">Client ID</Label>
              <Input id="client-id" v-model="form.clientId" data-testid="client-id" :disabled="form.mode === 'edit'" />
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="client-kind">Kind</Label>
              <Select id="client-kind" v-model="form.kind" data-testid="client-kind">
                <option v-for="k in CLIENT_KINDS" :key="k" :value="k">{{ k }}</option>
              </Select>
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="client-display">Display name</Label>
              <Input id="client-display" v-model="form.displayName" data-testid="client-display" />
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="client-secret">Client secret</Label>
              <Input id="client-secret" v-model="form.clientSecret" data-testid="client-secret" placeholder="confidential clients only" />
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="client-scopes">Scopes</Label>
              <Input id="client-scopes" v-model="form.scopes" data-testid="client-scopes" placeholder="reports:read, billing:read" />
            </div>
          </div>

          <div class="grid gap-4 sm:grid-cols-3">
            <div class="flex flex-col gap-1.5">
              <Label for="client-redirects">Redirect URIs</Label>
              <Textarea id="client-redirects" v-model="form.redirectUris" data-testid="client-redirects" placeholder="one per line" />
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="client-postlogout">Post-logout URIs</Label>
              <Textarea id="client-postlogout" v-model="form.postLogoutRedirectUris" data-testid="client-postlogout" />
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="client-home">Home URIs</Label>
              <Textarea id="client-home" v-model="form.homeUris" data-testid="client-home" />
            </div>
          </div>

          <div class="flex flex-wrap gap-4 text-sm">
            <label class="flex items-center gap-2">
              <input v-model="form.requirePkce" type="checkbox" data-testid="client-pkce"> Require PKCE
            </label>
            <label class="flex items-center gap-2">
              <input v-model="form.requireConsent" type="checkbox" data-testid="client-consent"> Require consent
            </label>
            <label class="flex items-center gap-2">
              <input v-model="form.requirePushedAuthorizationRequests" type="checkbox" data-testid="client-par"> Require PAR
            </label>
          </div>

          <p v-if="form.error" class="text-sm text-destructive" data-testid="client-form-error">{{ form.error }}</p>
          <div class="flex items-center gap-3">
            <Button type="submit" data-testid="client-submit" :disabled="form.busy || !form.tenant || !form.clientId">
              {{ form.mode === 'create' ? 'Create' : 'Save' }}
            </Button>
            <Button type="button" variant="ghost" @click="form.open = false">Cancel</Button>
          </div>
        </form>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardDescription>
          Registered OAuth / OIDC applications. <strong>Static</strong> clients are defined in code; the admin
          API does not edit them.
        </CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <p v-if="error" class="text-sm text-destructive">
          {{ (error as any)?.data?.detail ?? (error as any)?.data?.data?.detail ?? 'Could not load clients (are you an administrator?).' }}
        </p>
        <Table v-else data-testid="client-table">
          <TableHeader>
            <TableRow>
              <TableHead>Client ID</TableHead>
              <TableHead>Display name</TableHead>
              <TableHead>Type</TableHead>
              <TableHead>Tenant</TableHead>
              <TableHead>Origin</TableHead>
              <TableHead class="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-if="pending && rows.length === 0">
              <TableCell colspan="6" class="text-muted-foreground">Loading…</TableCell>
            </TableRow>
            <TableRow v-else-if="rows.length === 0">
              <TableCell colspan="6" class="text-muted-foreground">No clients.</TableCell>
            </TableRow>
            <template v-for="row in rows" :key="row.id">
              <TableRow :data-testid="`client-${row.clientId}`">
                <TableCell class="font-medium">{{ row.clientId }}</TableCell>
                <TableCell>{{ row.displayName ?? '—' }}</TableCell>
                <TableCell class="text-muted-foreground">{{ row.clientType ?? '—' }}</TableCell>
                <TableCell class="text-muted-foreground">{{ row.tenant ?? '—' }}</TableCell>
                <TableCell>
                  <Badge :variant="row.origin === 'dynamic' ? 'default' : 'outline'">{{ row.origin }}</Badge>
                </TableCell>
                <TableCell class="text-right">
                  <div class="flex justify-end gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      :data-testid="`client-edit-${row.clientId}`"
                      :disabled="row.origin === 'static'"
                      :title="row.origin === 'static' ? 'Defined in code' : undefined"
                      @click="openEdit(row)"
                    >
                      Edit
                    </Button>
                    <Button
                      variant="destructive"
                      size="sm"
                      :data-testid="`client-delete-${row.clientId}`"
                      :disabled="row.origin === 'static'"
                      :title="row.origin === 'static' ? 'Defined in code' : undefined"
                      @click="confirmDelete = row"
                    >
                      Delete
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
              <TableRow v-if="confirmDelete && confirmDelete.id === row.id" :data-testid="`client-confirm-${row.clientId}`">
                <TableCell colspan="6">
                  <div class="flex items-center justify-between gap-4 rounded-md bg-muted px-3 py-2 text-sm">
                    <span>Delete client <strong>{{ row.clientId }}</strong> from tenant <strong>{{ row.tenant }}</strong>?</span>
                    <div class="flex gap-2">
                      <Button variant="destructive" size="sm" :data-testid="`client-confirm-delete-${row.clientId}`" @click="remove(row)">Delete</Button>
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
