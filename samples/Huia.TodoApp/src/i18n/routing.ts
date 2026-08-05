import { defineRouting } from "next-intl/routing";

/**
 * English and Arabic (RTL) — the same locale pair Huia's own Identity UI supports out of the box
 * (see Huia.HuiaLocalizationBuilder / docs/localization.md), so signing in and managing todos stay in the
 * same language throughout.
 */
export const routing = defineRouting({
    locales: ["en", "ar"],
    defaultLocale: "en",
});
