import { useState } from 'react';
import { Routes, Route, Outlet, useNavigate } from 'react-router-dom';
import Sidebar from './components/Sidebar';
import LandingPage from './pages/LandingPage';
import ImportPage from './pages/ImportPage';
import DashboardPage from './pages/DashboardPage';
import TransactionsPage from './pages/TransactionsPage';
import DividendsPage from './pages/DividendsPage';
import Pit38Page from './pages/Pit38Page';
import type { AppState } from './types';

function AppLayout({ year }: { year: number | null }) {
  return (
    <div className="flex min-h-screen">
      <Sidebar year={year} />
      <main className="flex-1 ml-16 p-8">
        <Outlet />
      </main>
    </div>
  );
}

export default function App() {
  const navigate = useNavigate();
  const [state, setState] = useState<AppState>({
    sessionId: null,
    summary: null,
    trades: [],
    dividends: [],
  });

  const handleImportComplete = (data: AppState) => {
    setState(data);
    navigate('/app/dashboard');
  };

  return (
    <Routes>
      <Route path="/" element={<LandingPage />} />
      <Route path="/app" element={<AppLayout year={state.summary?.year ?? null} />}>
        <Route index element={<ImportPage onComplete={handleImportComplete} />} />
        <Route path="dashboard" element={<DashboardPage state={state} />} />
        <Route path="transactions" element={<TransactionsPage state={state} />} />
        <Route path="dividends" element={<DividendsPage state={state} />} />
        <Route path="pit38" element={<Pit38Page state={state} />} />
      </Route>
    </Routes>
  );
}
