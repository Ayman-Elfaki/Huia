import { useTranslations } from "next-intl";
import { createTodoAction } from "@/app/[locale]/todos/actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export function NewTodoForm() {
  const t = useTranslations("NewTodoForm");

  return (
    <form action={createTodoAction} className="flex gap-2">
      <Input name="title" placeholder={t("placeholder")} required className="flex-1" />
      <Button type="submit">{t("add")}</Button>
    </form>
  );
}
