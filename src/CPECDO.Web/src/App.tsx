import { Navigate, Route, Routes, useParams } from "react-router-dom";
import { useAuth } from "./auth/AuthContext";
import { AppShell } from "./components/AppShell";
import { DashboardPage } from "./pages/DashboardPage";
import { LoginPage } from "./pages/LoginPage";
import { MemberCreatePage } from "./pages/MemberCreatePage";
import { MembersRegisterPage } from "./pages/MembersRegisterPage";
import { ServiceClientPage } from "./pages/ServiceClientPage";
import { SavingsStatementPage } from "./pages/SavingsStatementPage";
import { TellerPage } from "./pages/TellerPage";
import { ChangePasswordPage } from "./pages/ChangePasswordPage";
import { MfaPage } from "./pages/MfaPage";
import { StaffPage } from "./pages/StaffPage";
import { BackupPage } from "./pages/BackupPage";
import { ReportsPage } from "./pages/ReportsPage";
import { TreasuryBanksPage } from "./pages/TreasuryBanksPage";
import { TreasuryMovementsPage } from "./pages/TreasuryMovementsPage";
import { TreasuryMovementNewPage } from "./pages/TreasuryMovementNewPage";
import { TreasuryMovementDetailPage } from "./pages/TreasuryMovementDetailPage";
import { LoanProductsPage } from "./pages/LoanProductsPage";
import { LoansPage } from "./pages/LoansPage";
import { LoanNewPage } from "./pages/LoanNewPage";
import { LoanDetailPage } from "./pages/LoanDetailPage";
import { CollectionSheetPage } from "./pages/CollectionSheetPage";
import { TellerCreditPage } from "./pages/TellerCreditPage";
import { InternalMovementPage } from "./pages/InternalMovementPage";
import type { ReactNode } from "react";

function LegacyMemberRedirect() {
  const { id } = useParams();
  return <Navigate to={`/membres/${id}`} replace />;
}

function Protected({ children }: { children: ReactNode }) {
  const { ready, session } = useAuth();
  if (!ready) return <div className="boot">CPCREDO</div>;
  if (!session) return <Navigate to="/login" replace />;
  if (session.user.mustChangePassword) return <Navigate to="/changer-mot-de-passe" replace />;
  return <AppShell>{children}</AppShell>;
}

export function App() {
  const { ready, session, mfaChallenge } = useAuth();

  if (!ready) {
    return <div className="boot">CPCREDO</div>;
  }

  return (
    <Routes>
      <Route
        path="/login"
        element={
          session ? (
            session.user.mustChangePassword ? (
              <Navigate to="/changer-mot-de-passe" replace />
            ) : (
              <Navigate to="/" replace />
            )
          ) : mfaChallenge ? (
            <Navigate to="/mfa" replace />
          ) : (
            <LoginPage />
          )
        }
      />
      <Route
        path="/mfa"
        element={session ? <Navigate to="/" replace /> : mfaChallenge ? <MfaPage /> : <Navigate to="/login" replace />}
      />
      <Route
        path="/changer-mot-de-passe"
        element={
          !session ? (
            <Navigate to="/login" replace />
          ) : session.user.mustChangePassword ? (
            <ChangePasswordPage />
          ) : (
            <Navigate to="/" replace />
          )
        }
      />
      <Route
        path="/"
        element={
          <Protected>
            <DashboardPage />
          </Protected>
        }
      />
      <Route path="/members" element={<Navigate to="/membres" replace />} />
      <Route path="/members/new" element={<Navigate to="/membres/nouveau" replace />} />
      <Route path="/members/:id" element={<LegacyMemberRedirect />} />
      <Route
        path="/membres"
        element={
          <Protected>
            <MembersRegisterPage />
          </Protected>
        }
      />
      <Route
        path="/membres/nouveau"
        element={
          <Protected>
            <MemberCreatePage />
          </Protected>
        }
      />
      <Route
        path="/membres/:id"
        element={
          <Protected>
            <MembersRegisterPage />
          </Protected>
        }
      />
      <Route
        path="/service-client"
        element={
          <Protected>
            <ServiceClientPage />
          </Protected>
        }
      />
      <Route
        path="/service-client/:id"
        element={
          <Protected>
            <ServiceClientPage />
          </Protected>
        }
      />
      <Route
        path="/teller"
        element={
          <Protected>
            <TellerPage />
          </Protected>
        }
      />
      <Route
        path="/caisse/decaissement-credit"
        element={
          <Protected>
            <TellerCreditPage />
          </Protected>
        }
      />
      <Route
        path="/caisse/paiement-credit"
        element={
          <Protected>
            <TellerCreditPage />
          </Protected>
        }
      />
      <Route
        path="/caisse/mouvement-interne"
        element={
          <Protected>
            <InternalMovementPage />
          </Protected>
        }
      />
      <Route
        path="/staff"
        element={
          <Protected>
            <StaffPage />
          </Protected>
        }
      />
      <Route
        path="/staff/sauvegarde"
        element={
          <Protected>
            <BackupPage />
          </Protected>
        }
      />
      <Route
        path="/reports"
        element={
          <Protected>
            <ReportsPage />
          </Protected>
        }
      />
      <Route
        path="/treasury/banks"
        element={
          <Protected>
            <TreasuryBanksPage />
          </Protected>
        }
      />
      <Route
        path="/tresorerie/mouvements/nouveau"
        element={
          <Protected>
            <TreasuryMovementNewPage />
          </Protected>
        }
      />
      <Route
        path="/tresorerie/mouvements/:id"
        element={
          <Protected>
            <TreasuryMovementDetailPage />
          </Protected>
        }
      />
      <Route
        path="/tresorerie/mouvements"
        element={
          <Protected>
            <TreasuryMovementsPage />
          </Protected>
        }
      />
      <Route path="/treasury/transfers" element={<Navigate to="/tresorerie/mouvements" replace />} />
      <Route
        path="/savings-accounts/:id"
        element={
          <Protected>
            <SavingsStatementPage />
          </Protected>
        }
      />
      <Route
        path="/credit/produits"
        element={
          <Protected>
            <LoanProductsPage />
          </Protected>
        }
      />
      <Route
        path="/credit/prets/nouveau"
        element={
          <Protected>
            <LoanNewPage />
          </Protected>
        }
      />
      <Route
        path="/credit/prets/:id"
        element={
          <Protected>
            <LoanDetailPage />
          </Protected>
        }
      />
      <Route
        path="/credit/recouvrement"
        element={
          <Protected>
            <CollectionSheetPage />
          </Protected>
        }
      />
      <Route
        path="/credit/prets"
        element={
          <Protected>
            <LoansPage />
          </Protected>
        }
      />
      <Route path="*" element={<Navigate to={session ? "/" : "/login"} replace />} />
    </Routes>
  );
}
