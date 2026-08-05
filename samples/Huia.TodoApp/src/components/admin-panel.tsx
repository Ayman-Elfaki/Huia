import { getTranslations } from "next-intl/server";
import { getReportSummary } from "@/lib/todo-api";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

/**
 * Only rendered for a session whose id_token carries the "Admin" role (see page.tsx) — but the actual
 * access boundary is ReportsEndpoints on the API side (scope + audience + role), not this check. A
 * tampered/forged claim here would still get a 403 from the API and render nothing back from
 * getReportSummary, never real data.
 */
export async function AdminPanel({ accessToken }: { accessToken: string }) {
  const t = await getTranslations("Admin");
  const summary = await getReportSummary(accessToken);

  if (!summary) {
    return null;
  }

  return (
    <Card className="mt-4">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          {t("title")}
          <Badge variant="secondary">{t("badge")}</Badge>
        </CardTitle>
      </CardHeader>
      <CardContent className="grid grid-cols-3 gap-4 text-center">
        <Stat value={summary.totalTodos} label={t("totalTodos")} />
        <Stat value={summary.completedTodos} label={t("completedTodos")} />
        <Stat value={summary.distinctOwners} label={t("distinctOwners")} />
      </CardContent>
    </Card>
  );
}

function Stat({ value, label }: { value: number; label: string }) {
  return (
    <div className="flex flex-col">
      <span className="text-2xl font-semibold">{value}</span>
      <span className="text-xs text-muted-foreground">{label}</span>
    </div>
  );
}
