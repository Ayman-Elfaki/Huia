import { Check } from "lucide-react";
import { useTranslations } from "next-intl";
import type { Todo } from "@/lib/todo-api";
import { deleteTodoAction, renameTodoAction, toggleTodoAction } from "@/app/[locale]/todos/actions";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

export function TodoItemRow({ todo }: { todo: Todo }) {
  const t = useTranslations("TodoItem");
  const toggle = toggleTodoAction.bind(null, todo.id, todo.title, !todo.isComplete);
  const rename = renameTodoAction.bind(null, todo.id, todo.isComplete);
  const remove = deleteTodoAction.bind(null, todo.id);

  return (
    <li className="flex flex-wrap items-center gap-2 border-b py-3 last:border-b-0">
      <form action={toggle}>
        <Button
          type="submit"
          variant={todo.isComplete ? "default" : "outline"}
          size="icon-sm"
          className="rounded-full"
          aria-label={todo.isComplete ? t("markNotDone") : t("markDone")}
        >
          {todo.isComplete ? <Check /> : null}
        </Button>
      </form>

      <form action={rename} className="flex min-w-[10rem] flex-1 items-center gap-2">
        <Input
          name="title"
          defaultValue={todo.title}
          className={cn(
            "min-w-0 flex-1 border-transparent bg-transparent shadow-none focus-visible:border-input",
            todo.isComplete && "text-muted-foreground line-through",
          )}
        />
        <Button type="submit" variant="ghost" size="sm">
          {t("save")}
        </Button>
      </form>

      <form action={remove}>
        <Button type="submit" variant="ghost" size="sm" className="text-destructive hover:text-destructive">
          {t("delete")}
        </Button>
      </form>
    </li>
  );
}
