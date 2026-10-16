import { createRootRoute, Outlet, HeadContent, Scripts } from '@tanstack/react-router'
import { HuiaHeadlessProvider } from '@huia/tanstack-start-headless/client'
import { Header } from '../components/header'
import '../styles.css'

export const Route = createRootRoute({
  head: () => ({
    meta: [
      { charSet: 'utf-8' },
      { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      { title: 'Huia Shop (TanStack Start)' },
    ],
  }),
  component: RootComponent,
})

function RootComponent() {
  return (
    <html lang="en" className="dark">
      <head>
        <HeadContent />
      </head>
      <body className="bg-[#0b0f19] text-gray-100 min-h-screen flex flex-col antialiased">
        <HuiaHeadlessProvider>
          <Header />
          <main className="flex-1 max-w-5xl w-full mx-auto p-6 md:p-8">
            <Outlet />
          </main>
          <footer className="border-t border-gray-800/60 py-6 text-center text-xs text-gray-500">
            Huia Headless Identity &bull; TanStack Start Storefront
          </footer>
        </HuiaHeadlessProvider>
        <Scripts />
      </body>
    </html>
  )
}
