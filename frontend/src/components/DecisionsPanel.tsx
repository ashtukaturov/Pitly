import { Scale } from 'lucide-react';
import { formatDate, formatPln } from '../format';
import type { DecisionTopic, Pit38Fields, TaxDecisions } from '../types';
import { useT } from '../i18n';

interface Question {
  topic: DecisionTopic;
  /** Prefix of the translation keys: `${key}.label`, `${key}.hint`, `${key}.${option}`. */
  key: string;
  options: string[];
}

const QUESTIONS: Question[] = [
  { topic: 'dateBasis', key: 'decisions.dateBasis', options: ['SettlementDate', 'TradeDate'] },
  { topic: 'securitiesLending', key: 'decisions.lending', options: ['Unconfirmed', 'Interest', 'OtherSources'] },
  { topic: 'paymentsInLieu', key: 'decisions.inLieu', options: ['Unconfirmed', 'Dividend', 'OtherSources'] },
  { topic: 'crossYearRefunds', key: 'decisions.crossYear', options: ['Unconfirmed', 'CorrectOriginalYear'] },
];

/**
 * The user's answers to the questions no source settles (backend TaxDecisions). The date of a trade is always
 * offered; the other questions appear when a finding of this result needs them or an answer was already given.
 * Changing an answer recalculates the same upload; the chosen answers are printed with the result.
 */
export default function DecisionsPanel({
  pit38,
  decisions,
  onChange,
  busy = false,
  error = null,
}: {
  pit38: Pit38Fields;
  decisions: TaxDecisions;
  onChange?: (decisions: TaxDecisions) => void;
  busy?: boolean;
  error?: string | null;
}) {
  const t = useT();
  const topics = new Set((pit38.diagnostics ?? []).map(d => d.decision).filter(Boolean));
  const shown = QUESTIONS.filter(q => q.topic === 'dateBasis' || topics.has(q.topic) || decisions[q.topic] !== 'Unconfirmed');

  return (
    <div className="print-warning bg-slate-800 border border-slate-700 rounded-xl p-5 text-sm">
      <p className="font-semibold text-white flex items-center gap-2">
        <Scale className="w-4 h-4 text-blue-400" />
        {t('decisions.title')}
      </p>
      <p className="text-slate-400 mt-1">{t('decisions.hint')}</p>
      <div className="mt-4 space-y-4">
        {shown.map(q => {
          const value = decisions[q.topic];
          const unconfirmed = value === 'Unconfirmed';
          return (
            <div key={q.topic} className="grid gap-1 md:grid-cols-[minmax(0,16rem)_minmax(0,1fr)] md:gap-4">
              <label htmlFor={`decision-${q.topic}`} className="text-slate-200 font-medium pt-1.5">{t(`${q.key}.label`)}</label>
              <div>
                {onChange ? (
                  <select
                    id={`decision-${q.topic}`}
                    value={value}
                    disabled={busy}
                    onChange={(e) => onChange({ ...decisions, [q.topic]: e.target.value })}
                    className={`no-print w-full max-w-md bg-slate-900 border rounded-lg px-3 py-1.5 text-sm text-white focus:outline-none focus:border-blue-500 disabled:opacity-60 ${unconfirmed ? 'border-amber-500/60' : 'border-slate-700'}`}
                  >
                    {q.options.map(option => (
                      <option key={option} value={option}>{t(`${q.key}.${option}`)}</option>
                    ))}
                  </select>
                ) : null}
                <p className={`${onChange ? 'hidden print-block' : ''} text-slate-200`}>{t(`${q.key}.${value}`)}</p>
                <p className="text-slate-500 text-xs mt-1">{t(`${q.key}.hint`)}</p>
              </div>
            </div>
          );
        })}
      </div>
      {busy && <p className="no-print text-slate-500 text-xs mt-3">{t('decisions.recalculating')}</p>}
      {error && <p className="no-print text-red-400 text-xs mt-3" role="alert">{error}</p>}
    </div>
  );
}

/**
 * Payments the user classified as income from other sources: not in section G, declared in PIT-36. Listed with
 * their totals (backend Pit38Fields) so that nothing falls between the two returns.
 */
export function OtherSourcesSection({ pit38 }: { pit38: Pit38Fields }) {
  const t = useT();
  const rows = pit38.otherSourcesIncome ?? [];
  if (rows.length === 0) return null;

  return (
    <div className="print-warning bg-slate-800 border border-slate-700 rounded-xl overflow-hidden text-sm">
      <div className="px-6 py-4 border-b border-slate-700">
        <h2 className="text-white font-semibold">{t('otherSources.title')}</h2>
        <p className="text-slate-400 mt-1">{t('otherSources.body')}</p>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full">
          <thead>
            <tr className="border-b border-slate-700">
              <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-left">{t('otherSources.colDate')}</th>
              <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-left">{t('otherSources.colIncome')}</th>
              <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">{t('otherSources.colAmount')}</th>
              <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">{t('otherSources.colPln')}</th>
              <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">{t('otherSources.colTax')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => (
              <tr key={i} className="border-b border-slate-700/50">
                <td className="px-6 py-2 text-slate-300">{formatDate(row.date)}</td>
                <td className="px-6 py-2 text-slate-300">{row.symbol}</td>
                <td className="px-6 py-2 text-right font-mono text-slate-300">{row.amountOriginal.toFixed(2)} {row.currency}</td>
                <td className="px-6 py-2 text-right font-mono text-white">{formatPln(row.amountPln)}</td>
                <td className="px-6 py-2 text-right font-mono text-slate-300">{formatPln(row.withholdingTaxPln)}</td>
              </tr>
            ))}
            <tr className="bg-slate-700/30">
              <td className="px-6 py-3 text-slate-200 font-semibold" colSpan={3}>{t('otherSources.total')}</td>
              <td className="px-6 py-3 text-right font-mono text-white font-semibold">{formatPln(pit38.otherSourcesIncomePln ?? 0)}</td>
              <td className="px-6 py-3 text-right font-mono text-white font-semibold">{formatPln(pit38.otherSourcesWithholdingPln ?? 0)}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  );
}
