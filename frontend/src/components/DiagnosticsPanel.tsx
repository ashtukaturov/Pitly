import { useState } from 'react';
import { AlertTriangle, CheckCircle2, Info } from 'lucide-react';
import { formatPln } from '../format';
import type { Pit38Fields, TaxDiagnostic } from '../types';
import { useT, useLang, type Lang } from '../i18n';

function pluralKey(lang: Lang, count: number): 'One' | 'Many' {
  if (lang === 'en') return count === 1 ? 'One' : 'Many';
  return count % 10 === 1 && count % 100 !== 11 ? 'One' : 'Many';
}

/**
 * The structured findings of a calculation (backend Pit38Fields.Diagnostics): a readiness banner, the findings
 * that keep the amounts from being final, and the informational notes behind a toggle. The code of each finding
 * is translated; its message is the backend's English text. Shown on the dashboard and on the PIT-38 page, and
 * printed with it, so that the same facts travel with the amounts.
 */
export default function DiagnosticsPanel({ pit38, compact = false }: { pit38: Pit38Fields; compact?: boolean }) {
  const t = useT();
  const { lang } = useLang();
  const [notesOpen, setNotesOpen] = useState(false);
  const diagnostics = pit38.diagnostics ?? [];
  const blocking = diagnostics.filter(d => d.blocksFiling && d.code !== 'REIMPORT_REQUIRED');
  const notes = diagnostics.filter(d => !d.blocksFiling);
  const ready = pit38.readyToFile && !pit38.needsReimport;

  return (
    <div className="space-y-4">
      {pit38.needsReimport && (
        <div className="print-warning bg-red-500/10 border border-red-500/40 rounded-xl p-5 text-sm text-red-200 flex gap-3" role="alert">
          <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5 text-red-300" />
          <div>
            <p className="font-semibold text-red-100">{t('diag.reimport.title')}</p>
            <p className="mt-1">{t('diag.reimport.body')}</p>
          </div>
        </div>
      )}

      {ready ? (
        !compact && (
          <div className="print-warning bg-emerald-500/10 border border-emerald-500/30 rounded-xl p-4 text-sm text-emerald-200 flex gap-3">
            <CheckCircle2 className="w-5 h-5 shrink-0 mt-0.5 text-emerald-300" />
            <div>
              <p className="font-semibold text-emerald-100">{t('diag.ready.title')}</p>
              <p className="mt-1 text-emerald-200/80">{t('diag.ready.body')}</p>
            </div>
          </div>
        )
      ) : (
        blocking.length > 0 && (
          <div className="print-warning bg-amber-500/10 border border-amber-500/40 rounded-xl p-5 text-sm text-amber-200" role="alert">
            <p className="font-semibold text-amber-100 flex items-center gap-2">
              <AlertTriangle className="w-4 h-4 text-amber-300" />
              {t('diag.notReady.title', { amount: formatPln(pit38.unresolvedAmountPln ?? 0) })}
            </p>
            <p className="mt-1 text-amber-200/80">{t('diag.notReady.body')}</p>
            <ul className="mt-3 space-y-3">
              {blocking.map((d, i) => <DiagnosticItem key={i} diagnostic={d} tone="amber" />)}
            </ul>
          </div>
        )
      )}

      {notes.length > 0 && (
        <div className="bg-slate-800 border border-slate-700 rounded-xl text-sm">
          <button
            type="button"
            onClick={() => setNotesOpen(!notesOpen)}
            className="no-print w-full flex items-center justify-between px-5 py-3 text-left text-slate-300 hover:text-white transition-colors"
          >
            <span className="flex items-center gap-2">
              <Info className="w-4 h-4 text-blue-400" />
              {t('diag.notesTitle')}
              <span className="text-slate-500">({t(`diag.notes${pluralKey(lang, notes.length)}`, { count: notes.length })})</span>
            </span>
            <span className="text-xs text-blue-400">{notesOpen ? t('diag.hideNotes') : t('diag.showNotes')}</span>
          </button>
          <ul className={`${notesOpen ? '' : 'hidden'} print-block px-5 pb-4 space-y-3`}>
            {notes.map((d, i) => <DiagnosticItem key={i} diagnostic={d} tone="slate" />)}
          </ul>
        </div>
      )}
    </div>
  );
}

function DiagnosticItem({ diagnostic, tone }: { diagnostic: TaxDiagnostic; tone: 'amber' | 'slate' }) {
  const t = useT();
  const labelKey = `diag.code.${diagnostic.code}`;
  const label = t(labelKey);
  const badge = tone === 'amber' ? 'bg-amber-500/20 text-amber-100' : 'bg-slate-700 text-slate-200';
  const meta = tone === 'amber' ? 'text-amber-200/70' : 'text-slate-500';
  const details: string[] = [];
  if (diagnostic.accountId) details.push(t('diag.account', { account: diagnostic.accountId }));
  if (diagnostic.source) details.push(t('diag.source', { source: diagnostic.source }));
  if (diagnostic.affectedYears && diagnostic.affectedYears.length > 0) details.push(t('diag.years', { years: diagnostic.affectedYears.join(', ') }));

  return (
    <li className="flex flex-col gap-1">
      <div className="flex flex-wrap items-center gap-2">
        <span className={`inline-block rounded px-1.5 py-0.5 text-[11px] font-medium ${badge}`}>
          {label === labelKey ? diagnostic.code : label}
        </span>
        {diagnostic.unresolvedAmountPln != null && diagnostic.unresolvedAmountPln > 0 && (
          <span className="font-mono text-xs">{t('diag.unresolved', { amount: formatPln(diagnostic.unresolvedAmountPln) })}</span>
        )}
      </div>
      <span>{diagnostic.message}</span>
      {details.length > 0 && <span className={`text-xs ${meta}`}>{details.join(' · ')}</span>}
    </li>
  );
}
