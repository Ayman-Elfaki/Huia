<script setup lang="ts">
import { Ban, RefreshCw, Search } from '@lucide/vue'
import type { AuthorizationResponse } from '~~/shared/types/admin'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { InputGroup, InputGroupAddon, InputGroupInput } from '@/components/ui/input-group'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@/components/ui/table'

const subject = ref('')
const clientId = ref('')
const filters = computed(() => ({
  subject: subject.value || undefined,
  clientId: clientId.value || undefined
}))
const { items, hasPrevious, nextCursor, loading, error, refresh, goNext, goPrevious, reset }
  = useAdminList<AuthorizationResponse>('authorizations', filters)
await refresh()
watch([subject, clientId], reset)

const revoking = ref<string | null>(null)

async function revoke(authorization: AuthorizationResponse) {
  revoking.value = authorization.id
  try {
    await $huiaAdmin(`authorizations/${authorization.id}/revoke`, { method: 'POST' })
    await refresh()
  } finally {
    revoking.value = null
  }
}

function formatDate(value: string | null) {
  return value ? new Date(value).toLocaleString() : '—'
}
</script>

<template>
  <div class="flex flex-col gap-4 p-4 md:p-6">
    <div class="flex items-center justify-between gap-4">
      <h1 class="text-2xl font-semibold tracking-tight">
        Authorizations
      </h1>
      <Button
        variant="outline"
        size="icon"
        :disabled="loading"
        @click="refresh"
      >
        <RefreshCw :class="{ 'animate-spin': loading }" />
      </Button>
    </div>

    <div class="flex flex-wrap gap-3">
      <InputGroup class="max-w-xs">
        <InputGroupAddon>
          <Search />
        </InputGroupAddon>
        <InputGroupInput
          v-model="subject"
          placeholder="Filter by subject (user id)…"
        />
      </InputGroup>
      <InputGroup class="max-w-xs">
        <InputGroupAddon>
          <Search />
        </InputGroupAddon>
        <InputGroupInput
          v-model="clientId"
          placeholder="Filter by client id…"
        />
      </InputGroup>
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
            <TableHead>Subject</TableHead>
            <TableHead>Client ID</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Scopes</TableHead>
            <TableHead>Created</TableHead>
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
                v-for="col in 6"
                :key="col"
              >
                <Skeleton class="h-5 w-full" />
              </TableCell>
            </TableRow>
          </template>
          <TableEmpty
            v-else-if="!items.length"
            :colspan="6"
          >
            <Empty>
              <EmptyHeader>
                <EmptyMedia variant="icon">
                  <RefreshCw />
                </EmptyMedia>
                <EmptyTitle>No authorizations found</EmptyTitle>
                <EmptyDescription>Try a different subject or client id filter.</EmptyDescription>
              </EmptyHeader>
            </Empty>
          </TableEmpty>
          <TableRow
            v-for="authorization in items"
            :key="authorization.id"
          >
            <TableCell class="font-medium">
              {{ authorization.subject }}
            </TableCell>
            <TableCell>{{ authorization.applicationClientId }}</TableCell>
            <TableCell>
              <Badge :variant="authorization.status === 'valid' ? 'default' : 'secondary'">
                {{ authorization.status || '—' }}
              </Badge>
            </TableCell>
            <TableCell class="text-sm text-muted-foreground">
              {{ authorization.scopes.join(', ') || '—' }}
            </TableCell>
            <TableCell>{{ formatDate(authorization.creationDate) }}</TableCell>
            <TableCell>
              <div class="flex justify-end">
                <Button
                  variant="ghost"
                  size="sm"
                  :disabled="authorization.status !== 'valid' || revoking === authorization.id"
                  @click="revoke(authorization)"
                >
                  <Ban
                    data-icon="inline-start"
                    class="text-destructive"
                  />
                  Revoke
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
</template>
