"use client";

import { Languages } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { routing } from "@/i18n/routing";
import { getPathname, usePathname } from "@/i18n/navigation";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

const localeNames: Record<string, string> = {
  en: "English",
  ar: "العربية",
};

export function LocaleSwitcher() {
  const t = useTranslations("LocaleSwitcher");
  const locale = useLocale();
  const pathname = usePathname();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger render={<Button variant="ghost" size="sm" aria-label={t("label")} />}>
        <Languages />
        {localeNames[locale]}
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {routing.locales.map((nextLocale) => (
          <DropdownMenuItem
            key={nextLocale}
            disabled={nextLocale === locale}
            onClick={() => {
              // A full navigation rather than next-intl's router.replace (a client-side transition):
              // switching locale changes the root layout's <html lang>/<dir>, which forces next-themes'
              // anti-FOUC <script> element (see theme-provider.tsx) to freshly client-mount instead of
              // hydrating an existing server-rendered one — React warns whenever a <script> tag shows up in
              // a client-only render pass ("Encountered a script tag while rendering React component"), even
              // though the tag itself is inert either way. A hard navigation goes through the normal
              // server-render-then-hydrate path instead, where that same script element is expected.
              window.location.href = getPathname({ href: pathname, locale: nextLocale });
            }}
          >
            {localeNames[nextLocale]}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
