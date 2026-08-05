"use client";

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
  const Icon = icons[(theme as keyof typeof icons) ?? "system"] ?? MonitorCog;

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
