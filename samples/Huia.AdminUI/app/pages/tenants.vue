<script setup lang="ts">
definePageMeta({ middleware: 'auth' })

interface TenantRow {
  tenantId: string
  displayName: string
  passwordEnabled: boolean
  phoneLoginEnabled: boolean
  externalLoginEnabled: boolean
  clientCount: number
}

const { data: tenants, pending, error } = await useHuiaData<TenantRow[]>('admin/tenants', { default: () => [] })
</script>

<template>
  <section class="flex flex-col gap-6">
    <h1 class="text-2xl font-bold tracking-tight">Tenants</h1>

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
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="row in tenants" :key="row.tenantId" :data-testid="`tenant-${row.tenantId}`">
              <TableCell>
                <span class="font-medium">{{ row.displayName }}</span>
                <span class="ml-2 text-muted-foreground">{{ row.tenantId }}</span>
              </TableCell>
              <TableCell>{{ row.passwordEnabled ? 'yes' : '—' }}</TableCell>
              <TableCell>{{ row.phoneLoginEnabled ? 'yes' : '—' }}</TableCell>
              <TableCell>{{ row.externalLoginEnabled ? 'yes' : '—' }}</TableCell>
              <TableCell>{{ row.clientCount }}</TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  </section>
</template>
