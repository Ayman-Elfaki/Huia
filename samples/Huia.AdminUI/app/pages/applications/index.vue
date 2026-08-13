<script setup lang="ts">
import { Pencil, Plus, RefreshCw, Trash2 } from '@lucide/vue'
import type { ApplicationKind, ApplicationRequest, ApplicationResponse } from '~~/shared/types/admin'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectGroup, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'

const filters = ref({})
const { items, hasPrevious, nextCursor, loading, error, refresh, goNext, goPrevious }
  = useAdminList<ApplicationResponse>('applications', filters)
await refresh()

const kinds: { label: string, value: ApplicationKind }[] = [
  { label: 'Single-page application', value: 'spa' },
  { label: 'Native', value: 'native' },
  { label: 'Server-side web', value: 'web' },
  { label: 'Machine-to-machine', value: 'm2m' },
  { label: 'Device', value: 'device' }
]

const isModalOpen = ref(false)
const editing = ref<ApplicationResponse | null>(null)
const saving = ref(false)
const formError = ref<string | null>(null)

const form = reactive({
  kind: 'spa' as ApplicationKind,
  clientId: '',
  displayName: '',
  clientSecret: '',
  requiresPkce: true,
  requireConsent: false,
  redirectUris: '',
  postLogoutRedirectUris: '',
  scopes: ''
})

function resetForm() {
  form.kind = 'spa'
  form.clientId = ''
  form.displayName = ''
  form.clientSecret = ''
  form.requiresPkce = true
  form.requireConsent = false
  form.redirectUris = ''
  form.postLogoutRedirectUris = ''
  form.scopes = ''
  formError.value = null
}

function openCreate() {
  editing.value = null
  resetForm()
  isModalOpen.value = true
}

function openEdit(app: ApplicationResponse) {
  editing.value = app
  form.kind = app.kind as ApplicationKind
  form.clientId = app.clientId
  form.displayName = app.displayName ?? ''
  form.clientSecret = ''
  form.requiresPkce = app.requirements.includes('ft:pkce')
  form.requireConsent = app.consentType === 'explicit'
  form.redirectUris = app.redirectUris.join('\n')
  form.postLogoutRedirectUris = app.postLogoutRedirectUris.join('\n')
  form.scopes = app.permissions
    .filter(p => p.startsWith('scp:'))
    .map(p => p.slice(4))
    .join(', ')
  formError.value = null
  isModalOpen.value = true
}

function linesToArray(value: string): string[] {
  return value.split('\n').map(v => v.trim()).filter(Boolean)
}

function csvToArray(value: string): string[] {
  return value.split(',').map(v => v.trim()).filter(Boolean)
}

async function submit() {
  saving.value = true
  formError.value = null
  const body: ApplicationRequest = {
    kind: form.kind,
    clientId: form.clientId,
    displayName: form.displayName || null,
    clientSecret: form.clientSecret || null,
    requiresPkce: form.requiresPkce,
    requireConsent: form.requireConsent,
    redirectUris: linesToArray(form.redirectUris),
    postLogoutRedirectUris: linesToArray(form.postLogoutRedirectUris),
    scopes: csvToArray(form.scopes)
  }
  try {
    if (editing.value) {
      await $huiaAdmin(`applications/${editing.value.id}`, { method: 'PUT', body })
    } else {
      await $huiaAdmin('applications', { method: 'POST', body })
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

async function remove(app: ApplicationResponse) {
  deletingId.value = app.id
  try {
    await $huiaAdmin(`applications/${app.id}`, { method: 'DELETE' })
    await refresh()
  } finally {
    deletingId.value = null
  }
}

const needsSecret = computed(() => form.kind === 'web' || form.kind === 'm2m')
</script>

<template>
  <div>
    <div class="flex flex-col gap-4 p-4 md:p-6">
      <div class="flex items-center justify-between gap-4">
        <h1 class="text-2xl font-semibold tracking-tight">
          Applications
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
            New application
          </Button>
        </div>
      </div>

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
              <TableHead>Client ID</TableHead>
              <TableHead>Display name</TableHead>
              <TableHead>Kind</TableHead>
              <TableHead>Redirect URIs</TableHead>
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
                  v-for="col in 5"
                  :key="col"
                >
                  <Skeleton class="h-5 w-full" />
                </TableCell>
              </TableRow>
            </template>
            <TableEmpty
              v-else-if="!items.length"
              :colspan="5"
            >
              <Empty>
                <EmptyHeader>
                  <EmptyMedia variant="icon">
                    <RefreshCw />
                  </EmptyMedia>
                  <EmptyTitle>No applications yet</EmptyTitle>
                  <EmptyDescription>Register an OAuth/OIDC client to get started.</EmptyDescription>
                </EmptyHeader>
              </Empty>
            </TableEmpty>
            <TableRow
              v-for="app in items"
              :key="app.id"
            >
              <TableCell class="font-medium">
                {{ app.clientId }}
              </TableCell>
              <TableCell>{{ app.displayName || '—' }}</TableCell>
              <TableCell>
                <Badge variant="secondary">
                  {{ app.kind }}
                </Badge>
              </TableCell>
              <TableCell class="text-sm text-muted-foreground">
                {{ app.redirectUris.join(', ') || '—' }}
              </TableCell>
              <TableCell>
                <div class="flex justify-end gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    @click="openEdit(app)"
                  >
                    <Pencil />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    :disabled="deletingId === app.id"
                    @click="remove(app)"
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
        :has-next="!!nextCursor"
        :loading="loading"
        @previous="goPrevious"
        @next="goNext"
      />
    </div>

    <Dialog v-model:open="isModalOpen">
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{{ editing ? 'Edit application' : 'New application' }}</DialogTitle>
        </DialogHeader>

        <form
          class="flex flex-col gap-4"
          @submit.prevent="submit"
        >
          <FieldGroup>
            <Field>
              <FieldLabel>Kind</FieldLabel>
              <Select
                v-model="form.kind"
                :disabled="!!editing"
              >
                <SelectTrigger class="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem
                      v-for="kind in kinds"
                      :key="kind.value"
                      :value="kind.value"
                    >
                      {{ kind.label }}
                    </SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel>Client ID</FieldLabel>
              <Input
                v-model="form.clientId"
                :disabled="!!editing"
              />
            </Field>
            <Field>
              <FieldLabel>Display name</FieldLabel>
              <Input v-model="form.displayName" />
            </Field>
            <Field v-if="needsSecret">
              <FieldLabel>{{ editing ? 'Client secret (leave blank to keep current)' : 'Client secret' }}</FieldLabel>
              <Input
                v-model="form.clientSecret"
                type="password"
              />
            </Field>
            <div class="flex gap-6">
              <label class="flex items-center gap-2 text-sm">
                <Checkbox v-model="form.requiresPkce" />
                Requires PKCE
              </label>
              <label class="flex items-center gap-2 text-sm">
                <Checkbox v-model="form.requireConsent" />
                Requires consent
              </label>
            </div>
            <Field>
              <FieldLabel>Redirect URIs (one per line)</FieldLabel>
              <Textarea
                v-model="form.redirectUris"
                :rows="3"
              />
            </Field>
            <Field>
              <FieldLabel>Post-logout redirect URIs (one per line)</FieldLabel>
              <Textarea
                v-model="form.postLogoutRedirectUris"
                :rows="2"
              />
            </Field>
            <Field>
              <FieldLabel>Allowed scopes (comma-separated)</FieldLabel>
              <Input
                v-model="form.scopes"
                placeholder="profile, email, todos"
              />
            </Field>
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
              @click="isModalOpen = false"
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
  </div>
</template>
