import { useState, useMemo } from 'react';
import { ArrowUpDown, Search } from 'lucide-react';
import { formatPln, formatUsd, formatRate, formatDate, formatTaxPeriod, hasCustomTaxPeriod, numColor } from '../format';
import type { AppState, TradeResult } from '../types';
import EmptyState from '../components/EmptyState';
import { useT } from '../i18n';

type SortKey = 'dateTime' | 'symbol' | 'gainLossPln';

const TYPE_FILTER_KEYS = {
  all: 'transactions.filterAll',
  Buy: 'transactions.typeBuy',
  Sell: 'transactions.typeSell',
} as const;

export default function TransactionsPage({ state }: { state: AppState }) {
  const t = useT();
  const [symbolFilter, setSymbolFilter] = useState('');
  const [typeFilter, setTypeFilter] = useState<'all' | 'Buy' | 'Sell'>('all');
  const [sortKey, setSortKey] = useState<SortKey>('dateTime');
  const [sortAsc, setSortAsc] = useState(true);
  const [page, setPage] = useState(1);
  const pageSize = 25;

  const filtered = useMemo(() => {
    let data = [...state.trades];
    if (symbolFilter) data = data.filter(trade => trade.symbol.toLowerCase().includes(symbolFilter.toLowerCase()));
    if (typeFilter !== 'all') data = data.filter(trade => trade.type === typeFilter);

    data.sort((a, b) => {
      let cmp = 0;
      if (sortKey === 'dateTime') cmp = new Date(a.dateTime).getTime() - new Date(b.dateTime).getTime();
      else if (sortKey === 'symbol') cmp = a.symbol.localeCompare(b.symbol);
      else cmp = a.gainLossPln - b.gainLossPln;
      return sortAsc ? cmp : -cmp;
    });
    return data;
  }, [state.trades, symbolFilter, typeFilter, sortKey, sortAsc]);

  const totals = useMemo(() => {
    let proceeds = 0, cost = 0, gainLoss = 0;
    for (const trade of filtered) {
      if (trade.type !== 'Sell') continue;
      proceeds += trade.proceedsPln;
      cost += trade.costPln;
      gainLoss += trade.gainLossPln;
    }
    return { proceeds, cost, gainLoss };
  }, [filtered]);

  if (!state.sessionId || !state.summary) {
    return <EmptyState />;
  }

  const customTaxPeriod = hasCustomTaxPeriod(state.summary.year, state.summary.taxableFrom);
  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const paginated = filtered.slice((page - 1) * pageSize, page * pageSize);

  const toggleSort = (key: SortKey) => {
    if (sortKey === key) setSortAsc(!sortAsc);
    else { setSortKey(key); setSortAsc(true); }
  };

  const sortHeader = (label: string, k: SortKey) => (
    <th
      className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 cursor-pointer hover:text-slate-200 select-none"
      onClick={() => toggleSort(k)}
    >
      <span className="inline-flex items-center gap-1">
        {label}
        <ArrowUpDown className="w-3 h-3" />
      </span>
    </th>
  );

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-white text-2xl font-bold">{t('transactions.title')}</h1>
        <p className="text-slate-400 text-sm mt-1">
          {t('transactions.showing', { period: formatTaxPeriod(state.summary.taxableFrom, state.summary.taxableTo) })}
        </p>
        <p className="text-slate-500 text-sm mt-1">
          {t('transactions.costNoteBefore')}<span className="font-medium text-slate-400">{t('transactions.costNoteLabel')}</span>{t('transactions.costNoteAfter')}
        </p>
        {customTaxPeriod && (
          <p className="text-slate-500 text-sm mt-1">
            {t('transactions.customPeriodNote')}
          </p>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-4">
        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
          <input
            type="text"
            placeholder={t('transactions.filterPlaceholder')}
            value={symbolFilter}
            onChange={(e) => { setSymbolFilter(e.target.value); setPage(1); }}
            className="bg-slate-800 border border-slate-700 rounded-lg pl-9 pr-4 py-2 text-sm text-white placeholder:text-slate-500 focus:outline-none focus:border-blue-500"
          />
        </div>
        <div className="flex rounded-lg overflow-hidden border border-slate-700">
          {(['all', 'Buy', 'Sell'] as const).map(kind => (
            <button
              key={kind}
              onClick={() => { setTypeFilter(kind); setPage(1); }}
              className={`px-4 py-2 text-sm transition-colors ${
                typeFilter === kind ? 'bg-blue-600 text-white' : 'bg-slate-800 text-slate-400 hover:text-white'
              }`}
            >
              {t(TYPE_FILTER_KEYS[kind])}
            </button>
          ))}
        </div>
        <span className="text-slate-500 text-sm ml-auto">
          {filtered.length === 1
            ? t('transactions.countOne', { count: filtered.length })
            : t('transactions.countMany', { count: filtered.length })}
        </span>
      </div>

      <div className="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-700">
                {sortHeader(t('transactions.col.date'), 'dateTime')}
                {sortHeader(t('transactions.col.symbol'), 'symbol')}
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-left">{t('transactions.col.type')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('transactions.col.qty')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('transactions.col.priceUsd')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('transactions.col.proceedsUsd')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('transactions.col.tradeRate')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('transactions.col.proceedsPln')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-3 py-3 text-right">{t('transactions.col.costPln')}</th>
                {sortHeader(t('transactions.col.gainLossPln'), 'gainLossPln')}
              </tr>
            </thead>
            <tbody>
              {paginated.map((trade, i) => (
                <Row key={`${trade.symbol}-${trade.dateTime}-${i}`} trade={trade} />
              ))}
            </tbody>
            <tfoot>
              <tr className="border-t border-slate-600 bg-slate-800/80">
                <td colSpan={7} className="px-3 py-3 text-slate-300 font-medium text-sm">{t('transactions.totals')}</td>
                <td className="px-3 py-3 text-right font-mono tabular-nums text-blue-400 text-sm">{formatPln(totals.proceeds)}</td>
                <td className="px-3 py-3 text-right font-mono tabular-nums text-blue-400 text-sm">{formatPln(totals.cost)}</td>
                <td className={`px-3 py-3 text-right font-mono tabular-nums text-sm ${numColor(totals.gainLoss)}`}>{formatPln(totals.gainLoss)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      </div>

      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-2">
          <button
            disabled={page <= 1}
            onClick={() => setPage(p => p - 1)}
            className="px-3 py-1.5 text-sm rounded-lg bg-slate-800 border border-slate-700 text-slate-400 hover:text-white disabled:opacity-40"
          >
            {t('transactions.previous')}
          </button>
          <span className="text-slate-400 text-sm px-3">{t('transactions.pageOf', { page, total: totalPages })}</span>
          <button
            disabled={page >= totalPages}
            onClick={() => setPage(p => p + 1)}
            className="px-3 py-1.5 text-sm rounded-lg bg-slate-800 border border-slate-700 text-slate-400 hover:text-white disabled:opacity-40"
          >
            {t('transactions.next')}
          </button>
        </div>
      )}
    </div>
  );
}

