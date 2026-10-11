<script setup lang="ts">
import { toast } from 'vue-sonner'

definePageMeta({ middleware: 'auth' })

// Mirrors `TenantDto` / `TenantDetailDto` / `HuiaTenantDocument` from the Huia admin API
// (`src/Huia.OpenId/Endpoints/AdminEndpoints*.cs`, `src/Huia/Tenants/HuiaTenantDocument.cs`).
interface TenantRow {
  tenantId: string
  displayName: string
  passwordEnabled: boolean
  phoneLoginEnabled: boolean
  externalLoginEnabled: boolean
  clientCount: number
  origin: 'static' | 'dynamic'
}

interface AppDoc {
  kind: 'serverSide' | 'spa' | 'native' | 'm2m'
  clientId: string
  clientSecret?: string | null
  displayName?: string | null
  redirectUris: string[]
  [key: string]: unknown
}

interface ScopeDoc {
  name: string
  displayName?: string | null
  description?: string | null
  resources: string[]
}

interface TenantDocument {
  displayName?: string | null
  branding: { accentColor?: string | null, logoUrl?: string | null, [key: string]: unknown }
  emailPassword?: { minimumLength: number, requireConfirmedEmail: boolean, allowSelfServiceRegistration: boolean, [key: string]: unknown } | null
  phone?: { defaultCountry?: string | null, [key: string]: unknown } | null
  passkey?: Record<string, unknown> | null
  roles: string[]
  scopes: ScopeDoc[]
  applications: AppDoc[]
  [key: string]: unknown
}

const { data: tenants, pending, error, refresh } = await useHuiaData<TenantRow[]>('admin/tenants', {
  default: () => [],
})

function blankDocument(): TenantDocument {
  return {
    displayName: '',
    branding: { accentColor: '' },
    emailPassword: { minimumLength: 8, requireConfirmedEmail: false, allowSelfServiceRegistration: true },
    phone: null,
    passkey: null,
    roles: [],
    scopes: [],
    applications: [],
  }
}

const form = reactive({
  open: false,
  mode: 'create' as 'create' | 'edit',
  tenantId: '',
  version: null as number | null,
  busy: false,
  error: '',
})
const doc = ref<TenantDocument>(blankDocument())
const confirmDelete = ref<TenantRow | null>(null)

function problem(e: any) {
  const data = e?.data?.data ?? e?.data
  const errors = data?.errors ? Object.values(data.errors).flat() : []
  return errors.length ? errors.join(' ') : (data?.detail ?? data?.title ?? 'The request failed.')
}

function openCreate() {
  Object.assign(form, { open: true, mode: 'create', tenantId: '', version: null, error: '' })
  doc.value = blankDocument()
}

async function openEdit(row: TenantRow) {
  try {
    const detail = await $huia<{ version: number | null, document: TenantDocument }>(
      `admin/tenants/${encodeURIComponent(row.tenantId)}`,
    )
    // Fill any section the server left out so the form can bind to it.
    doc.value = { ...blankDocument(), ...detail.document, branding: { ...detail.document.branding } }
    Object.assign(form, { open: true, mode: 'edit', tenantId: row.tenantId, version: detail.version, error: '' })
  }
  catch (e: any) {
    toast.error(problem(e))
  }
}

// --- bindings for the optional sign-in sections and list-valued fields --------------------------

const passwordEnabled = computed({
  get: () => !!doc.value.emailPassword,
  set: (on: boolean) => {
    doc.value.emailPassword = on
      ? (doc.value.emailPassword ?? { minimumLength: 8, requireConfirmedEmail: false, allowSelfServiceRegistration: true })
      : null
  },
})

const phoneEnabled = computed({
  get: () => !!doc.value.phone,
  set: (on: boolean) => {
    doc.value.phone = on ? (doc.value.phone ?? { defaultCountry: '' }) : null
  },
})

const passkeyEnabled = computed({
  get: () => !!doc.value.passkey,
  set: (on: boolean) => {
    doc.value.passkey = on ? (doc.value.passkey ?? {}) : null
  },
})

const rolesText = computed({
  get: () => doc.value.roles.join(', '),
  set: (v: string) => {
    doc.value.roles = v.split(',').map(r => r.trim()).filter(Boolean)
  },
})

