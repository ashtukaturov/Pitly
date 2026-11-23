import { useState, useMemo } from 'react';
import { Info, Search } from 'lucide-react';
import { formatPln, formatRate, formatDate, formatTaxPeriod, hasCustomTaxPeriod } from '../format';
import type { AppState, WithholdingEvent } from '../types';
import EmptyState from '../components/EmptyState';
import { useT } from '../i18n';
import { formRefs } from '../pit38Form';

const PL_TAX_RATE = 0.19;

const KIND_KEYS: Record<string, string> = {
  Dividend: 'dividends.kind.dividend',
  PaymentInLieu: 'dividends.kind.paymentInLieu',
  Interest: 'dividends.kind.interest',
};

const KIND_STYLES: Record<string, string> = {
  Dividend: 'bg-blue-500/15 text-blue-300',
  PaymentInLieu: 'bg-amber-500/15 text-amber-300',
  Interest: 'bg-emerald-500/15 text-emerald-300',
};

export default function DividendsPage({ state }: { state: AppState }) {
  const t = useT();
  const [symbolFilter, setSymbolFilter] = useState('');

  const filtered = useMemo(() => {
    if (!symbolFilter) return state.dividends;
    return state.dividends.filter(d => d.symbol.toLowerCase().includes(symbolFilter.toLowerCase()));
  }, [state.dividends, symbolFilter]);

  const totals = useMemo(() => {
    let amountPln = 0, withholdingPln = 0, creditablePln = 0, plTax = 0, netOwed = 0;
    for (const d of filtered) {
      const tax = d.amountPln * PL_TAX_RATE;
      amountPln += d.amountPln;
      withholdingPln += d.withholdingTaxPln;
      creditablePln += d.creditableWithholdingPln;
      plTax += tax;
      netOwed += Math.max(tax - d.creditableWithholdingPln, 0);
    }
    return { amountPln, withholdingPln, creditablePln, plTax, netOwed };
  }, [filtered]);

  const hasCappedWithholding = filtered.some(d => d.withholdingTaxPln > d.creditableWithholdingPln + 0.005);

  if (!state.sessionId || !state.summary) {
    return <EmptyState />;
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-white text-2xl font-bold">{t('dividends.title')}</h1>
        <p className="text-slate-400 text-sm mt-1">
          {t('dividends.showing', { period: formatTaxPeriod(state.summary.taxableFrom, state.summary.taxableTo) })}
        </p>
        {hasCustomTaxPeriod(state.summary.year, state.summary.taxableFrom) && (
          <p className="text-slate-500 text-sm mt-1">
            {t('dividends.residencyNote')}
          </p>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-4">
        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
          <input
            type="text"
            placeholder={t('dividends.filterPlaceholder')}
            value={symbolFilter}
            onChange={(e) => setSymbolFilter(e.target.value)}
            className="bg-slate-800 border border-slate-700 rounded-lg pl-9 pr-4 py-2 text-sm text-white placeholder:text-slate-500 focus:outline-none focus:border-blue-500"
          />
        </div>
        <span className="text-slate-500 text-sm ml-auto">
          {filtered.length === 1
            ? t('dividends.countOne', { count: filtered.length })
            : t('dividends.countMany', { count: filtered.length })}
        </span>
      </div>

      <div className="flex items-start gap-3 bg-blue-500/10 border border-blue-500/30 rounded-xl p-4">
        <Info className="w-5 h-5 text-blue-400 shrink-0 mt-0.5" />
        <div className="text-sm text-blue-300">
          <p>{t('dividends.info1')}</p>
          <p className="mt-1">{t('dividends.info2Before')}<strong>{t('dividends.info2Rate')}</strong>{t('dividends.info2After')}</p>
          <p className="mt-1">{t('dividends.info3', { zrycz: formRefs(state.pit38?.form).zrycz })}</p>
        </div>
      </div>

      {hasCappedWithholding && (
        <div className="bg-amber-500/10 border border-amber-500/30 rounded-xl p-4 text-sm text-amber-200">
          {t('dividends.cappedWarning')}
        </div>
      )}

      <div className="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-700">
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-left">{t('dividends.col.date')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-left">{t('dividends.col.symbol')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-left">{t('dividends.col.type')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('dividends.col.amountUsd')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('dividends.col.rate')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('dividends.col.amountPln')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('dividends.col.withheldPln')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('dividends.col.creditablePln')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('dividends.col.plTax')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('dividends.col.netTaxOwed')}</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((d, i) => {
                const plTax = d.amountPln * PL_TAX_RATE;
                const netOwed = Math.max(plTax - d.creditableWithholdingPln, 0);
                const capped = d.withholdingTaxPln > d.creditableWithholdingPln + 0.005;
                const kindKey = KIND_KEYS[d.kind];
                return (
                  <tr
                    key={i}
                    className={`border-b border-slate-700/50 hover:bg-slate-700/30 transition-colors ${d.rateUnavailable ? 'bg-amber-500/5' : ''}`}
                  >
                    <td className="px-3 py-2.5 text-slate-300 whitespace-nowrap">{formatDate(d.date)}</td>
                    <td className="px-3 py-2.5 text-white font-medium">{d.symbol}</td>
                    <td className="px-3 py-2.5">
                      <span className={`inline-block rounded px-1.5 py-0.5 text-[11px] font-medium ${KIND_STYLES[d.kind] ?? KIND_STYLES.Dividend}`}>
                        {kindKey ? t(kindKey) : d.kind}
                      </span>
                    </td>
                    <td className="px-3 py-2.5 text-right font-mono tabular-nums text-slate-300 whitespace-nowrap">{d.amountOriginal.toFixed(2)} {d.currency}</td>
                    <td className="px-3 py-2.5 text-right font-mono tabular-nums text-slate-400">{formatRate(d.exchangeRate)}</td>
                    <td className="px-3 py-2.5 text-right font-mono tabular-nums text-blue-400">{formatPln(d.amountPln)}</td>
                    <td
                      className="px-3 py-2.5 text-right font-mono tabular-nums text-red-400"
                      title={d.withholdingSource ? t(`events.withholdingSource.${d.withholdingSource}`) : undefined}
                    >
                      {formatPln(d.withholdingTaxPln)}
                    </td>
                    <td
                      className={`px-3 py-2.5 text-right font-mono tabular-nums ${capped ? 'text-amber-400' : 'text-red-400'}`}
                      title={d.maxCreditableRate != null
                        ? t('dividends.treatyCap', { rate: Math.round(d.maxCreditableRate * 100) })
                        : t('dividends.statutoryCap')}
                    >
                      {formatPln(d.creditableWithholdingPln)}
                    </td>
                    <td className="px-3 py-2.5 text-right font-mono tabular-nums text-slate-300">{formatPln(plTax)}</td>
                    <td className="px-3 py-2.5 text-right font-mono tabular-nums text-amber-400">{formatPln(netOwed)}</td>
                  </tr>
                );
              })}
            </tbody>
            <tfoot>
              <tr className="border-t border-slate-600 bg-slate-800/80">
                <td colSpan={5} className="px-3 py-3 text-slate-300 font-medium text-sm">{t('dividends.totals')}</td>
                <td className="px-3 py-3 text-right font-mono tabular-nums text-blue-400 text-sm">{formatPln(totals.amountPln)}</td>
                <td className="px-3 py-3 text-right font-mono tabular-nums text-red-400 text-sm">{formatPln(totals.withholdingPln)}</td>
                <td className="px-3 py-3 text-right font-mono tabular-nums text-red-400 text-sm">{formatPln(totals.creditablePln)}</td>
                <td className="px-3 py-3 text-right font-mono tabular-nums text-slate-300 text-sm">{formatPln(totals.plTax)}</td>
                <td className="px-3 py-3 text-right font-mono tabular-nums text-amber-400 text-sm">{formatPln(totals.netOwed)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      </div>

      <WithholdingEvents events={state.withholdingEvents} />
    </div>
  );
}

const STATUS_STYLES: Record<WithholdingEvent['status'], string> = {
  Matched: 'bg-emerald-500/15 text-emerald-300',
  Shared: 'bg-blue-500/15 text-blue-300',
  Unresolved: 'bg-amber-500/15 text-amber-300',
};

// Every withholding event of the year as the statements report it and what the calculation did with it
// (backend WithholdingEvent). This is the audit trail behind the "Withheld" column above.
function WithholdingEvents({ events }: { events: WithholdingEvent[] }) {
  const t = useT();
  const th = 'text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3';
  return (
    <div className="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden">
      <div className="px-6 py-4 border-b border-slate-700">
        <h2 className="text-white text-lg font-semibold">{t('events.title')}</h2>
        <p className="text-slate-400 text-sm mt-1">{t('events.intro')}</p>
      </div>
      {events.length === 0 ? (
        <p className="px-6 py-4 text-slate-500 text-sm">{t('events.none')}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-700">
                <th className={`${th} text-left`}>{t('events.col.date')}</th>
                <th className={`${th} text-left`}>{t('events.col.instrument')}</th>
                <th className={`${th} text-right`}>{t('events.col.amount')}</th>
                <th className={`${th} text-left`}>{t('events.col.status')}</th>
                <th className={`${th} text-left`}>{t('events.col.payment')}</th>
                <th className={`${th} text-left`}>{t('events.col.source')}</th>
              </tr>
            </thead>
            <tbody>
              {events.map((e, i) => {
                const eventYear = new Date(e.date).getFullYear();
                const bookedElsewhere = e.sourceStatementYear != null && e.sourceStatementYear !== eventYear;
                const reportedLater = e.reportDate != null && new Date(e.reportDate).getFullYear() !== eventYear;
                const codeKey = e.diagnosticCode ? `diag.code.${e.diagnosticCode}` : null;
                return (
                  <tr key={i} className="border-b border-slate-700/50 hover:bg-slate-700/30 transition-colors">
                    <td className="px-3 py-2.5 text-slate-300 whitespace-nowrap">
                      {formatDate(e.date)}
                      {bookedElsewhere && <span className="block text-xs text-amber-400">{t('events.booked', { year: e.sourceStatementYear ?? '' })}</span>}
                      {reportedLater && <span className="block text-xs text-amber-400">{t('events.reported', { date: formatDate(e.reportDate!) })}</span>}
                    </td>
                    <td className="px-3 py-2.5">
                      <span className="text-white font-medium">{e.symbol}</span>
                      {e.reference && <span className="ml-2 text-xs text-slate-500">{e.reference}</span>}
                      {e.rawDescription && <span className="block text-xs text-slate-500 max-w-xs truncate" title={e.rawDescription}>{e.rawDescription}</span>}
                    </td>
                    <td className={`px-3 py-2.5 text-right font-mono tabular-nums ${e.amount < 0 ? 'text-emerald-400' : 'text-red-400'}`}>
                      {e.amount.toFixed(2)} {e.currency}
                      <span className="block text-xs text-slate-500">{formatPln(e.amountPln)}</span>
                    </td>
                    <td className="px-3 py-2.5">
                      <span className={`inline-block rounded px-1.5 py-0.5 text-[11px] font-medium ${STATUS_STYLES[e.status]}`}>
                        {t(`events.status.${e.status}`)}
                      </span>
                      {codeKey && <span className="block text-xs text-amber-400/80 mt-0.5">{t(codeKey)}</span>}
                    </td>
                    <td className="px-3 py-2.5 text-slate-300 whitespace-nowrap">{e.matchedPayment ?? '—'}</td>
                    <td className="px-3 py-2.5 text-xs text-slate-500">
                      {e.sourceFile}
                      {e.accountId && <span className="block">{e.accountId}</span>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
