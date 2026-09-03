<script setup lang="ts">
import type { TenantRow } from '~/composables/useTenantOptions'

const { loggedIn, login } = useAuth()

// The admin API 401s when signed out; the landing branch below doesn't render this data anyway.
const { data: tenants } = useHuiaData<TenantRow[]>('admin/tenants', { default: () => [] })

const sections = [
  { to: '/tenants', label: 'Tenants', hint: 'Configured tenants and their sign-in methods' },
  { to: '/users', label: 'Users', hint: 'Every account across all tenants' },
  { to: '/clients', label: 'Clients', hint: 'Registered OAuth / OIDC applications' },
  { to: '/scopes', label: 'Scopes', hint: 'Per-tenant OAuth scopes — create, edit, delete' },
  { to: '/keys', label: 'Signing keys', hint: 'Per-tenant token signing keys and their status' },
]
</script>

<template>
  <section v-if="!loggedIn" class="flex flex-col gap-6">
    <div class="flex flex-col gap-2">
      <h1 class="text-3xl font-bold tracking-tight">Huia administration</h1>
      <p class="text-muted-foreground">
        Signs in against the <code>master</code> tenant. The admin API requires the
        <code>huia.administrator</code> role.
      </p>
    </div>

    <Card>
      <CardHeader>
        <CardTitle>Sign in</CardTitle>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <p class="text-sm text-muted-foreground">
          Use the seeded administrator (<code>admin@huia.local</code> / <code>Admin1!Pass</code>).
        </p>
        <Button data-testid="landing-sign-in" class="self-start" @click="login()">
          Sign in with Huia
        </Button>
      </CardContent>
    </Card>
  </section>

  <section v-else class="flex flex-col gap-6" data-testid="dashboard">
    <h1 class="text-2xl font-bold tracking-tight">Dashboard</h1>

    <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <Card>
        <CardHeader>
          <CardDescription>Tenants</CardDescription>
          <CardTitle class="text-3xl" data-testid="stat-tenants">{{ tenants.length }}</CardTitle>
        </CardHeader>
        <CardContent class="text-sm text-muted-foreground">
          {{ tenants.reduce((n, t) => n + t.clientCount, 0) }} registered clients in total
        </CardContent>
      </Card>

      <NuxtLink v-for="s in sections" :key="s.to" :to="s.to" class="block">
        <Card class="h-full transition-colors hover:border-primary">
          <CardHeader>
            <CardTitle class="text-base">{{ s.label }}</CardTitle>
            <CardDescription>{{ s.hint }}</CardDescription>
          </CardHeader>
        </Card>
      </NuxtLink>
    </div>

    <Card>
      <CardHeader>
        <CardTitle>Tenants</CardTitle>
        <CardDescription>Sign-in methods enabled per tenant.</CardDescription>
      </CardHeader>
      <CardContent>
        <Table>
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
            <TableRow v-for="row in tenants" :key="row.tenantId">
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
