import { Link, useLocation } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { InstitutionHeader } from "./InstitutionHeader";
import { LanguageSwitch } from "./LanguageSwitch";
import { useAuth } from "../auth/AuthContext";
import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react";

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
      },
      {
        to: "/credit/rapports",
        labelKey: "nav.creditReports",
        roles: ["Admin", "Gerant", "OfficierCredit", "Commissaire"],
        isActive: (pathname) => pathname === "/credit/rapports"
      },
      {
        to: "/credit/fonds",
        labelKey: "nav.creditPool",
        roles: ["Admin", "Gerant", "OfficierCredit", "Commissaire"],
        isActive: (pathname) => pathname === "/credit/fonds"
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
        isActive: (pathname, search) => pathname === "/reports" && q(search, "kind") === "trial-balance"
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

const LAST_CHILD_KEY = "cpcredo.nav.last.";

function rememberChild(groupId: string, to: string) {
  try {
    sessionStorage.setItem(LAST_CHILD_KEY + groupId, to);
  } catch {
    /* ignore */
  }
}

function lastOrFirst(group: NavGroup): string {
  if (!group.children || group.children.length === 0) return group.to ?? "/";
  try {
    const last = sessionStorage.getItem(LAST_CHILD_KEY + group.id);
    if (last && group.children.some((child) => child.to === last)) return last;
  } catch {
    /* ignore */
  }
  return group.children[0].to;
}

function NavMenu({
  groups,
  activeId,
  pathname,
  onLeafClick
}: {
  groups: NavGroup[];
  activeId: string | null;
  pathname: string;
  search: string;
  onLeafClick?: () => void;
}) {
  const { t } = useTranslation();

  return (
    <nav className="app-nav__list" aria-label={t("nav.label")}>
      {groups.map((group) => {
        const selected = group.id === activeId;
        if (group.children && group.children.length > 0) {
          return (
            <Link
              key={group.id}
              to={lastOrFirst(group)}
              className={selected ? "app-nav__leaf app-nav__top active" : "app-nav__leaf app-nav__top"}
              aria-current={selected ? "page" : undefined}
              onClick={onLeafClick}
            >
              {t(group.labelKey)}
            </Link>
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

function SubnavBar({
  group,
  pathname,
  search
}: {
  group: NavGroup | null;
  pathname: string;
  search: string;
}) {
  const { t } = useTranslation();
  const children = group?.children ?? [];
  return (
    <div className="app-subnav" role="navigation" aria-label={group ? t(group.labelKey) : t("nav.label")}>
      {children.length === 0 ? (
        <span className="app-subnav__title">{group ? t(group.labelKey) : ""}</span>
      ) : (
        children.map((child) => {
          const active = child.isActive(pathname, search);
          return (
            <Link
              key={child.to}
              to={child.to}
              className={active ? "app-subnav__btn is-active" : "app-subnav__btn"}
              aria-current={active ? "page" : undefined}
              onClick={() => group && rememberChild(group.id, child.to)}
            >
              {t(child.labelKey)}
            </Link>
          );
        })
      )}
    </div>
  );
}

export function AppShell({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const { session, logout } = useAuth();
  const location = useLocation();
  const drawerTitleId = useId();
  const headerRef = useRef<HTMLDivElement>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);

  const groups = useMemo(
    () => (session ? visibleGroups(session.roles.map((r) => r.name)) : []),
    [session]
  );
  const routeGroupId = activeGroupId(groups, location.pathname, location.search);
  const activeGroup = groups.find((g) => g.id === routeGroupId) ?? null;

  useEffect(() => {
    if (!activeGroup) return;
    const child = activeGroup.children?.find((c) => c.isActive(location.pathname, location.search));
    if (child) rememberChild(activeGroup.id, child.to);
  }, [activeGroup, location.pathname, location.search]);

  useEffect(() => {
    setDrawerOpen(false);
  }, [location.pathname, location.search]);

  useEffect(() => {
    const root = document.getElementById("root");
    document.documentElement.classList.add("app-locked");
    document.body.classList.add("app-locked");
    root?.classList.add("app-locked");
    return () => {
      document.documentElement.classList.remove("app-locked");
      document.body.classList.remove("app-locked");
      root?.classList.remove("app-locked");
    };
  }, []);

  useEffect(() => {
    if (!drawerOpen) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") setDrawerOpen(false);
    };
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("keydown", onKey);
    };
  }, [drawerOpen]);

  useEffect(() => {
    const onResize = () => {
      if (window.innerWidth >= 992) setDrawerOpen(false);
    };
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, []);

  useEffect(() => {
    const el = headerRef.current;
    if (!el) return;
    const apply = () => {
      document.documentElement.style.setProperty("--app-header-h", `${el.offsetHeight}px`);
    };
    apply();
    const ro = new ResizeObserver(apply);
    ro.observe(el);
    return () => ro.disconnect();
  }, [session]);

  if (!session) return null;

  const menuProps = {
    groups,
    activeId: routeGroupId,
    pathname: location.pathname,
    search: location.search
  };

  return (
    <div className="app-shell">
      <div className="app-shell__top" ref={headerRef}>
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
        <div className="app-shell__brand">
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
          <NavMenu {...menuProps} />
        </aside>
        <div className="app-shell__main">
          <SubnavBar group={activeGroup} pathname={location.pathname} search={location.search} />
          <div className="app-shell__content">
            {children}
            <footer className="app-footer">{t("letterhead.sigle")}</footer>
          </div>
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
          {...menuProps}
          onLeafClick={() => setDrawerOpen(false)}
        />
      </aside>
    </div>
  );
}
