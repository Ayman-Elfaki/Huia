import type { Metadata } from 'next'
import './globals.css'
import { HuiaOidcProvider } from 'next-huia-oidc/client'
import { Header } from './header'

export const metadata: Metadata = {
  title: 'Huia Todo (Next.js)',
  description: 'Next.js 15 sample application powered by next-huia-oidc and Huia.OpenId',
}

export default function RootLayout({
  children,
}: {
  children: React.ReactNode
}) {
  return (
    <html lang="en" className="dark">
      <body className="bg-[#0b0f19] text-gray-100 min-h-screen flex flex-col antialiased">
        <HuiaOidcProvider>
          <Header />
          <main className="flex-1 max-w-5xl w-full mx-auto p-6 md:p-8">
            {children}
          </main>
          <footer className="border-t border-gray-800/60 py-6 text-center text-xs text-gray-500">
            Huia Multi-Tenant OpenID Connect &bull; Next.js 15 Client
          </footer>
        </HuiaOidcProvider>
      </body>
    </html>
  )
}
