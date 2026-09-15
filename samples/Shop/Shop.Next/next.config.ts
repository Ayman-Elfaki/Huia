import type { NextConfig } from 'next'

const nextConfig: NextConfig = {
  reactStrictMode: true,
  transpilePackages: ['next-huia-headless', 'huia-auth-core'],
}

export default nextConfig
