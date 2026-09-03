<script setup lang="ts">
import { Moon, Sun } from 'lucide-vue-next'

const { loggedIn, user, logout } = useOidcAuth()
const colorMode = useColorMode()

const displayName = computed(() =>
  user.value?.userInfo?.name
  ?? user.value?.userInfo?.preferred_username
  ?? user.value?.userInfo?.email
  ?? 'account')

function toggleTheme() {
  colorMode.preference = colorMode.value === 'dark' ? 'light' : 'dark'
}
</script>

<template>
  <div class="flex min-h-screen flex-col">
    <header class="border-b">
      <div class="flex h-14 items-center justify-between gap-4 px-4">
        <NuxtLink to="/" class="font-semibold">Huia Admin</NuxtLink>
        <div class="flex items-center gap-3 text-sm">
          <template v-if="loggedIn">
            <span class="text-muted-foreground" data-testid="user-name">{{ displayName }}</span>
            <Button variant="outline" size="sm" data-testid="sign-out" @click="logout('oidc')">Sign out</Button>
          </template>
          <Button
            variant="ghost"
            size="icon"
            data-testid="theme-toggle"
            aria-label="Toggle theme"
            @click="toggleTheme"
          >
            <ClientOnly>
              <Moon v-if="colorMode.value === 'dark'" class="size-4" />
              <Sun v-else class="size-4" />
            </ClientOnly>
          </Button>
        </div>
      </div>
    </header>

    <div class="flex flex-1">
      <aside v-if="loggedIn" class="hidden w-56 shrink-0 border-r bg-card/40 p-3 md:block">
        <AppSidebar />
      </aside>
      <main class="mx-auto w-full max-w-6xl flex-1 px-4 py-8">
        <slot />
      </main>
    </div>
  </div>
</template>
