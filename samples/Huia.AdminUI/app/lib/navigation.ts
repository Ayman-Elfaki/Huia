import { AppWindow, KeyRound, ShieldCheck, Tag, Users } from '@lucide/vue'
import type { Component } from 'vue'

export interface NavigationItem {
  label: string
  to: string
  icon: Component
}

export const navigationItems: NavigationItem[] = [
  { label: 'Applications', icon: AppWindow, to: '/applications' },
  { label: 'Scopes', icon: ShieldCheck, to: '/scopes' },
  { label: 'Users', icon: Users, to: '/users' },
  { label: 'Roles', icon: Tag, to: '/roles' },
  { label: 'Authorizations', icon: KeyRound, to: '/authorizations' }
]
