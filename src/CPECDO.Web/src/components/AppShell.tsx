import { Link, useLocation } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { InstitutionHeader } from "./InstitutionHeader";
import { LanguageSwitch } from "./LanguageSwitch";
import { useAuth } from "../auth/AuthContext";
import { useEffect, useId, useMemo, useState, type ReactNode } from "react";

type RoleName = string;

type NavChild = {
  to: string;
  labelKey: string;
  roles?: RoleName[];
  isActive: (pathname: string, search: string) => boolean;
};

type NavGroup = {
  id: string;
  labelKey: string;
  roles?: RoleName[];
  to?: string;
  end?: boolean;
  children?: NavChild[];
};

function q(search: string, key: string): string | null {
  const raw = search.startsWith("?") ? search.slice(1) : search;
  return new URLSearchParams(raw).get(key);
}

function isLoanDetail(pathname: string): boolean {
  const match = pathname.match(/^\/credit\/prets\/([^/]+)$/);
  return Boolean(match && match[1] !== "nouveau");
}

function prefixMatch(pathname: string, to: string, end?: boolean): boolean {
  if (end) return pathname === to;
  return pathname === to || pathname.startsWith(`${to}/`);
}

const NAV: NavGroup[] = [
  { id: "accueil", labelKey: "nav.dashboard", to: "/", end: true },
  {
    id: "caisse",
    labelKey: "nav.teller",
    children: [
      {
        to: "/teller?vue=caisse",
        labelKey: "nav.tellerOpen",
        isActive: (pathname, search) =>
          pathname === "/teller" && (q(search, "vue") === "caisse" || q(search, "vue") === null)
      },
      {
        to: "/teller?vue=depot",
        labelKey: "nav.deposit",
        isActive: (pathname, search) => pathname === "/teller" && q(search, "vue") === "depot"
      },
      {
        to: "/teller?vue=retrait",
        labelKey: "nav.withdraw",
        isActive: (pathname, search) => pathname === "/teller" && q(search, "vue") === "retrait"
      },
      {
        to: "/teller?vue=encaisser",
        labelKey: "nav.collect",
        isActive: (pathname, search) => pathname === "/teller" && q(search, "vue") === "encaisser"
      },
      {
        to: "/caisse/decaissement-credit",
        labelKey: "nav.disburse",
        roles: ["Caissier"],
        isActive: (pathname) => pathname === "/caisse/decaissement-credit"
      },
      {
        to: "/caisse/paiement-credit",
        labelKey: "nav.tellerPay",
        roles: ["Caissier", "Gerant"],
        isActive: (pathname) => pathname === "/caisse/paiement-credit"
      },
      {
        to: "/caisse/mouvement-interne",
        labelKey: "nav.internalMove",
        roles: ["Admin", "Gerant", "Caissier"],
        isActive: (pathname) => pathname === "/caisse/mouvement-interne"
      }
    ]
  },
  {
    id: "membres",
    labelKey: "nav.members",
    roles: ["Admin", "Gerant", "OfficierCredit"],
    children: [
      {
        to: "/membres",
        labelKey: "nav.membersRegister",
        isActive: (pathname) => pathname === "/membres" || pathname.startsWith("/membres/")
      },
      {
        to: "/epargne/produits",
        labelKey: "nav.savingsProducts",
        roles: ["Admin", "Gerant"],
        isActive: (pathname) => pathname === "/epargne/produits"
      }
    ]
  },
  {
    id: "service",
    labelKey: "nav.service",
    to: "/service-client",
    roles: ["Admin", "Gerant", "ServiceClient"]
  },
  {
    id: "credit",
    labelKey: "nav.credit",
    roles: ["Admin", "Gerant", "OfficierCredit", "Caissier"],
    children: [
      {
        to: "/credit/produits",
        labelKey: "nav.loanProducts",
        roles: ["Admin", "Gerant"],
        isActive: (pathname) => pathname === "/credit/produits"
      },
      {
        to: "/credit/prets",
        labelKey: "nav.loanPipeline",
        roles: ["Admin", "Gerant", "OfficierCredit", "Caissier"],
        isActive: (pathname, search) => {
          const vue = q(search, "vue");
          if (pathname === "/credit/prets/nouveau") return true;
          return pathname === "/credit/prets" && vue !== "fiche" && vue !== "renouvellement";
        }
      },
      {
        to: "/credit/prets?vue=fiche",
        labelKey: "nav.loanFiche",
        roles: ["Admin", "Gerant", "OfficierCredit", "Caissier"],
        isActive: (pathname, search) => {
          const vue = q(search, "vue");
          if (pathname === "/credit/prets" && vue === "fiche") return true;
          return isLoanDetail(pathname) && vue !== "renouvellement";
        }
      },
      {
        to: "/credit/prets?vue=renouvellement",
        labelKey: "nav.loanRenew",
        roles: ["Admin", "Gerant", "OfficierCredit"],
        isActive: (pathname, search) => q(search, "vue") === "renouvellement" && (pathname === "/credit/prets" || isLoanDetail(pathname))
      }
    ]
  },
  {
    id: "tresorerie",
    labelKey: "nav.treasury",
    children: [
      {
        to: "/tresorerie/mouvements",
        labelKey: "nav.treasuryMoves",
        isActive: (pathname, search) =>
          (pathname === "/tresorerie/mouvements" || pathname.startsWith("/tresorerie/mouvements/")) &&
          q(search, "vue") !== "journal"
      },
      {
        to: "/treasury/banks",
        labelKey: "nav.treasuryBanksNav",
        roles: ["Admin", "Gerant"],
        isActive: (pathname) => pathname === "/treasury/banks" || pathname.startsWith("/treasury/banks/")
      },
      {
        to: "/tresorerie/mouvements?vue=journal",
        labelKey: "nav.treasuryJournal",
        isActive: (pathname, search) => pathname === "/tresorerie/mouvements" && q(search, "vue") === "journal"
      }
    ]
  },
  {
    id: "rapports",
    labelKey: "nav.reports",
    children: [
      {
        to: "/reports?kind=teller-cash-proof",
        labelKey: "reports.kind.teller-cash-proof",
        isActive: (pathname, search) => pathname === "/reports" && q(search, "kind") === "teller-cash-proof"
      },
      {
        to: "/reports?kind=trial-balance",
        labelKey: "reports.kind.trial-balance",
        isActive: (pathname, search) =>
          pathname === "/reports" && (q(search, "kind") === "trial-balance" || q(search, "kind") === null)
      },
      {
        to: "/reports?kind=deposits",
        labelKey: "reports.kind.deposits",
        isActive: (pathname, search) => pathname === "/reports" && q(search, "kind") === "deposits"
      },
      {
        to: "/reports?kind=liquidity",
        labelKey: "reports.kind.liquidity",
        isActive: (pathname, search) => pathname === "/reports" && q(search, "kind") === "liquidity"
      },
      {
        to: "/reports?kind=par-ct90",
        labelKey: "reports.kind.par-ct90",
        isActive: (pathname, search) => pathname === "/reports" && q(search, "kind") === "par-ct90"
      },
      {
        to: "/credit/recouvrement",
        labelKey: "nav.collectionSheet",
        roles: ["Admin", "Gerant", "OfficierCredit", "Caissier"],
        isActive: (pathname) => pathname === "/credit/recouvrement"
      },
      {
        to: "/reports?kind=financials",
        labelKey: "reports.kind.financials",
        isActive: (pathname, search) => pathname === "/reports" && q(search, "kind") === "financials"
      },
      {
        to: "/reports?kind=renewal-register",
        labelKey: "reports.kind.renewal-register",
        isActive: (pathname, search) => pathname === "/reports" && q(search, "kind") === "renewal-register"
      }
    ]
  },
  {
    id: "admin",
    labelKey: "nav.admin",
    roles: ["Admin", "Gerant"],
    children: [
      {
        to: "/staff",
        labelKey: "nav.users",
        roles: ["Admin"],
        isActive: (pathname) => pathname === "/staff"
      },
      {
        to: "/staff/sauvegarde",
        labelKey: "nav.backup",
        roles: ["Admin", "Gerant"],
        isActive: (pathname) => pathname === "/staff/sauvegarde"
      }
    ]
  }
];

