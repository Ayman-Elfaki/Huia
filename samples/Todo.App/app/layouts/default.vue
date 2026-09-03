<script setup lang="ts">
import { Languages, Moon, Sun } from 'lucide-vue-next'

const { loggedIn, user } = useUserSession()
const { login, logout } = useAuth()
const route = useRoute()
const colorMode = useColorMode()
const { locale, locales, setLocale, t } = useI18n()

const displayName = computed(() =>
  user.value?.name
  ?? user.value?.preferred_username
  ?? user.value?.email
  ?? 'account')

const currentDir = computed(() =>
  locales.value.find(l => l.code === locale.value)?.dir ?? 'ltr')

// Keep <html lang/dir> in step with the active locale (drives RTL for Arabic).
useHead(() => ({
  htmlAttrs: { lang: locale.value, dir: currentDir.value },
}))

function toggleTheme() {
  colorMode.preference = colorMode.value === 'dark' ? 'light' : 'dark'
}

// Carry the chosen locale into the Huia sign-in UI via the OIDC ui_locales hint.
function signIn() {
  return login({ locale: locale.value })
}
</script>

<template>
  <div class="min-h-screen">
    <header class="border-b">
      <div class="mx-auto flex h-14 max-w-3xl items-center justify-between px-4">
        <NuxtLink to="/" class="font-semibold">{{ t('nav.brand') }}</NuxtLink>
        <nav class="flex items-center gap-4 text-sm">
          <template v-if="loggedIn">
            <NuxtLink to="/tasks" :class="route.path === '/tasks' ? 'text-primary' : 'text-muted-foreground'">{{ t('nav.tasks') }}</NuxtLink>
            <NuxtLink to="/profile" :class="route.path === '/profile' ? 'text-primary' : 'text-muted-foreground'">{{ t('nav.profile') }}</NuxtLink>
            <span class="text-muted-foreground" data-testid="user-name">{{ displayName }}</span>
            <Button variant="outline" size="sm" data-testid="sign-out" @click="logout()">{{ t('nav.signOut') }}</Button>
          </template>
          <Button v-else size="sm" data-testid="sign-in" @click="signIn">{{ t('nav.signIn') }}</Button>

          <DropdownMenu>
            <DropdownMenuTrigger as-child>
              <Button variant="ghost" size="icon" data-testid="locale-switcher" :aria-label="t('nav.language')">
                <Languages class="size-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem
                v-for="l in locales"
                :key="l.code"
                :data-testid="`locale-option-${l.code}`"
                :class="l.code === locale ? 'font-semibold' : ''"
                @select="setLocale(l.code)"
              >
                {{ l.name }}
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>

          <Button variant="ghost" size="icon" data-testid="theme-toggle" :aria-label="t('nav.toggleTheme')" @click="toggleTheme">
            <ClientOnly>
              <Moon v-if="colorMode.value === 'dark'" class="size-4" />
              <Sun v-else class="size-4" />
            </ClientOnly>
          </Button>
        </nav>
      </div>
    </header>
    <main class="mx-auto max-w-3xl px-4 py-10">
      <slot />
    </main>
  </div>
</template>
