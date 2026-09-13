# `next-huia-headless` — Configuration & Usage

## 1. Installation

```bash
npm install next-huia-headless huia-auth-core
```

## 2. Defining Configuration

Create `app/auth.config.ts`:

```ts
import type { HuiaHeadlessConfig } from 'next-huia-headless/server'

export const huiaHeadlessConfig: HuiaHeadlessConfig = {
  backendUrl: process.env.SHOP_API_URL || 'http://localhost:5345',
  session: {
    cookiePassword: process.env.HUIA_SESSION_PASSWORD || 'at-least-32-characters-long-secret-key-1234',
    cookieName: 'huia_sess',
    maxAge: 86400 * 7,
    secure: process.env.NODE_ENV === 'production',
  },
}
```

## 3. Catch-all Route Handler

Create `app/api/auth/[...huia]/route.ts`:

```ts
import { createHuiaHeadlessHandler } from 'next-huia-headless/server'
import { huiaHeadlessConfig } from '@/app/auth.config'

const handler = createHuiaHeadlessHandler(huiaHeadlessConfig)

export const GET = handler
export const POST = handler
```

This creates the following endpoints:
- `POST /api/auth/register`: Creates a new account with email, password, and optional first/last name.
- `POST /api/auth/login`: Authenticates with email and password, establishing the session cookie.
- `POST /api/auth/logout`: Clears the session cookie and purges server-stored tokens.
- `GET /api/auth/session`: Returns the current user claims and logged-in status.
- `POST /api/auth/phone-start`: Dispatches an SMS verification code.
- `POST /api/auth/phone-verify`: Validates the one-time passcode.
- `POST /api/auth/phone-complete-profile`: Sets the name for new phone users.
- `GET /api/auth/external/:provider`: Redirects to an external IdP.
- `POST /api/auth/external-exchange`: Exchanges the external provider callback code.
- `POST /api/auth/external-complete-profile`: Completes profile for new external users.

## 4. Root Layout & Client Provider

In `app/layout.tsx`:

```tsx
import { HuiaHeadlessProvider } from 'next-huia-headless/client'

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <HuiaHeadlessProvider>
          {children}
        </HuiaHeadlessProvider>
      </body>
    </html>
  )
}
```

## 5. Client Hook (`useHuia`)

In any Client Component:

```tsx
'use client'

import { useHuia, useUserSession } from 'next-huia-headless/client'

export function LoginForm() {
  const { login, register, startPhoneLogin, verifyPhoneLogin } = useHuia()
  const { loggedIn, user } = useUserSession()

  const handleLogin = async () => {
    const res = await login({ email: 'user@example.com', password: 'Password1!' })
    if (res.ok) console.log('Logged in!')
  }

  return (
    <div>
      {loggedIn ? <p>Welcome, {user?.email}</p> : <button onClick={handleLogin}>Log In</button>}
    </div>
  )
}
```