function canSee(roleNames: string[], roles?: RoleName[]): boolean {
  if (!roles || roles.length === 0) return true;
  return roles.some((role) => roleNames.includes(role));
}

function visibleGroups(roleNames: string[]): NavGroup[] {
  return NAV.flatMap((group) => {
    if (!canSee(roleNames, group.roles)) return [];
    if (!group.children) return [group];
    const children = group.children.filter((child) => canSee(roleNames, child.roles));
    if (children.length === 0) return [];
    return [{ ...group, children }];
  });
}

function activeGroupId(groups: NavGroup[], pathname: string, search: string): string | null {
  return (
    groups.find((group) =>
      group.children
        ? group.children.some((child) => child.isActive(pathname, search))
        : prefixMatch(pathname, group.to ?? "", group.end)
    )?.id ?? null
  );
}

function NavMenu({
  idPrefix,
  groups,
  expandedId,
  pathname,
  search,
  onToggle,
  onLeafClick
}: {
  idPrefix: string;
  groups: NavGroup[];
  expandedId: string | null;
  pathname: string;
  search: string;
  onToggle: (id: string) => void;
  onLeafClick?: () => void;
}) {
  const { t } = useTranslation();

  return (
    <nav className="app-nav__list" aria-label={t("nav.label")}>
      {groups.map((group) => {
        if (group.children && group.children.length > 0) {
          const open = expandedId === group.id;
          const panelId = `${idPrefix}-${group.id}`;
          return (
            <div key={group.id} className={open ? "app-nav__group is-open" : "app-nav__group"}>
              <button
                type="button"
                className="app-nav__parent"
                aria-expanded={open}
                aria-controls={panelId}
                onClick={() => onToggle(group.id)}
              >
                <span>{t(group.labelKey)}</span>
                <span className="app-nav__chevron" aria-hidden="true">
                  {open ? "▾" : "▸"}
                </span>
              </button>
              {open ? (
                <div id={panelId} className="app-nav__children" role="group" aria-label={t(group.labelKey)}>
                  {group.children.map((child) => {
                    const active = child.isActive(pathname, search);
                    return (
                      <Link
                        key={child.to}
                        to={child.to}
                        className={active ? "app-nav__leaf active" : "app-nav__leaf"}
                        aria-current={active ? "page" : undefined}
                        onClick={onLeafClick}
                      >
                        {t(child.labelKey)}
                      </Link>
                    );
                  })}
                </div>
              ) : null}
            </div>
          );
        }

        const active = prefixMatch(pathname, group.to ?? "", group.end);
        return (
          <Link
            key={group.id}
            to={group.to ?? "/"}
            className={active ? "app-nav__leaf app-nav__top active" : "app-nav__leaf app-nav__top"}
            aria-current={active ? "page" : undefined}
            onClick={onLeafClick}
          >
            {t(group.labelKey)}
          </Link>
        );
      })}
    </nav>
  );
}

