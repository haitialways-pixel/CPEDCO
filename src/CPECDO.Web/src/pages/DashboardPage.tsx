import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";

export function DashboardPage() {
  const { t, i18n } = useTranslation();
  const { session } = useAuth();
  if (!session) return null;

  const roleKey = session.roles[0]?.name ?? "";
  const roleLabel =
    i18n.language === "en"
      ? session.roles[0]?.displayNameEn
      : i18n.language === "ht"
        ? session.roles[0]?.displayNameHt
        : session.roles[0]?.displayNameFr;

  return (
    <main className="dashboard">
      <h1>{t("dashboard.welcome", { name: session.user.fullName })}</h1>
      <section className="facts">
        <article>
          <span>{t("dashboard.branch")}</span>
          <strong>{session.branch.name}</strong>
        </article>
        <article>
          <span>{t("dashboard.role")}</span>
          <strong>{roleLabel ?? t(`roles.${roleKey}`)}</strong>
        </article>
        <article>
          <span>{t("dashboard.currencies")}</span>
          <strong>
            {session.institution.primaryCurrency} · {session.institution.secondaryCurrency}
          </strong>
        </article>
        <article>
          <span>{t("dashboard.timezone")}</span>
          <strong>{session.institution.displayTimeZone}</strong>
        </article>
      </section>
    </main>
  );
}
