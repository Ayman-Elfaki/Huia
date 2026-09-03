import { defineBuildConfig } from 'unbuild'

export default defineBuildConfig({
  externals: ['#imports', 'nitropack', 'nitropack/runtime', 'h3', '@nuxt/schema', '#app'],
  // The Nuxt plugin / route-middleware runtime files produce non-portable inferred .d.ts
  // (TS2742 against nuxt/dist/app/*). They are auto-registered by path and never type-imported
  // by a consumer, so shipping them without a declaration file is fine.
  failOnWarn: false,
})
