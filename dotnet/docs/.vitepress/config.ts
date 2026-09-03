import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Huia',
  description: 'Multi-tenant OpenID Connect / OAuth 2.0 Identity Provider for ASP.NET Core 10',
  cleanUrls: true,
  themeConfig: {
    nav: [
      { text: 'Guide', link: '/guide/getting-started' },
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
        text: 'Architecture',
        items: [
          { text: 'Overview', link: '/architecture/overview' },
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
  },
})
