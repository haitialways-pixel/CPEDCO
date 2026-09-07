import { FormEvent, useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { searchMembers, type MemberSummary } from "../api/members";

export function MemberSearchPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const navigate = useNavigate();
  const canWrite = session?.roles.some((r) => !r.isReadOnly) ?? false;
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("");
  const [items, setItems] = useState<MemberSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function runSearch(q = query, st = status) {
    setBusy(true);
    setError(null);
    try {
      const result = await searchMembers(q, st || undefined);
      setItems(result.items);
      setTotal(result.total);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("members.searchError"));
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => {
    void runSearch("", "");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    void runSearch();
  }

  return (
    <main className="page">
      <div className="page__head">
        <h1>{t("members.title")}</h1>
        {canWrite ? (
          <Link className="btn-primary" to="/members/new">
            {t("members.create")}
          </Link>
        ) : (
          <p className="readonly-hint">{t("members.readOnly")}</p>
        )}
      </div>
      <form className="search-bar" onSubmit={onSubmit}>
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder={t("members.searchPlaceholder")}
        />
        <select value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">{t("members.statusAny")}</option>
          <option value="Pending">{t("members.status.Pending")}</option>
          <option value="Active">{t("members.status.Active")}</option>
          <option value="Suspended">{t("members.status.Suspended")}</option>
          <option value="Closed">{t("members.status.Closed")}</option>
        </select>
        <button type="submit" disabled={busy}>
          {busy ? t("members.searching") : t("members.search")}
        </button>
      </form>
      {error ? <p className="login-form__error">{error}</p> : null}
      <p className="muted">{t("members.count", { count: total })}</p>
      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              <th>{t("members.no")}</th>
              <th>{t("members.name")}</th>
              <th>{t("members.phone")}</th>
              <th>{t("members.city")}</th>
              <th>{t("members.statusLabel")}</th>
              <th>{t("members.legalStatus")}</th>
              <th>{t("members.votes")}</th>
            </tr>
          </thead>
          <tbody>
            {items.length === 0 ? (
              <tr>
                <td colSpan={7}>{t("members.empty")}</td>
              </tr>
            ) : (
              items.map((m) => (
                <tr key={m.id} onClick={() => navigate(`/members/${m.id}`)}>
                  <td>{m.memberNo}</td>
                  <td>{m.fullName}</td>
                  <td>{m.phone}</td>
                  <td>{m.city}</td>
                  <td>{t(`members.status.${m.status}`)}</td>
                  <td>{t(`members.legal.${m.legalStatus}`)}</td>
                  <td>{m.votingRights ? t("members.voteYes") : t("members.voteNo")}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </main>
  );
}