function Row({ trade }: { trade: TradeResult }) {
  const t = useT();
  const isSell = trade.type === 'Sell';
  return (
    <tr className={`border-b border-slate-700/50 hover:bg-slate-700/30 transition-colors ${trade.rateUnavailable ? 'bg-amber-500/5' : ''}`}>
      <td className="px-3 py-2.5 text-slate-300 whitespace-nowrap" title={trade.rateUnavailable ? t('transactions.rateUnavailable') : undefined}>
        {formatDate(trade.dateTime)}
        {trade.settleDate && trade.settleDate.slice(0, 10) !== trade.dateTime.slice(0, 10) && (
          <span className="block text-xs text-slate-500 mt-0.5">{t('transactions.settles', { date: formatDate(trade.settleDate) })}</span>
        )}
      </td>
      <td className="px-3 py-2.5 text-white font-medium">
        {trade.symbol}
        {trade.note && (
          <span className="block text-xs font-normal text-amber-300/90 mt-0.5 max-w-xs" title={trade.note}>{trade.note}</span>
        )}
      </td>
      <td className="px-3 py-2.5">
        <span className={trade.type === 'Buy'
          ? 'bg-blue-500/20 text-blue-300 text-xs px-2 py-0.5 rounded-full'
          : 'bg-amber-500/20 text-amber-300 text-xs px-2 py-0.5 rounded-full'
        }>
          {trade.type === 'Buy' ? t('transactions.typeBuy') : trade.type === 'Sell' ? t('transactions.typeSell') : trade.type}
        </span>
      </td>
      <td className="px-3 py-2.5 text-right font-mono tabular-nums text-slate-300">{trade.quantity}</td>
      <td className="px-3 py-2.5 text-right font-mono tabular-nums text-slate-300">{formatUsd(trade.priceOriginal)}</td>
      <td className="px-3 py-2.5 text-right font-mono tabular-nums text-slate-300">{isSell ? formatUsd(trade.proceedsOriginal) : '—'}</td>
      <td className="px-3 py-2.5 text-right font-mono tabular-nums text-slate-400 whitespace-nowrap">
        {formatRate(trade.exchangeRate)}
        {trade.rateDate && (
          <span className="block text-xs font-sans text-slate-500 mt-0.5">{t('transactions.rateOf', { date: formatDate(trade.rateDate) })}</span>
        )}
      </td>
      <td className="px-3 py-2.5 text-right font-mono tabular-nums text-blue-400">{isSell ? formatPln(trade.proceedsPln) : '—'}</td>
      <td className="px-3 py-2.5 text-right font-mono tabular-nums text-blue-400">{formatPln(trade.costPln)}</td>
      <td className={`px-3 py-2.5 text-right font-mono tabular-nums ${isSell ? numColor(trade.gainLossPln) : 'text-slate-600'}`}>
        {isSell ? formatPln(trade.gainLossPln) : '—'}
      </td>
    </tr>
  );
}
