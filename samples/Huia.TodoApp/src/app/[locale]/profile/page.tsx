import {getServerSession} from "next-auth/next";
import {getTranslations} from "next-intl/server";

import {redirect} from "@/i18n/navigation";
import {authOptions} from "@/auth";
import * as manageApi from "@/lib/manage-api";
import {Card, CardContent, CardHeader, CardTitle} from "@/components/ui/card";
import {ProfileInfoForm} from "@/components/profile/profile-info-form";
import {ChangePasswordForm} from "@/components/profile/change-password-form";
import {TwoFactorPanel} from "@/components/profile/two-factor-panel";

export default async function ProfilePage({params}: { params: Promise<{ locale: string }> }) {
    const {locale} = await params;
    const session = await getServerSession(authOptions);
    if (!session?.accessToken || session.error) {
        redirect({href: "/", locale});
        return;
    }

    const t = await getTranslations("Profile");
    const [info, externalLogins] = await Promise.all([
        manageApi.getInfo(session.accessToken),
        manageApi.getExternalLogins(session.accessToken),
    ]);
    const hasPassword = externalLogins.hasPassword;
    const twoFactor = hasPassword ? await manageApi.getTwoFactorStatus(session.accessToken) : null;

    return (
        <div className="flex flex-1 justify-center bg-muted/30 px-4 py-8 sm:py-16">
            <main className="flex w-full max-w-lg flex-col gap-4">
                <h1 className="text-2xl font-semibold">{t("title")}</h1>

                <Card>
                    <CardHeader>
                        <CardTitle>{t("infoTitle")}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <ProfileInfoForm email={info.email} firstName={info.firstName} lastName={info.lastName} />
                    </CardContent>
                </Card>

                {hasPassword ? (
                    <Card>
                        <CardHeader>
                            <CardTitle>{t("passwordTitle")}</CardTitle>
                        </CardHeader>
                        <CardContent>
                            <ChangePasswordForm />
                        </CardContent>
                    </Card>
                ) : null}

                {hasPassword && twoFactor ? (
                    <Card>
                        <CardHeader>
                            <CardTitle>{t("twoFactorTitle")}</CardTitle>
                        </CardHeader>
                        <CardContent>
                            <TwoFactorPanel email={info.email} initial={{status: "idle", ...twoFactor}} />
                        </CardContent>
                    </Card>
                ) : null}
            </main>
        </div>
    );
}
