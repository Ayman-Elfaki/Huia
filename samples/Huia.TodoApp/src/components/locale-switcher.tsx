"use client";

import { useTransition } from "react";
import { Languages } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { routing } from "@/i18n/routing";
import { usePathname, useRouter } from "@/i18n/navigation";
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
  const router = useRouter();
  const [isPending, startTransition] = useTransition();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger render={<Button variant="ghost" size="sm" disabled={isPending} aria-label={t("label")} />}>
        <Languages />
        {localeNames[locale]}
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {routing.locales.map((nextLocale) => (
          <DropdownMenuItem
            key={nextLocale}
            disabled={nextLocale === locale}
            onClick={() => {
              startTransition(() => {
                router.replace(pathname, { locale: nextLocale });
              });
            }}
          >
            {localeNames[nextLocale]}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
