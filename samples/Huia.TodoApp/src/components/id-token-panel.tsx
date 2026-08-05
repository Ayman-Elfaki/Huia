"use client";

import { useTranslations } from "next-intl";
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

export function IdTokenPanel({ idToken }: { idToken: string | undefined }) {
  const t = useTranslations("IdToken");

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
