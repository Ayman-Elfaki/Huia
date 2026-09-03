<script setup lang="ts">
import { AppWindow, Building2, KeyRound, LayoutDashboard, ShieldCheck, UserCog, Users, UsersRound } from 'lucide-vue-next'

const route = useRoute()

const items = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, testid: 'nav-dashboard' },
  { to: '/tenants', label: 'Tenants', icon: Building2, testid: 'nav-tenants' },
  { to: '/users', label: 'Users', icon: Users, testid: 'nav-users' },
  { to: '/roles', label: 'Roles', icon: UsersRound, testid: 'nav-roles' },
  { to: '/clients', label: 'Clients', icon: AppWindow, testid: 'nav-clients' },
  { to: '/scopes', label: 'Scopes', icon: ShieldCheck, testid: 'nav-scopes' },
  { to: '/keys', label: 'Signing keys', icon: KeyRound, testid: 'nav-keys' },
  { to: '/profile', label: 'Profile', icon: UserCog, testid: 'nav-profile' },
]

function isActive(to: string) {
  return to === '/' ? route.path === '/' : route.path.startsWith(to)
}
</script>

<template>
  <nav class="flex flex-col gap-1" data-testid="admin-nav">
    <NuxtLink
      v-for="item in items"
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
