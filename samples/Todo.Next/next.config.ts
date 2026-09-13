import type { NextConfig } from 'next'

const nextConfig: NextConfig = {
  reactStrictMode: true,
  transpilePackages: ['next-huia-oidc', 'huia-auth-core'],
}

export default nextConfig
