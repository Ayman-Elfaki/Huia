import { createRootRoute, Outlet, HeadContent, Scripts } from '@tanstack/react-router'
import { HuiaOidcProvider } from '@huia/tanstack-start-oidc/client'
import { Header } from '../components/header'
import '../styles.css'

export const Route = createRootRoute({
  head: () => ({
    meta: [
      { charSet: 'utf-8' },
      { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      { title: 'Huia Todo (TanStack Start)' },
    ],
  }),
  component: RootComponent,
})

function RootComponent() {
  return (
    <html lang="en">
      <head>
        <HeadContent />
      </head>
      <body className="min-h-screen bg-gray-950 text-gray-100 antialiased">
        <HuiaOidcProvider>
          <Header />
          <main className="max-w-5xl mx-auto px-6 py-8">
            <Outlet />
          </main>
        </HuiaOidcProvider>
        <Scripts />
      </body>
    </html>
  )
}
