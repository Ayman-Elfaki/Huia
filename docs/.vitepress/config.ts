import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'

export default withMermaid(defineConfig({
  title: 'Huia',
  description: 'Multi-tenant OpenID Connect / OAuth 2.0 (Huia.OpenId) and single-tenant bearer-token (Huia.Headless) identity for ASP.NET Core, with two first-party Nuxt 4 client modules',
  cleanUrls: true,
  themeConfig: {
    nav: [
      { text: 'Guide', link: '/guide/getting-started' },
      { text: '.NET reference', link: '/dotnet/options' },
      {
        text: 'Next.js',
        items: [
          { text: 'OIDC (next-huia-oidc)', link: '/next/overview' },
          { text: 'Headless (next-huia-headless)', link: '/next-headless/overview' },
        ],
      },
      {
        text: 'Nuxt',
        items: [
          { text: 'OIDC (nuxt-huia-oidc)', link: '/nuxt/overview' },
          { text: 'Headless (nuxt-huia-headless)', link: '/nuxt-headless/overview' },
        ],
      },
      { text: 'Core', link: '/core/overview' },
      { text: 'Architecture', link: '/architecture/overview' },
      { text: 'Security', link: '/security/index' },
    ],
    sidebar: [
      {
        text: 'Guide',
        items: [
          { text: 'Getting started', link: '/guide/getting-started' },
          { text: 'Configuration', link: '/guide/configuration' },
          { text: 'Multi-tenancy', link: '/guide/multi-tenancy' },
          { text: 'Pushed authorization (PAR)', link: '/guide/pushed-authorization' },
          { text: 'Admin console', link: '/guide/admin-ui' },
          { text: 'Email testing', link: '/guide/email-testing' },
        ],
      },
      {
        text: '.NET reference — Huia.OpenId',
        items: [
          { text: 'Options reference', link: '/dotnet/options' },
          { text: 'HuiaUserManager & user types', link: '/dotnet/user-manager' },
          { text: 'Endpoints', link: '/dotnet/endpoints' },
          { text: 'Passwordless SMS', link: '/dotnet/passwordless-sms' },
          { text: 'Passkeys', link: '/dotnet/passkeys' },
          { text: 'External login', link: '/dotnet/external-login' },
          { text: 'Events', link: '/dotnet/events' },
        ],
      },
      {
        text: '.NET reference — Huia.Headless',
        items: [
          { text: 'Huia.Headless', link: '/dotnet/headless' },
        ],
      },
      {
        text: 'Shared Core (huia-auth-core)',
        items: [
          { text: 'Overview', link: '/core/overview' },
        ],
      },
      {
        text: 'Next.js library — OIDC (next-huia-oidc)',
        items: [
          { text: 'Overview', link: '/next/overview' },
          { text: 'Configuration', link: '/next/configuration' },
        ],
      },
      {
        text: 'Next.js library — Headless (next-huia-headless)',
        items: [
          { text: 'Overview', link: '/next-headless/overview' },
          { text: 'Configuration', link: '/next-headless/configuration' },
        ],
      },
      {
        text: 'Nuxt module — OIDC (nuxt-huia-oidc)',
        items: [
          { text: 'Overview', link: '/nuxt/overview' },
          { text: 'Configuration', link: '/nuxt/configuration' },
          { text: 'Session model & security', link: '/nuxt/session-model' },
          { text: 'Route protection', link: '/nuxt/route-protection' },
          { text: 'Migrating from nuxt-oidc-auth', link: '/nuxt/migrating' },
        ],
      },
      {
        text: 'Nuxt module — Headless (nuxt-huia-headless)',
        items: [
          { text: 'Overview', link: '/nuxt-headless/overview' },
          { text: 'Configuration', link: '/nuxt-headless/configuration' },
        ],
      },
      {
        text: 'Architecture',
        items: [
          { text: 'Overview', link: '/architecture/overview' },
          { text: 'Request & token flow', link: '/architecture/request-flow' },
          { text: 'Key management', link: '/architecture/key-management' },
        ],
      },
      {
        text: 'Security',
        items: [
          { text: 'Overview', link: '/security/index' },
          { text: 'Security headers', link: '/security/security-headers' },
        ],
      },
    ],
    outline: [2, 3],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/Ayman-Elfaki/Huia' },
    ],
  },
}))
