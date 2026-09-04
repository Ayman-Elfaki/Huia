<script setup lang="ts">
import { AppWindow, Building2, KeyRound, LayoutDashboard, ShieldCheck, UserCog, Users, UsersRound } from 'lucide-vue-next'

// Every admin-console route is API-gated to the `huia.administrator` role server-side; these
// `requiresRole` markers just keep the nav from ever showing a link that would 403, e.g. if a
// future role split leaves some signed-in users without full administrator access.
const ADMINISTRATOR = 'huia.administrator'

const route = useRoute()
const { hasRole } = useAuth()

const items = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, testid: 'nav-dashboard' },
  { to: '/tenants', label: 'Tenants', icon: Building2, testid: 'nav-tenants', requiresRole: ADMINISTRATOR },
  { to: '/users', label: 'Users', icon: Users, testid: 'nav-users', requiresRole: ADMINISTRATOR },
  { to: '/roles', label: 'Roles', icon: UsersRound, testid: 'nav-roles', requiresRole: ADMINISTRATOR },
  { to: '/clients', label: 'Clients', icon: AppWindow, testid: 'nav-clients', requiresRole: ADMINISTRATOR },
  { to: '/scopes', label: 'Scopes', icon: ShieldCheck, testid: 'nav-scopes', requiresRole: ADMINISTRATOR },
  { to: '/keys', label: 'Signing keys', icon: KeyRound, testid: 'nav-keys', requiresRole: ADMINISTRATOR },
  { to: '/profile', label: 'Profile', icon: UserCog, testid: 'nav-profile' },
]

const visibleItems = computed(() => items.filter(item => !item.requiresRole || hasRole(item.requiresRole)))

function isActive(to: string) {
  return to === '/' ? route.path === '/' : route.path.startsWith(to)
}
</script>

<template>
  <nav class="flex flex-col gap-1" data-testid="admin-nav">
    <NuxtLink
      v-for="item in visibleItems"
      :key="item.to"
      :to="item.to"
      :data-testid="item.testid"
      :class="[
        'flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors',
        isActive(item.to)
          ? 'bg-primary/10 font-medium text-primary'
          : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground',
      ]"
    >
      <component :is="item.icon" class="size-4 shrink-0" />
      {{ item.label }}
    </NuxtLink>
  </nav>
</template>