export function AppShell({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const { session, logout } = useAuth();
  const location = useLocation();
  const drawerTitleId = useId();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  const groups = useMemo(
    () => (session ? visibleGroups(session.roles.map((r) => r.name)) : []),
    [session]
  );
  const routeGroupId = activeGroupId(groups, location.pathname, location.search);

  useEffect(() => {
    setExpandedId(routeGroupId);
  }, [routeGroupId]);

  useEffect(() => {
    setDrawerOpen(false);
  }, [location.pathname, location.search]);

  useEffect(() => {
    if (!drawerOpen) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") setDrawerOpen(false);
    };
    document.addEventListener("keydown", onKey);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = "";
    };
  }, [drawerOpen]);

  useEffect(() => {
    const onResize = () => {
      if (window.innerWidth >= 992) setDrawerOpen(false);
    };
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, []);

  if (!session) return null;

  function toggleGroup(id: string) {
    setExpandedId((current) => (current === id ? null : id));
  }

  const menuProps = {
    groups,
    expandedId,
    pathname: location.pathname,
    search: location.search,
    onToggle: toggleGroup
  };

  return (
    <div className="app-shell">
      <div className="app-shell__top">
        <div className="app-shell__brand">
          <button
            type="button"
            className="app-nav-toggle"
            aria-label={t("nav.openMenu")}
            aria-expanded={drawerOpen}
            aria-controls="app-nav-drawer"
            onClick={() => setDrawerOpen(true)}
          >
            ☰
          </button>
          <InstitutionHeader compact />
        </div>
        <div className="app-shell__tools">
          <LanguageSwitch />
          <button type="button" className="btn-ghost" onClick={logout}>
            {t("dashboard.logout")}
          </button>
        </div>
      </div>
      <div className="app-shell__body">
        <aside className="app-nav app-nav--sidebar">
          <NavMenu idPrefix="nav-side" {...menuProps} />
        </aside>
        <div className="app-shell__main">
          {children}
          <footer className="app-footer">{t("letterhead.footer")}</footer>
        </div>
      </div>
      {drawerOpen ? (
        <button
          type="button"
          className="app-nav-backdrop"
          aria-label={t("nav.closeMenu")}
          onClick={() => setDrawerOpen(false)}
        />
      ) : null}
      <aside
        id="app-nav-drawer"
        className={drawerOpen ? "app-nav app-nav--drawer is-open" : "app-nav app-nav--drawer"}
        aria-hidden={!drawerOpen}
        aria-labelledby={drawerTitleId}
      >
        <div className="app-nav__drawer-head">
          <p id={drawerTitleId} className="app-nav__drawer-title">
            {t("letterhead.sigle")}
          </p>
          <button type="button" className="app-nav__close" onClick={() => setDrawerOpen(false)}>
            {t("nav.closeMenu")}
          </button>
        </div>
        <NavMenu
          idPrefix="nav-drawer"
          {...menuProps}
          onLeafClick={() => setDrawerOpen(false)}
        />
      </aside>
    </div>
  );
}
