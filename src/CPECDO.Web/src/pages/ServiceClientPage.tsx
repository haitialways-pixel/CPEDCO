import { FormEvent, useEffect, useState } from "react";
import { Link, Navigate, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import {
  assignTicket,
  closeTicket,
  fetchMember360,
  fetchStaff,
  openTicket,
  searchMembers,
  type Member360,
  type MemberSummary,
  type StaffSummary
} from "../api/members";
import {
  blockAccount,
  downloadLivretPdf,
  downloadStatementPdf,
  unblockAccount,
  type SavingsAccount
} from "../api/savings";
import { KycPieces } from "../components/KycPieces";
import { MemberAccountsPanel } from "../components/MemberAccountsPanel";
import { formatMoney } from "../money";

function canAccessService(roles: string[]) {
  return roles.some((r) => ["Admin", "Gerant", "ServiceClient"].includes(r));
}

export function ServiceClientPage() {
  const { id } = useParams();
  const { session } = useAuth();
  const roles = session?.roles.map((r) => r.name) ?? [];
  if (!canAccessService(roles)) return <Navigate to="/" replace />;
  return id ? <ServiceDetail id={id} /> : <ServiceSearch />;
}

function ServiceSearch() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [query, setQuery] = useState("");
  const [items, setItems] = useState<MemberSummary[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function runSearch(event?: FormEvent) {
    event?.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await searchMembers(query);
      setItems(result.items);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("members.searchError"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="page">
      <h1>{t("nav.service")}</h1>
      <form className="search-bar" onSubmit={(e) => void runSearch(e)}>
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder={t("members.searchPlaceholder")}
        />
        <button type="submit" disabled={busy}>
          {busy ? t("members.searching") : t("members.search")}
        </button>
      </form>
      {error ? <p className="login-form__error">{error}</p> : null}
      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              <th>{t("members.no")}</th>
              <th>{t("members.name")}</th>
              <th>{t("members.phone")}</th>
              <th>{t("members.statusLabel")}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {items.length === 0 ? (
              <tr>
                <td colSpan={5}>{t("members.empty")}</td>
              </tr>
            ) : (
              items.map((m) => (
                <tr key={m.id}>
                  <td>{m.memberNo}</td>
                  <td>{m.fullName}</td>
                  <td>{m.phone}</td>
                  <td>{t(`members.status.${m.status}`)}</td>
                  <td>
                    <button type="button" className="btn-primary" onClick={() => navigate(`/service-client/${m.id}`)}>
                      {t("service.fiche360")}
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </main>
  );
}

function ServiceDetail({ id }: { id: string }) {
  const { t } = useTranslation();
  const [member, setMember] = useState<Member360 | null>(null);
  const [accounts, setAccounts] = useState<SavingsAccount[]>([]);
  const [staff, setStaff] = useState<StaffSummary[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [blockReason, setBlockReason] = useState("");
  const [pdfFrom, setPdfFrom] = useState("2026-01-01");
  const [pdfTo, setPdfTo] = useState(new Date().toISOString().slice(0, 10));
  const [ticketSubject, setTicketSubject] = useState("");
  const [ticketBody, setTicketBody] = useState("");
  const [ticketAssignee, setTicketAssignee] = useState("");

  useEffect(() => {
    void Promise.all([fetchMember360(id), fetchStaff()])
      .then(([data, staffList]) => {
        setMember(data);
        setAccounts(data.savingsAccounts ?? []);
        setStaff(staffList);
      })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("members.loadError")));
  }, [id, t]);

  if (!member && !error) return <main className="page">{t("members.loading")}</main>;
  if (!member) {
    return (
      <main className="page">
        <p>
          <Link to="/service-client">{t("members.back")}</Link>
        </p>
        <p className="login-form__error">{error}</p>
      </main>
    );
  }

  return (
    <main className="page">
      <p>
        <Link to="/service-client">{t("members.back")}</Link>
      </p>
      <p className="eyebrow">{member.memberNo}</p>
      <h1>{member.fullName}</h1>
      {error ? <p className="login-form__error">{error}</p> : null}
      <KycPieces
        memberId={id}
        documents={member.kycDocuments ?? []}
        onChanged={async () => {
          const next = await fetchMember360(id);
          setMember(next);
          setAccounts(next.savingsAccounts ?? []);
        }}
      />
      <section className="facts">
        <article>
          <span>{t("members.statusLabel")}</span>
          <strong>{t(`members.status.${member.status}`)}</strong>
        </article>
        <article>
          <span>{t("members.phone")}</span>
          <strong>{member.phone}</strong>
        </article>
        <article>
          <span>{t("members.legalStatus")}</span>
          <strong>{t(`members.legal.${member.legalStatus}`)}</strong>
        </article>
        <article>
          <span>{t("members.cin")}</span>
          <strong>{member.cin ?? "—"}</strong>
        </article>
        <article>
          <span>{t("members.nif")}</span>
          <strong>{member.nif ?? "—"}</strong>
        </article>
        <article>
          <span>{t("members.dateOfBirth")}</span>
          <strong>{member.dateOfBirth ?? "—"}</strong>
        </article>
        <article>
          <span>{t("members.placeOfBirth")}</span>
          <strong>{member.placeOfBirth ?? "—"}</strong>
        </article>
        <article>
          <span>{t("members.occupation")}</span>
          <strong>{member.occupation ?? "—"}</strong>
        </article>
        <article>
          <span>{t("members.address")}</span>
          <strong>{member.addressLine}</strong>
        </article>
        <article>
          <span>{t("members.kyc")}</span>
          <strong>{t(`members.kycStatus.${member.kycStatus}`)}</strong>
        </article>
      </section>
      <p className="muted">{t("members.ficheLocked")}</p>
      <MemberAccountsPanel
        member={member}
        canOpen
        canPay
        onMemberUpdated={(next) => {
          setMember(next);
          setAccounts(next.savingsAccounts ?? []);
        }}
      />

      <section className="card-block">
        <h2>{t("service.pdf")}</h2>
        {accounts.map((account) => (
          <div key={account.id} className="search-bar" style={{ marginBottom: "0.6rem" }}>
            <span>
              {account.accountNo} · {formatMoney(account.availableBalance, account.currencyCode)}
            </span>
            <input type="date" value={pdfFrom} onChange={(e) => setPdfFrom(e.target.value)} />
            <input type="date" value={pdfTo} onChange={(e) => setPdfTo(e.target.value)} />
            <button
              type="button"
              onClick={() =>
                void downloadStatementPdf(account.id, pdfFrom, pdfTo).catch((err: unknown) =>
                  setError(err instanceof Error ? err.message : t("service.error"))
                )
              }
            >
              {t("service.pdf")}
            </button>
            <button
              type="button"
              className="btn-ghost"
              onClick={() =>
                void downloadLivretPdf(account.id, pdfFrom, pdfTo)
                  .then(async () => {
                    const next = await fetchMember360(id);
                    setMember(next);
                    setAccounts(next.savingsAccounts ?? []);
                  })
                  .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")))
              }
            >
              {t("savings.livret")}
            </button>
          </div>
        ))}
      </section>

      <section className="card-block">
        <h2>{t("service.block")}</h2>
        {accounts.map((account) => (
          <div key={account.id} className="stack-form" style={{ marginBottom: "0.8rem" }}>
            <p>
              <strong>{account.accountNo}</strong>
              {account.isBlocked ? ` · ${t("service.blocked")}` : ""}
            </p>
            {account.isBlocked ? (
              <button
                type="button"
                className="btn-ghost"
                disabled={busy}
                onClick={() => {
                  setBusy(true);
                  void unblockAccount(account.id)
                    .then((updated) => setAccounts((current) => current.map((a) => (a.id === updated.id ? updated : a))))
                    .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")))
                    .finally(() => setBusy(false));
                }}
              >
                {t("service.unblock")}
              </button>
            ) : (
              <div className="search-bar">
                <input
                  value={blockReason}
                  onChange={(e) => setBlockReason(e.target.value)}
                  placeholder={t("service.blockReason")}
                />
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => {
                    setBusy(true);
                    void blockAccount(account.id, blockReason)
                      .then((updated) => {
                        setAccounts((current) => current.map((a) => (a.id === updated.id ? updated : a)));
                        setBlockReason("");
                      })
                      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")))
                      .finally(() => setBusy(false));
                  }}
                >
                  {t("service.block")}
                </button>
              </div>
            )}
          </div>
        ))}
      </section>

      <section className="card-block">
        <h2>{t("service.tickets")}</h2>
        <form
          className="stack-form"
          onSubmit={(e) => {
            e.preventDefault();
            setBusy(true);
            void openTicket(id, ticketSubject, ticketBody, ticketAssignee || undefined)
              .then((created) => {
                setMember((current) =>
                  current ? { ...current, tickets: [created, ...(current.tickets ?? [])] } : current
                );
                setTicketSubject("");
                setTicketBody("");
              })
              .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")))
              .finally(() => setBusy(false));
          }}
        >
          <label>
            {t("service.subject")}
            <input required value={ticketSubject} onChange={(e) => setTicketSubject(e.target.value)} />
          </label>
          <label>
            {t("service.body")}
            <input value={ticketBody} onChange={(e) => setTicketBody(e.target.value)} />
          </label>
          <label>
            {t("service.assign")}
            <select value={ticketAssignee} onChange={(e) => setTicketAssignee(e.target.value)}>
              <option value="">{t("service.unassigned")}</option>
              {staff.map((person) => (
                <option key={person.id} value={person.id}>
                  {person.fullName}
                </option>
              ))}
            </select>
          </label>
          <button className="btn-primary" type="submit" disabled={busy}>
            {t("service.openTicket")}
          </button>
        </form>
        {(member.tickets ?? []).length === 0 ? (
          <p className="muted">{t("service.noTickets")}</p>
        ) : (
          <ul className="ul-reset" style={{ marginTop: "1rem" }}>
            {member.tickets.map((ticket) => (
              <li key={ticket.id} className="card-block">
                <strong>
                  {ticket.ticketNo} · {ticket.subject}
                </strong>
                <p className="muted">
                  {ticket.status} · {ticket.createdByName}
                  {ticket.assignedToName ? ` → ${ticket.assignedToName}` : ""}
                </p>
                {ticket.status !== "Closed" ? (
                  <div className="actions">
                    <select
                      value={ticket.assignedToUserId ?? ""}
                      onChange={(e) => {
                        const userId = e.target.value;
                        if (!userId) return;
                        void assignTicket(id, ticket.id, userId)
                          .then((updated) =>
                            setMember((current) =>
                              current
                                ? {
                                    ...current,
                                    tickets: current.tickets.map((item) => (item.id === updated.id ? updated : item))
                                  }
                                : current
                            )
                          )
                          .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")));
                      }}
                    >
                      <option value="">{t("service.assign")}</option>
                      {staff.map((person) => (
                        <option key={person.id} value={person.id}>
                          {person.fullName}
                        </option>
                      ))}
                    </select>
                    <button
                      type="button"
                      className="btn-ghost"
                      onClick={() =>
                        void closeTicket(id, ticket.id)
                          .then((updated) =>
                            setMember((current) =>
                              current
                                ? {
                                    ...current,
                                    tickets: current.tickets.map((item) => (item.id === updated.id ? updated : item))
                                  }
                                : current
                            )
                          )
                          .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")))
                      }
                    >
                      {t("service.closeTicket")}
                    </button>
                  </div>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </section>
    </main>
  );
}
