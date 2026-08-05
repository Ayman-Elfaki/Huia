<script setup lang="ts">
useHead({
  meta: [{ name: 'viewport', content: 'width=device-width, initial-scale=1' }],
  link: [{ rel: 'icon', href: '/favicon.ico' }],
  htmlAttrs: { lang: 'en' }
})

useSeoMeta({
  title: 'Huia Admin',
  description: 'Manage Huia applications, scopes, users, roles, authorizations, and sessions.'
})

const { user, loggedIn, logout } = useOidcAuth()
const roles = computed(() => normalizeRoles(user.value?.claims?.role))
const isAdmin = computed(() => roles.value.includes('Admin'))

const navigationItems = [
  { label: 'Applications', icon: 'i-lucide-app-window', to: '/applications' },
  { label: 'Scopes', icon: 'i-lucide-shield-check', to: '/scopes' },
  { label: 'Users', icon: 'i-lucide-users', to: '/users' },
  { label: 'Roles', icon: 'i-lucide-tag', to: '/roles' },
  { label: 'Authorizations', icon: 'i-lucide-key-round', to: '/authorizations' },
  { label: 'Sessions', icon: 'i-lucide-monitor-smartphone', to: '/sessions' }
]
</script>

<template>
  <UApp>
    <div v-if="!loggedIn" class="flex items-center justify-center h-screen">
      <UButton label="Sign in" icon="i-lucide-log-in" size="lg" @click="() => useOidcAuth().login()" />
    </div>

    <div v-else-if="!isAdmin" class="flex items-center justify-center h-screen">
      <UCard class="max-w-sm">
        <template #header>
          <div class="flex items-center gap-2">
            <UIcon name="i-lucide-shield-alert" class="text-error size-5" />
            <span class="font-semibold">Access denied</span>
          </div>
        </template>
        <p class="text-muted text-sm">
          {{ user?.userInfo?.email || user?.userName }} is signed in but doesn't have the "Admin" role that
          Huia's admin API requires.
        </p>
        <template #footer>
          <UButton label="Sign out" color="neutral" variant="subtle" @click="() => logout()" />
        </template>
      </UCard>
    </div>

    <UDashboardGroup v-else>
      <UDashboardSidebar collapsible>
        <template #header>
          <span class="font-semibold text-highlighted">
            <img src="@/assets/images/huia-icon.svg" width="65" alt="logo" />
          </span>
        </template>

        <UNavigationMenu :items="navigationItems" orientation="vertical" />

        <template #footer>
          <UDropdownMenu :items="[[
            { label: 'Profile', icon: 'i-lucide-user', to: '/profile' },
            { label: 'Sign out', icon: 'i-lucide-log-out', onSelect: () => logout() }
          ]]">
            <UButton :label="user?.userInfo?.email as string || user?.userName || 'Account'" icon="i-lucide-user-circle"
              color="neutral" variant="ghost" class="w-full justify-start" trailing-icon="i-lucide-chevron-up" />
          </UDropdownMenu>
        </template>
      </UDashboardSidebar>

      <NuxtPage />
    </UDashboardGroup>
  </UApp>
</template>
