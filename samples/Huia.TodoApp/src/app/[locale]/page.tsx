import { getTranslations } from "next-intl/server";
import { listTodos } from "@/lib/todo-api";
import { Link } from "@/i18n/navigation";
import { SignInButton, SignOutButton } from "@/components/auth-buttons";
import { NewTodoForm } from "@/components/new-todo-form";
import { TodoItemRow } from "@/components/todo-item-row";
import { LocaleSwitcher } from "@/components/locale-switcher";
import { ThemeToggle } from "@/components/theme-toggle";
import { IdTokenPanel } from "@/components/id-token-panel";
import { AdminPanel } from "@/components/admin-panel";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { getServerSession } from "next-auth/next"
import { authOptions } from "@/auth";
import { huiaEndSessionUrl } from "@/lib/utils";

export default async function Home({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  const rawSession = await getServerSession(authOptions);
  const t = await getTranslations("HomePage");

  // A dead refresh token (revoked session, expired, ...) is set as session.error by the jwt callback in
  // auth.ts rather than thrown — treat it the same as "not signed in" so the page falls back to the sign-in
  // prompt instead of rendering panels backed by an access token that's about to start failing every call.
  const session = rawSession?.error ? undefined : rawSession;

  const fullName = [session?.givenName, session?.familyName].filter(Boolean).join(" ") || undefined;

  return (
    <div className="flex flex-1 justify-center bg-muted/30 px-4 py-8 sm:py-16">
      <main className="w-full max-w-lg">
        <header className="mb-8 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-center justify-between gap-2">
            <h1 className="text-2xl font-semibold">{t("heading")}</h1>
            <div className="flex items-center gap-1">
              <ThemeToggle />
              <LocaleSwitcher />
            </div>
          </div>

          {session?.accessToken ? (
            <div className="flex flex-wrap items-center gap-2 sm:justify-end">
              <span className="min-w-0 flex-1 truncate text-sm text-muted-foreground sm:max-w-48 sm:flex-none">
                {fullName ?? session.user?.email ?? session.user?.name}
              </span>
              <IdTokenPanel
                idToken={session.idToken}
                accessTokenExpires={session.accessTokenExpires}
                hasRefreshToken={session.hasRefreshToken}
              />
              <Button variant="outline" size="sm" render={<Link href="/profile" />}>
                {t("profileLink")}
              </Button>
              <SignOutButton endSessionUrl={huiaEndSessionUrl(session.idToken)} />
            </div>
          ) : null}
        </header>

        {session?.accessToken ? (
          <>
            <TodoPanel accessToken={session.accessToken} />
            {session.roles?.includes("Admin") ? <AdminPanel accessToken={session.accessToken} /> : null}
          </>
        ) : (
          <Card>
            <CardContent className="flex flex-col items-start gap-4">
              <p className="text-sm text-muted-foreground">{t("signInPrompt")}</p>
              <SignInButton locale={locale} />
            </CardContent>
          </Card>
        )}
      </main>
    </div>
  );
}

async function TodoPanel({ accessToken }: { accessToken: string }) {
  const todos = await listTodos(accessToken);
  const t = await getTranslations("HomePage");

  return (
    <Card>
      <CardContent>
        <NewTodoForm />

        {todos.length === 0 ? (
          <p className="mt-6 text-sm text-muted-foreground">{t("empty")}</p>
        ) : (
          <ul className="mt-4">
            {todos.map((todo) => (
              <TodoItemRow key={todo.id} todo={todo} />
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
