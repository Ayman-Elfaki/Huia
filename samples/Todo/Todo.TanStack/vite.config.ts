import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import { huiaOidc } from '@huia/tanstack-start-oidc/vite'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  plugins: [
    huiaOidc({
      routeFile: 'src/routes/api/auth/$.ts',
      authConfigImportPath: '@/auth.config',
    }),
    tanstackStart(),
    react(),
  ],
})
