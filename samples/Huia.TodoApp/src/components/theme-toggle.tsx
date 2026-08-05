"use client";

import { useEffect, useState } from "react";
import { Moon, Sun, MonitorCog } from "lucide-react";
import { useTranslations } from "next-intl";
import { useTheme } from "next-themes";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

const icons = { light: Sun, dark: Moon, system: MonitorCog } as const;

export function ThemeToggle() {
  const t = useTranslations("ThemeToggle");
  const { theme, setTheme } = useTheme();
  // The server always renders as if no theme were stored yet (next-themes has no access to localStorage
  // there), but the client's very first render already knows the real stored preference — showing the same
  // "not mounted yet" icon on both until this effect fires (one render tick after hydration) is what avoids
  // a hydration mismatch on which icon to show, rather than trying to keep the two in sync.
  const [mounted, setMounted] = useState(false);
  useEffect(() => setMounted(true), []);
  const Icon = mounted ? (icons[(theme as keyof typeof icons) ?? "system"] ?? MonitorCog) : MonitorCog;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger render={<Button variant="ghost" size="icon-sm" aria-label={t("label")} />}>
        <Icon />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {(["light", "dark", "system"] as const).map((option) => {
          const OptionIcon = icons[option];
          return (
            <DropdownMenuItem key={option} disabled={theme === option} onClick={() => setTheme(option)}>
              <OptionIcon />
              {t(option)}
            </DropdownMenuItem>
          );
        })}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
