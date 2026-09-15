import type { Metadata } from 'next'
import './globals.css'
import { HuiaHeadlessProvider } from 'next-huia-headless/client'
import { Header } from './header'

export const metadata: Metadata = {
  title: 'Huia Shop (Next.js)',
  description: 'Next.js 15 headless ecommerce sample powered by next-huia-headless',
}

export default function RootLayout({
  children,
}: {
  children: React.ReactNode
}) {
  return (
    <html lang="en" className="dark">
      <body className="bg-[#0b0f19] text-gray-100 min-h-screen flex flex-col antialiased">
        <HuiaHeadlessProvider>
          <Header />
          <main className="flex-1 max-w-5xl w-full mx-auto p-6 md:p-8">
            {children}
          </main>
          <footer className="border-t border-gray-800/60 py-6 text-center text-xs text-gray-500">
            Huia Headless Identity &bull; Next.js 15 Storefront
          </footer>
        </HuiaHeadlessProvider>
      </body>
    </html>
  )
}
