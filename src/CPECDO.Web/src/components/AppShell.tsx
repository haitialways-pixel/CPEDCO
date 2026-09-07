import { NavLink } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { InstitutionHeader } from "./InstitutionHeader";
import { LanguageSwitch } from "./LanguageSwitch";
import { useAuth } from "../auth/AuthContext";
import type { ReactNode } from "react";

export function AppShell({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const { session, logout } = useAuth();
  if (!session) return null;

  return (
    <div className="app-shell">
      <div className="app-shell__top">
        <InstitutionHeader compact />
        <div className="app-shell__tools">
          <LanguageSwitch />
          <button type="button" className="btn-ghost" onClick={logout}>
            {t("dashboard.logout")}
          </button>
        </div>
      </div>
      <nav className="app-nav" aria-label={t("nav.label")}>
        <NavLink to="/" end>
          {t("nav.dashboard")}
        </NavLink>
        <NavLink to="/members">{t("nav.members")}</NavLink>
        {session.roles.some((r) => ["Admin", "Gerant", "ServiceClient"].includes(r.name)) ? (
          <NavLink to="/members">{t("nav.service")}</NavLink>
        ) : null}
        <NavLink to="/teller">{t("nav.teller")}</NavLink>
        {session.roles.some((r) => r.name === "Caissier" || r.name === "Gerant") ? (
          <NavLink to="/caisse/paiement-credit">{t("nav.tellerCredit")}</NavLink>
        ) : null}
        {session.roles.some((r) => ["Admin", "Gerant"].includes(r.name)) ? (
          <NavLink to="/treasury/banks">{t("nav.treasuryBanks")}</NavLink>
        ) : null}
        <NavLink to="/tresorerie/mouvements">{t("nav.treasuryTransfers")}</NavLink>
        {session.roles.some((r) => ["Admin", "Gerant", "OfficierCredit", "Caissier"].includes(r.name)) ? (
          <NavLink to="/credit/prets">{t("nav.loans")}</NavLink>
        ) : null}
        {session.roles.some((r) => ["Admin", "Gerant", "OfficierCredit", "Caissier"].includes(r.name)) ? (
          <NavLink to="/credit/recouvrement">{t("nav.collection")}</NavLink>
        ) : null}
        {session.roles.some((r) => ["Admin", "Gerant"].includes(r.name)) ? (
          <NavLink to="/credit/produits">{t("nav.loanProducts")}</NavLink>
        ) : null}
        <NavLink to="/reports">{t("nav.reports")}</NavLink>
        {session.roles.some((r) => r.name === "Admin") ? <NavLink to="/staff">{t("nav.staff")}</NavLink> : null}
      </nav>
      {children}
    </div>
  );
}
