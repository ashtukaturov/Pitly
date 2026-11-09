import { useState } from 'react';
import { Routes, Route, Outlet, useNavigate } from 'react-router-dom';
import Sidebar from './components/Sidebar';
import LandingPage from './pages/LandingPage';
import ImportPage from './pages/ImportPage';
import DashboardPage from './pages/DashboardPage';
import TransactionsPage from './pages/TransactionsPage';
import DividendsPage from './pages/DividendsPage';
import Pit38Page from './pages/Pit38Page';
import { importFiles } from './api';
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
    pit38: null,
    trades: [],
    dividends: [],
    files: [],
    availableYears: [],
    priorYearLosses: [],
  });
  const [switchingYear, setSwitchingYear] = useState(false);
  const [yearError, setYearError] = useState<string | null>(null);

  const handleStartOver = () => {
    setState({
      sessionId: null,
      summary: null,
      pit38: null,
      trades: [],
      dividends: [],
      files: [],
      availableYears: [],
      priorYearLosses: [],
    });
    setYearError(null);
  };

  const handleImportComplete = (data: AppState) => {
    setState(data);
    setYearError(null);
    navigate('/app/dashboard');
  };

  // Recalculate the same upload for another tax year or residency start date; the files stay in memory.
  const handleRecalculate = async (options: { year?: number; residencyStartDate?: string }) => {
    if (state.files.length === 0) return;
    const year = options.year ?? state.summary?.year;
    const residencyStartDate = 'residencyStartDate' in options ? options.residencyStartDate : state.residencyStartDate;
    setSwitchingYear(true);
    setYearError(null);
    try {
      const result = await importFiles(state.files, residencyStartDate, state.priorYearLosses, year);
      setState(prev => ({
        ...prev,
        sessionId: result.sessionId,
        summary: result.summary,
        pit38: result.pit38,
        trades: result.trades,
        dividends: result.dividends,
        availableYears: result.availableYears,
        residencyStartDate,
      }));
    } catch (err: unknown) {
      setYearError(err instanceof Error ? err.message : 'Could not switch the tax year');
    } finally {
      setSwitchingYear(false);
    }
  };

  return (
    <Routes>
      <Route path="/" element={<LandingPage />} />
      <Route path="/app" element={<AppLayout year={state.summary?.year ?? null} />}>
        <Route
          index
          element={<ImportPage onComplete={handleImportComplete} existingFiles={state.files} onStartOver={handleStartOver} />}
        />
        <Route
          path="dashboard"
          element={<DashboardPage state={state} onRecalculate={handleRecalculate} switchingYear={switchingYear} yearError={yearError} />}
        />
        <Route path="transactions" element={<TransactionsPage state={state} />} />
        <Route path="dividends" element={<DividendsPage state={state} />} />
        <Route path="pit38" element={<Pit38Page state={state} />} />
      </Route>
    </Routes>
  );
}
