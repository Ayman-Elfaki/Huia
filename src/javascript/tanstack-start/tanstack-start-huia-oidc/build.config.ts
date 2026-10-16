import { fileURLToPath } from 'node:url'
import { defineBuildConfig } from 'unbuild'

const coreSource = fileURLToPath(new URL('../../shared/huia-auth-core/src/index.ts', import.meta.url))

export default defineBuildConfig({
  entries: [
    { input: 'src/index.ts', name: 'index' },
    { input: 'src/server/index.ts', name: 'server/index' },
    { input: 'src/client/index.ts', name: 'client/index' },
    { input: 'src/middleware.ts', name: 'middleware' },
    { input: 'src/vite.ts', name: 'vite' },
  ],
  declaration: true,
  sourcemap: true,
  alias: {
    'huia-auth-core': coreSource,
  },
  externals: [
    '@tanstack/react-start',
    '@tanstack/react-start/*',
    '@tanstack/react-router',
    '@tanstack/react-router/*',
    'react',
    'react/*',
    'react-dom',
    'react-dom/*',
    'vite',
    'node:fs',
    'node:path',
    'node:url',
  ],
  rollup: {
    inlineDependencies: true,
    output: {
      entryFileNames: '[name].js',
      chunkFileNames: 'shared/[name]-[hash].js',
      banner: chunk => (chunk.fileName === 'client/index.js' ? "'use client';" : ''),
    },
  },
})
