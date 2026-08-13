<script setup lang="ts">
import { ChevronsUpDown, LoaderCircle, LogIn, LogOut, ShieldAlert, User } from '@lucide/vue'
import { Toaster } from '@/components/ui/sonner'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger
} from '@/components/ui/dropdown-menu'
import { SidebarFooter, SidebarInset, SidebarMenu, SidebarMenuButton, SidebarMenuItem, SidebarProvider, SidebarTrigger } from '@/components/ui/sidebar'
import { navigationItems } from '@/lib/navigation'

useHead({
  meta: [{ name: 'viewport', content: 'width=device-width, initial-scale=1' }],
  link: [{ rel: 'icon', href: '/favicon.ico' }],
  htmlAttrs: { lang: 'en', class: 'dark' }
})

useSeoMeta({
  title: 'Huia Admin',
  description: 'Manage Huia applications, scopes, users, roles, and authorizations.'
})

const { user, loggedIn, logout } = useOidcAuth()
const roles = computed(() => normalizeRoles(user.value?.claims?.role))
const isAdmin = computed(() => roles.value.includes('Admin'))
const accountLabel = computed(() => (user.value?.userInfo?.email as string) || user.value?.userName || 'Account')

const route = useRoute()
const currentSection = computed(() => navigationItems.find(item => route.path.startsWith(item.to))?.label)

/** First letter(s) of the account label, for the avatar fallback — e.g. "ayman@example.com" -> "A". */
const accountInitial = computed(() => accountLabel.value.charAt(0).toUpperCase())
</script>

<template>
  <Toaster />

  <div v-if="!loggedIn" class="flex items-center justify-center h-screen">
    <Button size="lg" @click="() => useOidcAuth().login()">
      <LogIn data-icon="inline-start" />
      Sign in
    </Button>
  </div>

  <div v-else-if="!isAdmin" class="flex items-center justify-center h-screen">
    <Card class="max-w-sm">
      <CardHeader>
        <CardTitle class="flex items-center gap-2">
          <ShieldAlert class="size-5 text-destructive" />
          Access denied
        </CardTitle>
      </CardHeader>
      <CardContent>
        <p class="text-sm text-muted-foreground">
          {{ accountLabel }} is signed in but doesn't have the "Admin" role that Huia's admin API requires.
        </p>
      </CardContent>
      <CardFooter>
        <Button variant="secondary" @click="() => logout()">
          Sign out
        </Button>
      </CardFooter>
    </Card>
  </div>

  <SidebarProvider v-else>
    <AppSidebar>
      <template #footer>
        <SidebarFooter>
          <SidebarMenu>
            <SidebarMenuItem>
              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <SidebarMenuButton size="lg"
                    class="data-[state=open]:bg-sidebar-accent data-[state=open]:text-sidebar-accent-foreground">
                    <Avatar class="size-8 rounded-lg">
                      <AvatarFallback class="rounded-lg">
                        {{ accountInitial }}
                      </AvatarFallback>
                    </Avatar>
                    <span class="truncate">{{ accountLabel }}</span>
                    <ChevronsUpDown class="ml-auto" />
                  </SidebarMenuButton>
                </DropdownMenuTrigger>
                <DropdownMenuContent side="top" align="start" class="w-56">
                  <DropdownMenuLabel class="truncate">
                    {{ accountLabel }}
                  </DropdownMenuLabel>
                  <DropdownMenuSeparator />
                  <DropdownMenuGroup>
                    <DropdownMenuItem as-child>
                      <NuxtLink to="/profile">
                        <User />
                        Profile
                      </NuxtLink>
                    </DropdownMenuItem>
                  </DropdownMenuGroup>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem @select="() => logout()">
                    <LogOut />
                    Sign out
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarFooter>
      </template>
    </AppSidebar>

    <SidebarInset>
      <header class="flex h-14 shrink-0 items-center gap-2 border-b px-4">
        <SidebarTrigger class="-ml-1" />
        <span class="font-medium text-sm text-muted-foreground">{{ currentSection }}</span>
      </header>

      <NuxtPage>
        <template #default="{ Component }">
          <Suspense>
            <component :is="Component" />
            <template #fallback>
              <div class="flex items-center justify-center py-24">
                <LoaderCircle class="size-6 animate-spin text-muted-foreground" />
              </div>
            </template>
          </Suspense>
        </template>
      </NuxtPage>
    </SidebarInset>
  </SidebarProvider>
</template>