function listText(values: string[]) {
  return values.join(', ')
}

function parseList(text: string) {
  return text.split(',').map(v => v.trim()).filter(Boolean)
}

function addApplication() {
  doc.value.applications.push({ kind: 'm2m', clientId: '', clientSecret: '', redirectUris: [] })
}

function removeApplication(index: number) {
  doc.value.applications.splice(index, 1)
}

function addScope() {
  doc.value.scopes.push({ name: '', displayName: '', description: '', resources: [] })
}

function removeScope(index: number) {
  doc.value.scopes.splice(index, 1)
}

const isInteractive = (kind: string) => kind !== 'm2m'
const needsSecret = (kind: string) => kind === 'serverSide' || kind === 'm2m'

function cleanedDocument(): TenantDocument {
  const copy: TenantDocument = JSON.parse(JSON.stringify(doc.value))
  copy.displayName = copy.displayName || null
  copy.branding.accentColor = copy.branding.accentColor || null
  if (copy.phone) {
    copy.phone.defaultCountry = copy.phone.defaultCountry || null
  }
  // An empty secret means "keep the stored one" on update; the server treats null that way.
  for (const app of copy.applications) {
    app.clientSecret = app.clientSecret || null
    app.displayName = app.displayName || null
    if (!isInteractive(app.kind)) {
      app.redirectUris = []
    }
  }
  return copy
}

