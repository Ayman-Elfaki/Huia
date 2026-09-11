const huiaBaseUrl = process.env.NUXT_PUBLIC_HUIA_BASE_URL ?? 'https://localhost:5340'
const shopApiUrl = process.env.NUXT_PUBLIC_SHOP_API_URL ?? 'https://localhost:5340'

export default defineNuxtConfig({
  compatibilityDate: '2025-01-01',
  devtools: { enabled: false },
  modules: [
    '../../src/nuxt-headless/src/module',
  ],
  huiaHeadless: {
    huia: { baseUrl: huiaBaseUrl, tenant: 'shop' },
  },
  runtimeConfig: {
    shopApiUrl,
    public: {
      appName: 'Huia Shop',
      huiaBaseUrl,
    },
  },
})
