import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

const nextConfig: NextConfig = {
    // Required by Aspire's AddNextJsApp for its container publish mode (standalone output + public/).
    output: "standalone",
};

const withNextIntl = createNextIntlPlugin();

export default withNextIntl(nextConfig);
