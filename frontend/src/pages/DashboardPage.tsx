import { useMemo, useState } from 'react';
import { TrendingUp, Coins, Landmark, CreditCard, AlertTriangle } from 'lucide-react';
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, PieChart, Pie, Cell, Legend } from 'recharts';
import { formatPln, numColor, formatTaxPeriod, hasCustomTaxPeriod } from '../format';
import type { AppState } from '../types';
import EmptyState from '../components/EmptyState';
import { useT } from '../i18n';

export default function DashboardPage({
  state,
  onRecalculate,
  switchingYear = false,
  yearError = null,
}: {
  state: AppState;
  onRecalculate?: (options: { year?: number; residencyStartDate?: string }) => void;
  switchingYear?: boolean;
  yearError?: string | null;
}) {
  const t = useT();
  const { summary, trades } = state;
  const [residencyDraft, setResidencyDraft] = useState(state.residencyStartDate ?? '');
  const residencyChanged = (residencyDraft || undefined) !== (state.residencyStartDate || undefined);

  const monthlyData = useMemo(() => {
    const months: Record<string, { proceeds: number; costs: number }> = {};
    for (const trade of trades) {
      if (trade.type !== 'Sell') continue;
      const d = new Date(trade.dateTime);
      const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
      if (!months[key]) months[key] = { proceeds: 0, costs: 0 };
      months[key].proceeds += trade.proceedsPln;
      months[key].costs += trade.costPln;
    }
    return Object.entries(months)
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([month, data]) => ({
        month,
        proceeds: Math.round(data.proceeds),
        costs: Math.round(data.costs),
      }));
  }, [trades]);

  if (!state.sessionId || !summary) {
    return <EmptyState />;
  }

  const totalTaxOwed = summary.capitalGainTaxPln + summary.dividendTaxOwedPln;
  const customTaxPeriod = hasCustomTaxPeriod(summary.year, summary.taxableFrom);

  const pieData = [
    { name: t('dashboard.chart.capitalGainsTax'), value: Math.round(summary.capitalGainTaxPln * 100) / 100 },
    { name: t('dashboard.chart.dividendTax'), value: Math.round(summary.dividendTaxOwedPln * 100) / 100 },
  ].filter(d => d.value > 0);

  const PIE_COLORS = ['#3b82f6', '#f59e0b'];

  const paymentCount = state.dividends.length;
  const warningCount = summary.warnings?.length ?? 0;

  const cards = [
    {
      label: t('dashboard.card.capitalGains'),
      value: summary.capitalGainPln,
      sub: t('dashboard.card.capitalGainsSub', { tax: formatPln(summary.capitalGainTaxPln) }),
      icon: TrendingUp,
      color: numColor(summary.capitalGainPln),
    },
    {
      label: t('dashboard.card.dividends'),
      value: summary.totalDividendsPln,
      sub: paymentCount === 1
        ? t('dashboard.card.paymentsOne', { count: paymentCount })
        : t('dashboard.card.paymentsMany', { count: paymentCount }),
      icon: Coins,
      color: 'text-blue-400',
    },
    {
      label: t('dashboard.card.foreignTaxCredited'),
      value: summary.totalCreditableWithholdingPln,
      sub: summary.totalWithholdingPln > summary.totalCreditableWithholdingPln + 0.005
        ? t('dashboard.card.withheldCapped', { amount: formatPln(summary.totalWithholdingPln) })
        : t('dashboard.card.withholdingCredited'),
      icon: Landmark,
      color: 'text-amber-400',
    },
    {
      label: t('dashboard.card.taxOwed'),
      value: totalTaxOwed,
      sub: t('dashboard.card.dueBy', { year: summary.year + 1 }),
      icon: CreditCard,
      color: 'text-white',
      highlight: true,
    },
  ];

  return (
    <div className="space-y-8">
      <div>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-white text-2xl font-bold">{t('dashboard.title', { year: summary.year })}</h1>
          {state.availableYears.length > 1 && onRecalculate && (
            <label className="no-print flex items-center gap-2 text-sm text-slate-400">
              <span>{t('dashboard.switchYear')}</span>
              <select
                value={summary.year}
                disabled={switchingYear}
                onChange={(e) => onRecalculate({ year: Number(e.target.value) })}
                className="bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-white focus:outline-none focus:border-blue-500 disabled:opacity-60"
                aria-label={t('dashboard.taxYearAria')}
              >
                {state.availableYears.map((y) => (
                  <option key={y} value={y}>{y}</option>
                ))}
              </select>
            </label>
          )}
          {onRecalculate && state.files.length > 0 && (
            <form
              className="no-print flex items-center gap-2 text-sm text-slate-400"
              onSubmit={(e) => { e.preventDefault(); onRecalculate({ residencyStartDate: residencyDraft || undefined }); }}
            >
              <span>{t('dashboard.residencyFrom')}</span>
              <input
                type="date"
                value={residencyDraft}
                disabled={switchingYear}
                onChange={(e) => setResidencyDraft(e.target.value)}
                className="bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-white focus:outline-none focus:border-blue-500 disabled:opacity-60"
                aria-label={t('dashboard.residencyAria')}
              />
              <button
                type="submit"
                disabled={switchingYear || !residencyChanged}
                className="bg-blue-600 hover:bg-blue-500 disabled:opacity-40 disabled:hover:bg-blue-600 text-white px-3 py-1.5 rounded-lg transition-colors"
              >
                {t('dashboard.recalculate')}
              </button>
            </form>
          )}
          {switchingYear && <span className="text-slate-500 text-sm">{t('dashboard.recalculating')}</span>}
        </div>
        {yearError && (
          <p className="text-red-400 text-sm mt-2">{yearError}</p>
        )}
        <p className="text-slate-400 text-sm mt-1">
          {t('dashboard.taxablePeriod', { period: formatTaxPeriod(summary.taxableFrom, summary.taxableTo) })}
        </p>
        {state.availableYears.length > 1 && (
          <p className="text-slate-500 text-sm mt-1">
            {t('dashboard.statementsUploaded', { years: state.availableYears.join(', ') })}
          </p>
        )}
        {customTaxPeriod && (
          <p className="text-slate-500 text-sm mt-1">
            {t('dashboard.customPeriodNote')}
          </p>
        )}
      </div>

      {warningCount > 0 && (
        <div className="bg-amber-500/10 border border-amber-500/30 rounded-xl p-5 text-sm text-amber-200">
          <p className="font-semibold text-amber-100 mb-2 flex items-center gap-2">
            <AlertTriangle className="w-4 h-4 text-amber-300" />
            {warningCount === 1
              ? t('dashboard.reviewOne', { count: warningCount })
              : t('dashboard.reviewMany', { count: warningCount })}
          </p>
          <ul className="space-y-1.5 list-disc list-inside">
            {summary.warnings.map((warning, i) => (
              <li key={i}>{warning}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-4">
        {cards.map((c) => (
          <div
            key={c.label}
            className={`bg-slate-800 border rounded-xl p-6 ${
              c.highlight ? 'border-blue-500 bg-blue-500/5' : 'border-slate-700'
            }`}
          >
            <div className="flex items-center justify-between mb-3">
              <span className="text-slate-400 text-sm">{c.label}</span>
              <c.icon className={`w-5 h-5 ${c.color}`} />
            </div>
            <p className={`text-2xl font-mono tabular-nums font-semibold ${c.color}`}>
              {formatPln(c.value)}
            </p>
            <p className="text-slate-500 text-xs mt-1">{c.sub}</p>
          </div>
        ))}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="bg-slate-800 border border-slate-700 rounded-xl p-6">
          <h2 className="text-white text-lg font-semibold mb-4">{t('dashboard.chart.monthly')}</h2>
          {monthlyData.length > 0 ? (
            <ResponsiveContainer width="100%" height={300}>
              <BarChart data={monthlyData}>
                <XAxis dataKey="month" tick={{ fill: '#94a3b8', fontSize: 12 }} />
                <YAxis tick={{ fill: '#94a3b8', fontSize: 12 }} />
                <Tooltip
                  contentStyle={{ background: '#1e293b', border: '1px solid #334155', borderRadius: 8 }}
                  labelStyle={{ color: '#e2e8f0' }}
                />
                <Bar dataKey="proceeds" name={t('dashboard.chart.proceeds')} fill="#3b82f6" radius={[4, 4, 0, 0]} />
                <Bar dataKey="costs" name={t('dashboard.chart.costs')} fill="#64748b" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          ) : (
            <p className="text-slate-500 text-center py-12">{t('dashboard.chart.noSells')}</p>
          )}
        </div>

        <div className="bg-slate-800 border border-slate-700 rounded-xl p-6">
          <h2 className="text-white text-lg font-semibold mb-4">{t('dashboard.chart.taxBreakdown')}</h2>
          {pieData.length > 0 ? (
            <ResponsiveContainer width="100%" height={300}>
              <PieChart>
                <Pie
                  data={pieData}
                  cx="50%"
                  cy="50%"
                  innerRadius={60}
                  outerRadius={100}
                  dataKey="value"
                  label={({ name, value }) => t('dashboard.chart.pieLabel', { name: String(name), value: String(value) })}
                >
                  {pieData.map((_, i) => (
                    <Cell key={i} fill={PIE_COLORS[i % PIE_COLORS.length]} />
                  ))}
                </Pie>
                <Legend />
                <Tooltip
                  contentStyle={{ background: '#1e293b', border: '1px solid #334155', borderRadius: 8 }}
                />
              </PieChart>
            </ResponsiveContainer>
          ) : (
            <p className="text-slate-500 text-center py-12">{t('dashboard.chart.noTax')}</p>
          )}
        </div>
      </div>
    </div>
  );
}
