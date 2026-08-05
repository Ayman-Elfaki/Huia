import NextAuth from "next-auth";
import {authOptions} from "@/auth";

// App Router route handlers are matched by named HTTP-method exports, not a default export — NextAuth(...)
// itself just returns the request handler function, so it needs re-exporting under both names here.
const handler = NextAuth(authOptions);

export {handler as GET, handler as POST};
