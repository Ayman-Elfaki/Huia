import createMiddleware from "next-intl/middleware";
import { routing } from "./i18n/routing";

/**
 * Next.js 16 renamed the `middleware.ts` file convention (and its exported function) to `proxy.ts`/`proxy` —
 * see node_modules/next/dist/docs/01-app/03-api-reference/03-file-conventions/proxy.md. next-intl's
 * `createMiddleware` handler itself is unaffected (same NextRequest -> NextResponse shape); only the
 * file/export name it's wired up under changes.
 */
export const proxy = createMiddleware(routing);

export const config = {
    // Skip API routes (notably NextAuth's own /api/auth/*, which must never be locale-prefixed), Next's
    // internals, and any request for a file with an extension (static assets).
    matcher: ["/((?!api|_next|_vercel|.*\\..*).*)"],
};
