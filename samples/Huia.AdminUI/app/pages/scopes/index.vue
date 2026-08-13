<script setup lang="ts">
import { Pencil, Plus, RefreshCw, Trash2 } from '@lucide/vue'
import type { ScopeRequest, ScopeResponse } from '~~/shared/types/admin'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'

const filters = ref({})
const { items, hasPrevious, nextCursor, loading, error, refresh, goNext, goPrevious }
  = useAdminList<ScopeResponse>('scopes', filters)
await refresh()

const isModalOpen = ref(false)
const editing = ref<ScopeResponse | null>(null)
const saving = ref(false)
const formError = ref<string | null>(null)

const form = reactive({
  name: '',
  displayName: '',
  description: '',
  resources: ''
})

function csvToArray(value: string): string[] {
  return value.split(',').map(v => v.trim()).filter(Boolean)
}

function openCreate() {
  editing.value = null
  form.name = ''
  form.displayName = ''
  form.description = ''
  form.resources = ''
  formError.value = null
  isModalOpen.value = true
}

function openEdit(scope: ScopeResponse) {
  editing.value = scope
  form.name = scope.name
  form.displayName = scope.displayName ?? ''
  form.description = scope.description ?? ''
  form.resources = scope.resources.join(', ')
  formError.value = null
  isModalOpen.value = true
}

async function submit() {
  saving.value = true
  formError.value = null
  const body: ScopeRequest = {
    name: form.name,
    displayName: form.displayName || null,
    description: form.description || null,
    resources: csvToArray(form.resources)
  }
  try {
    if (editing.value) {
      await $huiaAdmin(`scopes/${editing.value.id}`, { method: 'PUT', body })
    } else {
      await $huiaAdmin('scopes', { method: 'POST', body })
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

async function remove(scope: ScopeResponse) {
  deletingId.value = scope.id
  try {
    await $huiaAdmin(`scopes/${scope.id}`, { method: 'DELETE' })
    await refresh()
  } finally {
    deletingId.value = null
  }
}
</script>

<template>
  <div>
    <div class="flex flex-col gap-4 p-4 md:p-6">
      <div class="flex items-center justify-between gap-4">
        <h1 class="text-2xl font-semibold tracking-tight">
          Scopes
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
            New scope
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
              <TableHead>Name</TableHead>
              <TableHead>Display name</TableHead>
              <TableHead>Resources</TableHead>
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
                  v-for="col in 4"
                  :key="col"
                >
                  <Skeleton class="h-5 w-full" />
                </TableCell>
              </TableRow>
            </template>
            <TableEmpty
              v-else-if="!items.length"
              :colspan="4"
            >
              <Empty>
                <EmptyHeader>
                  <EmptyMedia variant="icon">
                    <RefreshCw />
                  </EmptyMedia>
                  <EmptyTitle>No scopes yet</EmptyTitle>
                  <EmptyDescription>Define a scope to gate access to a resource.</EmptyDescription>
                </EmptyHeader>
              </Empty>
            </TableEmpty>
            <TableRow
              v-for="scope in items"
              :key="scope.id"
            >
              <TableCell class="font-medium">
                {{ scope.name }}
              </TableCell>
              <TableCell>{{ scope.displayName || '—' }}</TableCell>
              <TableCell class="text-sm text-muted-foreground">
                {{ scope.resources.join(', ') || '—' }}
              </TableCell>
              <TableCell>
                <div class="flex justify-end gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    @click="openEdit(scope)"
                  >
                    <Pencil />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    :disabled="deletingId === scope.id"
                    @click="remove(scope)"
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
          <DialogTitle>{{ editing ? 'Edit scope' : 'New scope' }}</DialogTitle>
        </DialogHeader>

        <form
          class="flex flex-col gap-4"
          @submit.prevent="submit"
        >
          <FieldGroup>
            <Field>
              <FieldLabel>Name</FieldLabel>
              <Input
                v-model="form.name"
                :disabled="!!editing"
              />
            </Field>
            <Field>
              <FieldLabel>Display name</FieldLabel>
              <Input v-model="form.displayName" />
            </Field>
            <Field>
              <FieldLabel>Description</FieldLabel>
              <Textarea
                v-model="form.description"
                :rows="2"
              />
            </Field>
            <Field>
              <FieldLabel>Resources (comma-separated)</FieldLabel>
              <Input
                v-model="form.resources"
                placeholder="todo-api"
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
