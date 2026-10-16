import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import { huiaHeadless } from '@huia/tanstack-start-headless/vite'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  plugins: [
    huiaHeadless({
      routeFile: 'src/routes/api/auth/$.ts',
      authConfigImportPath: '@/auth.config',
    }),
    tanstackStart(),
    react(),
  ],
})
