# @huia/tanstack-start-headless

TanStack Start pure JSON authentication library for the [Huia.Headless](https://github.com/Ayman-Elfaki/Huia) identity provider.

Allows client applications to fully own and style their registration, login, SMS OTP, and external identity provider forms, backed by secure encrypted HTTP-only session cookies on TanStack Start.

## Installation

```bash
npm install @huia/tanstack-start-headless huia-auth-core
```

## Quick Start

### 1. Configure Auth (`app/auth.config.ts`)

```ts
import type { HuiaHeadlessConfig } from '@huia/tanstack-start-headless'

export const huiaConfig: HuiaHeadlessConfig = {
  baseUrl: process.env.HUIA_HEADLESS_BASE_URL ?? 'https://api.example.com',
  session: {
    password: process.env.HUIA_SESSION_PASSWORD!,
  },
}
```

### 2. Register Vite Plugin (`vite.config.ts`)

```ts
import { defineConfig } from 'vite'
import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import { huiaHeadless } from '@huia/tanstack-start-headless/vite'

export default defineConfig({
  plugins: [
    huiaHeadless(),
    tanstackStart(),
  ],
})
```

### 3. Use in Client Components

```tsx
import { useHuia } from '@huia/tanstack-start-headless/client'

export function LoginForm() {
  const { login, register, startPhoneLogin, verifyPhoneLogin } = useHuia()

  const handlePasswordLogin = async (e: React.FormEvent) => {
    e.preventDefault()
    const res = await login({ email, password })
    if (res.ok) {
      // Logged in!
    }
  }

  // Render your custom login form
}
```

## License

MIT
