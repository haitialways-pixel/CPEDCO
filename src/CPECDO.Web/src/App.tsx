import { Navigate, Route, Routes } from "react-router-dom";
import { useAuth } from "./auth/AuthContext";
import { AppShell } from "./components/AppShell";
import { DashboardPage } from "./pages/DashboardPage";
import { LoginPage } from "./pages/LoginPage";
import { MemberCreatePage } from "./pages/MemberCreatePage";
import { Member360Page } from "./pages/Member360Page";
import { MemberSearchPage } from "./pages/MemberSearchPage";
import { SavingsStatementPage } from "./pages/SavingsStatementPage";
import { TellerPage } from "./pages/TellerPage";
import { ChangePasswordPage } from "./pages/ChangePasswordPage";
import { StaffPage } from "./pages/StaffPage";
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
import type { ReactNode } from "react";

function Protected({ children }: { children: ReactNode }) {
  const { ready, session } = useAuth();
  if (!ready) return <div className="boot">CPCREDO</div>;
  if (!session) return <Navigate to="/login" replace />;
  if (session.user.mustChangePassword) return <ChangePasswordPage />;
  return <AppShell>{children}</AppShell>;
}

export function App() {
  const { ready, session } = useAuth();

  if (!ready) {
    return <div className="boot">CPCREDO</div>;
  }

  return (
    <Routes>
      <Route path="/login" element={session ? <Navigate to="/" replace /> : <LoginPage />} />
      <Route
        path="/"
        element={
          <Protected>
            <DashboardPage />
          </Protected>
        }
      />
      <Route
        path="/members"
        element={
          <Protected>
            <MemberSearchPage />
          </Protected>
        }
      />
      <Route
        path="/members/new"
        element={
          <Protected>
            <MemberCreatePage />
          </Protected>
        }
      />
      <Route
        path="/members/:id"
        element={
          <Protected>
            <Member360Page />
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
        path="/caisse/paiement-credit"
        element={
          <Protected>
            <TellerCreditPage />
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
