import { fileURLToPath } from 'node:url'
import { defineBuildConfig } from 'unbuild'

const coreSource = fileURLToPath(new URL('../../shared/huia-auth-core/src/index.ts', import.meta.url))

export default defineBuildConfig({
  entries: [
    { input: 'src/index.ts', name: 'index' },
    { input: 'src/server/index.ts', name: 'server/index' },
    { input: 'src/client/index.ts', name: 'client/index' },
  ],
  declaration: true,
  sourcemap: true,
  alias: {
    'huia-auth-core': coreSource,
  },
  externals: ['next', 'next/*', 'react', 'react/*', 'react-dom', 'react-dom/*'],
  rollup: {
    inlineDependencies: true,
    output: {
      entryFileNames: '[name].js',
      chunkFileNames: 'shared/[name]-[hash].js',
    },
  },
})