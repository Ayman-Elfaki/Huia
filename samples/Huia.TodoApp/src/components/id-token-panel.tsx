"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";

/** Decodes a compact JWT's payload segment — display only, the signature is never verified client-side. */
function decodeJwtPayload(jwt: string): unknown {
  const payload = jwt.split(".")[1];
  const base64 = payload.replace(/-/g, "+").replace(/_/g, "/");
  const json = decodeURIComponent(
    atob(base64)
      .split("")
      .map((c) => "%" + c.charCodeAt(0).toString(16).padStart(2, "0"))
      .join(""),
  );
  return JSON.parse(json);
}

/** Live "Xm Ys" (or "expired") countdown to `expiresAt` — ticks every second so refreshAccessToken's silent
 * swap (see @/lib/token-refresh, wired into the `jwt` callback in auth.ts) is something you can actually
 * watch happen, not just trust runs invisibly: reload the page once the countdown hits zero and it resets. */
function useCountdown(expiresAt: number | undefined): string | null {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (expiresAt === undefined) return;
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [expiresAt]);

  if (expiresAt === undefined) return null;
  const remainingMs = expiresAt - now;
  if (remainingMs <= 0) return "expired";
  const minutes = Math.floor(remainingMs / 60_000);
  const seconds = Math.floor((remainingMs % 60_000) / 1000);
  return `${minutes}m ${seconds.toString().padStart(2, "0")}s`;
}

export function IdTokenPanel({
  idToken,
  accessTokenExpires,
  hasRefreshToken,
}: {
  idToken: string | undefined;
  accessTokenExpires?: number;
  hasRefreshToken?: boolean;
}) {
  const t = useTranslations("IdToken");
  const countdown = useCountdown(accessTokenExpires);

  if (!idToken) {
    return null;
  }

  let claims: unknown;
  try {
    claims = decodeJwtPayload(idToken);
  } catch {
    claims = null;
  }

  return (
    <Dialog>
      <DialogTrigger render={<Button variant="outline" size="sm" />}>{t("trigger")}</DialogTrigger>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t("title")}</DialogTitle>
          <DialogDescription>{t("description")}</DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          {countdown ? (
            <div className="flex flex-col gap-2 rounded-lg border p-3">
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-medium">{t("accessTokenExpiresIn", { countdown })}</span>
                <Badge variant={hasRefreshToken ? "default" : "secondary"}>
                  {hasRefreshToken ? t("refreshTokenOnFile") : t("noRefreshToken")}
                </Badge>
              </div>
              <p className="text-xs text-muted-foreground">{t("accessTokenRefreshHint")}</p>
            </div>
          ) : null}

          <div>
            <h3 className="mb-1 text-sm font-medium">{t("raw")}</h3>
            <pre className="max-h-32 overflow-auto rounded-md bg-muted p-3 text-xs break-all whitespace-pre-wrap">
              {idToken}
            </pre>
          </div>

          <div>
            <h3 className="mb-1 text-sm font-medium">{t("claims")}</h3>
            <pre className="max-h-64 overflow-auto rounded-md bg-muted p-3 text-xs whitespace-pre-wrap">
              {claims ? JSON.stringify(claims, null, 2) : t("none")}
            </pre>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