async function submit() {
  form.error = ''
  form.busy = true
  try {
    if (form.mode === 'create') {
      await $huia('admin/tenants', {
        method: 'POST',
        body: { tenantId: form.tenantId, document: cleanedDocument() },
      })
      toast.success(`Tenant ${form.tenantId} created.`)
    }
    else {
      await $huia(`admin/tenants/${encodeURIComponent(form.tenantId)}`, {
        method: 'PUT',
        body: { document: cleanedDocument() },
      })
      toast.success(`Tenant ${form.tenantId} updated.`)
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

async function remove(row: TenantRow) {
  try {
    await $huia(`admin/tenants/${encodeURIComponent(row.tenantId)}`, { method: 'DELETE' })
    toast.success(`Tenant ${row.tenantId} deleted.`)
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
      <h1 class="text-2xl font-bold tracking-tight">Tenants</h1>
      <Button data-testid="tenant-create" @click="openCreate">New tenant</Button>
    </div>

    <Card v-if="form.open" data-testid="tenant-form">
      <CardHeader>
        <CardTitle>{{ form.mode === 'create' ? 'New tenant' : `Edit ${form.tenantId}` }}</CardTitle>
        <CardDescription>
          A tenant created here is live immediately: it gets its own path, signing key, roles, scopes and clients.
          Tenants declared in code cannot be edited. Secrets are never shown; leave a secret empty to keep the stored one.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form class="flex flex-col gap-6" @submit.prevent="submit">
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="flex flex-col gap-1.5">
              <Label for="tenant-id">Tenant id</Label>
              <Input
                id="tenant-id"
                v-model="form.tenantId"
                data-testid="tenant-id"
                :disabled="form.mode === 'edit'"
                placeholder="acme"
              />
              <p class="text-xs text-muted-foreground">Lower-case letters, digits and hyphens. It is the URL path segment.</p>
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="tenant-display">Display name</Label>
              <Input id="tenant-display" v-model="doc.displayName" data-testid="tenant-display" placeholder="Acme Inc." />
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="tenant-accent">Accent colour</Label>
              <Input id="tenant-accent" v-model="doc.branding.accentColor" data-testid="tenant-accent" placeholder="#0d6efd" />
            </div>
            <div class="flex flex-col gap-1.5">
              <Label for="tenant-roles">Roles</Label>
              <Input id="tenant-roles" v-model="rolesText" data-testid="tenant-roles" placeholder="admin, support" />
            </div>
          </div>

          <fieldset class="flex flex-col gap-3 rounded-md border p-4">
            <legend class="px-1 text-sm font-medium">Sign-in methods</legend>
            <label class="flex items-center gap-2 text-sm">
              <input v-model="passwordEnabled" type="checkbox" data-testid="tenant-password">
              Email and password
            </label>
            <div v-if="doc.emailPassword" class="ml-6 grid gap-3 sm:grid-cols-3">
              <div class="flex flex-col gap-1.5">
                <Label for="tenant-minlen">Minimum length</Label>
                <Input id="tenant-minlen" v-model.number="doc.emailPassword.minimumLength" type="number" min="6" data-testid="tenant-minlen" />
              </div>
              <label class="flex items-center gap-2 self-end text-sm">
                <input v-model="doc.emailPassword.requireConfirmedEmail" type="checkbox" data-testid="tenant-confirmed-email">
                Require confirmed email
              </label>
              <label class="flex items-center gap-2 self-end text-sm">
                <input v-model="doc.emailPassword.allowSelfServiceRegistration" type="checkbox" data-testid="tenant-self-service">
                Allow self-service registration
              </label>
            </div>

            <label class="flex items-center gap-2 text-sm">
              <input v-model="phoneEnabled" type="checkbox" data-testid="tenant-phone">
              Phone (SMS one-time code)
            </label>
            <div v-if="doc.phone" class="ml-6 flex flex-col gap-1.5 sm:w-1/3">
              <Label for="tenant-country">Default country (ISO 3166-1 alpha-2)</Label>
              <Input id="tenant-country" v-model="doc.phone.defaultCountry" data-testid="tenant-country" placeholder="SA" />
            </div>

            <label class="flex items-center gap-2 text-sm">
              <input v-model="passkeyEnabled" type="checkbox" data-testid="tenant-passkey">
              Passkeys
            </label>
            <p class="text-xs text-muted-foreground">External-provider login (Google, GitHub, …) is configured in code, not here.</p>
          </fieldset>

          <fieldset class="flex flex-col gap-3 rounded-md border p-4">
            <legend class="px-1 text-sm font-medium">Scopes</legend>
            <div
              v-for="(scope, i) in doc.scopes"
              :key="`scope-${i}`"
              class="grid items-end gap-3 sm:grid-cols-[1fr_1fr_auto]"
            >
              <div class="flex flex-col gap-1.5">
                <Label :for="`scope-name-${i}`">Name</Label>
                <Input :id="`scope-name-${i}`" v-model="scope.name" :data-testid="`tenant-scope-name-${i}`" placeholder="api:read" />
              </div>
              <div class="flex flex-col gap-1.5">
                <Label :for="`scope-display-${i}`">Display name</Label>
                <Input :id="`scope-display-${i}`" v-model="scope.displayName" />
              </div>
              <Button type="button" variant="ghost" size="sm" @click="removeScope(i)">Remove</Button>
            </div>
            <div>
              <Button type="button" variant="outline" size="sm" data-testid="tenant-add-scope" @click="addScope">Add scope</Button>
            </div>
          </fieldset>

          <fieldset class="flex flex-col gap-3 rounded-md border p-4">
            <legend class="px-1 text-sm font-medium">OAuth clients</legend>
            <div
              v-for="(app, i) in doc.applications"
              :key="`app-${i}`"
              class="flex flex-col gap-3 rounded-md bg-muted/40 p-3"
            >
              <div class="grid gap-3 sm:grid-cols-3">
                <div class="flex flex-col gap-1.5">
                  <Label :for="`app-kind-${i}`">Kind</Label>
                  <Select :id="`app-kind-${i}`" v-model="app.kind" :data-testid="`tenant-app-kind-${i}`">
                    <option value="serverSide">Server-side web app</option>
                    <option value="spa">Single-page app</option>
                    <option value="native">Native app</option>
                    <option value="m2m">Machine to machine</option>
                  </Select>
                </div>
                <div class="flex flex-col gap-1.5">
                  <Label :for="`app-id-${i}`">Client id</Label>
                  <Input :id="`app-id-${i}`" v-model="app.clientId" :data-testid="`tenant-app-id-${i}`" />
                </div>
                <div v-if="needsSecret(app.kind)" class="flex flex-col gap-1.5">
                  <Label :for="`app-secret-${i}`">Client secret</Label>
                  <Input
                    :id="`app-secret-${i}`"
                    v-model="app.clientSecret"
                    type="password"
                    autocomplete="new-password"
                    :data-testid="`tenant-app-secret-${i}`"
                    :placeholder="form.mode === 'edit' ? 'unchanged' : ''"
                  />
                </div>
              </div>
              <div v-if="isInteractive(app.kind)" class="flex flex-col gap-1.5">
                <Label :for="`app-redirects-${i}`">Redirect URIs (comma separated)</Label>
                <Input
                  :id="`app-redirects-${i}`"
                  :model-value="listText(app.redirectUris)"
                  :data-testid="`tenant-app-redirects-${i}`"
                  placeholder="https://app.example.com/callback"
                  @update:model-value="(v: string | number) => (app.redirectUris = parseList(String(v)))"
                />
              </div>
              <div>
                <Button type="button" variant="ghost" size="sm" @click="removeApplication(i)">Remove client</Button>
              </div>
            </div>
            <div>
              <Button type="button" variant="outline" size="sm" data-testid="tenant-add-app" @click="addApplication">
                Add client
              </Button>
            </div>
          </fieldset>

          <p v-if="form.error" class="text-sm text-destructive" data-testid="tenant-form-error">{{ form.error }}</p>
          <div class="flex items-center gap-3">
            <Button type="submit" data-testid="tenant-submit" :disabled="form.busy || !form.tenantId">
              {{ form.mode === 'create' ? 'Create' : 'Save' }}
            </Button>
            <Button type="button" variant="ghost" @click="form.open = false">Cancel</Button>
          </div>
        </form>
      </CardContent>
    </Card>

    <Card>
      <CardContent class="pt-6">
        <p v-if="pending" class="text-sm text-muted-foreground">Loading…</p>
        <p v-else-if="error" class="text-sm text-destructive">
          {{ (error as any)?.data?.detail ?? (error as any)?.data?.data?.detail ?? 'Could not load tenants (are you an administrator?).' }}
        </p>
        <Table v-else data-testid="tenant-table">
          <TableHeader>
            <TableRow>
              <TableHead>Tenant</TableHead>
              <TableHead>Password</TableHead>
              <TableHead>Phone</TableHead>
              <TableHead>External</TableHead>
              <TableHead>Clients</TableHead>
              <TableHead>Origin</TableHead>
              <TableHead class="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <template v-for="row in tenants" :key="row.tenantId">
              <TableRow :data-testid="`tenant-${row.tenantId}`">
                <TableCell>
                  <span class="font-medium">{{ row.displayName }}</span>
                  <span class="ml-2 text-muted-foreground">{{ row.tenantId }}</span>
                </TableCell>
                <TableCell>{{ row.passwordEnabled ? 'yes' : '—' }}</TableCell>
                <TableCell>{{ row.phoneLoginEnabled ? 'yes' : '—' }}</TableCell>
                <TableCell>{{ row.externalLoginEnabled ? 'yes' : '—' }}</TableCell>
                <TableCell>{{ row.clientCount }}</TableCell>
                <TableCell>
                  <Badge :variant="row.origin === 'dynamic' ? 'default' : 'outline'">{{ row.origin }}</Badge>
                </TableCell>
                <TableCell class="text-right">
                  <div class="flex justify-end gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      :data-testid="`tenant-edit-${row.tenantId}`"
                      :disabled="row.origin === 'static'"
                      :title="row.origin === 'static' ? 'Defined in code' : undefined"
                      @click="openEdit(row)"
                    >
                      Edit
                    </Button>
                    <Button
                      variant="destructive"
                      size="sm"
                      :data-testid="`tenant-delete-${row.tenantId}`"
                      :disabled="row.origin === 'static'"
                      :title="row.origin === 'static' ? 'Defined in code' : undefined"
                      @click="confirmDelete = row"
                    >
                      Delete
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
              <TableRow v-if="confirmDelete && confirmDelete.tenantId === row.tenantId" :data-testid="`tenant-confirm-${row.tenantId}`">
                <TableCell colspan="7">
                  <div class="flex items-center justify-between gap-4 rounded-md bg-muted px-3 py-2 text-sm">
                    <span>
                      Delete tenant <strong>{{ row.tenantId }}</strong>? It stops resolving immediately; its users and
                      data are not removed.
                    </span>
                    <div class="flex gap-2">
                      <Button
                        variant="destructive"
                        size="sm"
                        :data-testid="`tenant-confirm-delete-${row.tenantId}`"
                        @click="remove(row)"
                      >
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
