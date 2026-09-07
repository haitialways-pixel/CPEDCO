import { FormEvent, useEffect, useState } from "react";
import { Navigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { assignStaffRole, createStaff, disableStaff, fetchStaff, resetStaffPassword, staffRoleNames } from "../api/staff";
import { useAuth } from "../auth/AuthContext";
import type { StaffUser } from "../api/types";

export function StaffPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const [staff, setStaff] = useState<StaffUser[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState({ username: "", fullName: "", email: "", password: "", roleName: "Caissier" });

  useEffect(() => {
    if (!session) return;
    void fetchStaff(session.token)
      .then(setStaff)
      .catch((err) => setError(err instanceof Error ? err.message : t("staff.loadError")));
  }, [session, t]);

  if (!session) return null;
  if (!session.roles.some((r) => r.name === "Admin")) return <Navigate to="/" replace />;

  const token = session.token;

  async function create(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const created = await createStaff(token, form);
      setStaff((current) => [...current, created].sort((a, b) => a.fullName.localeCompare(b.fullName, "fr")));
      setForm({ username: "", fullName: "", email: "", password: "", roleName: "Caissier" });
    } catch (err) {
      setError(err instanceof Error ? err.message : t("staff.createError"));
    }
  }

  async function update(id: string, action: () => Promise<StaffUser>) {
    setError(null);
    try {
      const updated = await action();
      setStaff((current) => current.map((item) => (item.id === id ? updated : item)));
    } catch (err) {
      setError(err instanceof Error ? err.message : t("staff.updateError"));
    }
  }

  return (
    <main className="dashboard">
      <h1>{t("staff.title")}</h1>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}
      <section className="facts">
        <form className="login-form" onSubmit={(event) => void create(event)}>
          <h2>{t("staff.create")}</h2>
          <label>
            {t("staff.username")}
            <input value={form.username} onChange={(event) => setForm({ ...form, username: event.target.value })} required />
          </label>
          <label>
            {t("staff.fullName")}
            <input value={form.fullName} onChange={(event) => setForm({ ...form, fullName: event.target.value })} required />
          </label>
          <label>
            {t("staff.email")}
            <input type="email" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} required />
          </label>
          <label>
            {t("staff.password")}
            <input
              type="password"
              minLength={8}
              value={form.password}
              onChange={(event) => setForm({ ...form, password: event.target.value })}
              required
            />
          </label>
          <label>
            {t("staff.role")}
            <select value={form.roleName} onChange={(event) => setForm({ ...form, roleName: event.target.value })}>
              {staffRoleNames.map((role) => (
                <option key={role} value={role}>
                  {t(`roles.${role}`)}
                </option>
              ))}
            </select>
          </label>
          <button type="submit">{t("staff.createSubmit")}</button>
        </form>
      </section>
      <section className="coming-soon">
        <h2>{t("staff.list")}</h2>
        {staff.length === 0 ? <p>{t("staff.empty")}</p> : null}
        {staff.map((user) => (
          <article key={user.id} className="staff-row">
            <div>
              <strong>{user.fullName}</strong>
              <span>
                {user.username} · {user.email}
              </span>
              <span>
                {user.isActive ? t("staff.active") : t("staff.disabled")}
                {user.mustChangePassword ? ` · ${t("staff.mustChange")}` : ""}
                {user.roles[0] ? ` · ${t(`roles.${user.roles[0].name}`)}` : ""}
              </span>
            </div>
            <div>
              <select
                value={user.roles[0]?.name ?? ""}
                onChange={(event) => void update(user.id, () => assignStaffRole(token, user.id, event.target.value))}
                aria-label={t("staff.role")}
              >
                {staffRoleNames.map((role) => (
                  <option key={role} value={role}>
                    {t(`roles.${role}`)}
                  </option>
                ))}
              </select>
              {user.isActive ? (
                <button type="button" className="btn-ghost" onClick={() => void update(user.id, () => disableStaff(token, user.id))}>
                  {t("staff.disable")}
                </button>
              ) : null}
              <button
                type="button"
                className="btn-ghost"
                onClick={() => {
                  const password = window.prompt(t("staff.resetPrompt"));
                  if (password) void update(user.id, () => resetStaffPassword(token, user.id, password));
                }}
              >
                {t("staff.resetPassword")}
              </button>
            </div>
          </article>
        ))}
      </section>
    </main>
  );
}
