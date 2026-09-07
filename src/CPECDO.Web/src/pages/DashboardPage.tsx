import { Link } from "react-router-dom";
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
        <p>
          <Link className="btn-primary" to="/members">
            {t("nav.members")}
          </Link>{" "}
          <Link className="btn-primary" to="/teller">
            {t("nav.teller")}
          </Link>
          {session.roles.some((r) => ["Admin", "Gerant", "ServiceClient"].includes(r.name)) ? (
            <>
              {" "}
              <Link className="btn-primary" to="/members">
                {t("nav.service")}
              </Link>
            </>
          ) : null}
          {session.roles.some((r) => r.name === "Admin") ? (
            <>
              {" "}
              <Link className="btn-primary" to="/staff">
                {t("nav.staff")}
              </Link>
            </>
          ) : null}{" "}
          {session.roles.some((r) => ["Admin", "Gerant"].includes(r.name)) ? (
            <>
              <Link className="btn-primary" to="/treasury/banks">
                {t("nav.treasuryBanks")}
              </Link>{" "}
            </>
          ) : null}
          <Link className="btn-primary" to="/tresorerie/mouvements">
            {t("nav.treasuryTransfers")}
          </Link>{" "}
          {session.roles.some((r) => ["Admin", "Gerant", "OfficierCredit", "Caissier"].includes(r.name)) ? (
            <>
              <Link className="btn-primary" to="/credit/prets">
                {t("nav.loans")}
              </Link>{" "}
            </>
          ) : null}
          {session.roles.some((r) => ["Admin", "Gerant"].includes(r.name)) ? (
            <>
              <Link className="btn-primary" to="/credit/produits">
                {t("nav.loanProducts")}
              </Link>{" "}
            </>
          ) : null}
          <Link className="btn-primary" to="/reports">
            {t("nav.reports")}
          </Link>
        </p>
    </main>
  );
}
