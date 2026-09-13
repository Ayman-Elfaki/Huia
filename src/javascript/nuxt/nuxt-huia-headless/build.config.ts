import { defineBuildConfig } from 'unbuild'

export default defineBuildConfig({
  externals: ['#imports', 'nitropack', 'nitropack/runtime', 'h3', '@nuxt/schema', '#app'],
})
