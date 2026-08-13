<script setup lang="ts">
import { Pencil, Plus, RefreshCw, Trash2, Users } from '@lucide/vue'
import type { RoleMemberResponse, RoleResponse } from '~~/shared/types/admin'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@/components/ui/table'

const filters = ref({})
const { items, loading, error, refresh } = useAdminList<RoleResponse>('roles', filters)
await refresh()

// --- Create/rename ---
const isEditModalOpen = ref(false)
const editing = ref<RoleResponse | null>(null)
const name = ref('')
const saving = ref(false)
const formError = ref<string | null>(null)

function openCreate() {
  editing.value = null
  name.value = ''
  formError.value = null
  isEditModalOpen.value = true
}

function openEdit(role: RoleResponse) {
  editing.value = role
  name.value = role.name
  formError.value = null
  isEditModalOpen.value = true
}

async function submit() {
  saving.value = true
  formError.value = null
  try {
    if (editing.value) {
      await $huiaAdmin(`roles/${editing.value.id}`, { method: 'PUT', body: { name: name.value } })
    } else {
      await $huiaAdmin('roles', { method: 'POST', body: { name: name.value } })
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

async function remove(role: RoleResponse) {
  deletingId.value = role.id
  try {
    await $huiaAdmin(`roles/${role.id}`, { method: 'DELETE' })
    await refresh()
  } finally {
    deletingId.value = null
  }
}

// --- Members ---
const isMembersModalOpen = ref(false)
const membersTarget = ref<RoleResponse | null>(null)
const members = ref<RoleMemberResponse[]>([])
const membersLoading = ref(false)

async function openMembers(role: RoleResponse) {
  membersTarget.value = role
  isMembersModalOpen.value = true
  membersLoading.value = true
  try {
    members.value = await $huiaAdmin<RoleMemberResponse[]>(`roles/${role.id}/members`)
  } finally {
    membersLoading.value = false
  }
}
</script>

<template>
  <div>
    <div class="flex flex-col gap-4 p-4 md:p-6">
      <div class="flex items-center justify-between gap-4">
        <h1 class="text-2xl font-semibold tracking-tight">
          Roles
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
            New role
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
                  v-for="col in 2"
                  :key="col"
                >
                  <Skeleton class="h-5 w-full" />
                </TableCell>
              </TableRow>
            </template>
            <TableEmpty
              v-else-if="!items.length"
              :colspan="2"
            >
              <Empty>
                <EmptyHeader>
                  <EmptyMedia variant="icon">
                    <RefreshCw />
                  </EmptyMedia>
                  <EmptyTitle>No roles yet</EmptyTitle>
                  <EmptyDescription>Create a role to assign it to users.</EmptyDescription>
                </EmptyHeader>
              </Empty>
            </TableEmpty>
            <TableRow
              v-for="role in items"
              :key="role.id"
            >
              <TableCell class="font-medium">
                {{ role.name }}
              </TableCell>
              <TableCell>
                <div class="flex justify-end gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    title="Members"
                    @click="openMembers(role)"
                  >
                    <Users />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    @click="openEdit(role)"
                  >
                    <Pencil />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    :disabled="deletingId === role.id"
                    @click="remove(role)"
                  >
                    <Trash2 class="text-destructive" />
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </div>

    <Dialog v-model:open="isEditModalOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{{ editing ? 'Rename role' : 'New role' }}</DialogTitle>
        </DialogHeader>

        <form
          class="flex flex-col gap-4"
          @submit.prevent="submit"
        >
          <FieldGroup>
            <Field>
              <FieldLabel>Name</FieldLabel>
              <Input v-model="name" />
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

    <Dialog v-model:open="isMembersModalOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Members — {{ membersTarget?.name ?? '' }}</DialogTitle>
        </DialogHeader>

        <div
          v-if="membersLoading"
          class="flex flex-col gap-2"
        >
          <Skeleton
            v-for="n in 3"
            :key="n"
            class="h-5 w-full"
          />
        </div>
        <ul
          v-else-if="members.length"
          class="flex flex-col gap-1"
        >
          <li
            v-for="member in members"
            :key="member.id"
            class="text-sm"
          >
            {{ member.email }}
          </li>
        </ul>
        <p
          v-else
          class="text-sm text-muted-foreground"
        >
          No members yet.
        </p>
      </DialogContent>
    </Dialog>
  </div>
</template>
