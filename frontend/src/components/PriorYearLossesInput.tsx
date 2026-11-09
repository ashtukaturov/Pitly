import { Plus, Trash2 } from 'lucide-react';
import {
  CARRY_FORWARD_YEARS,
  MAX_PRIOR_YEAR_LOSS_ROWS as MAX_ROWS,
  ONE_OFF_FIRST_YEAR,
  parseAmount,
  type PriorYearLossRow,
} from '../priorYearLosses';
import { useT, type TFunction } from '../i18n/index';

// Statements for tax year N are normally imported in N+1; the real tax year is only known after parsing,
// so client-side hints use this guess and the backend repeats the check against the actual year.
const likelyTaxYear = new Date().getFullYear() - 1;

function rowHint(row: PriorYearLossRow, t: TFunction): { text: string; tone: 'warn' | 'info' } | null {
  const year = Number(row.year);
  if (!Number.isInteger(year) || row.year.trim() === '') return null;
  if (year >= likelyTaxYear) {
    return {
      text: t('losses.hintFuture', { year: likelyTaxYear, from: likelyTaxYear - CARRY_FORWARD_YEARS, to: likelyTaxYear - 1 }),
      tone: 'warn',
    };
  }
  if (year < likelyTaxYear - CARRY_FORWARD_YEARS) {
    return { text: t('losses.hintExpired', { year, until: year + CARRY_FORWARD_YEARS }), tone: 'warn' };
  }
  const loss = parseAmount(row.loss);
  const deducted = parseAmount(row.deducted) ?? 0;
  if (year < ONE_OFF_FIRST_YEAR) {
    return { text: t('losses.hintPre2019'), tone: 'info' };
  }
  if (loss !== null && loss > 0 && deducted > loss) {
    return { text: t('losses.hintDeductedExceeds'), tone: 'warn' };
  }
  if (year === likelyTaxYear - CARRY_FORWARD_YEARS) {
    return { text: t('losses.hintLastChance', { year, taxYear: likelyTaxYear }), tone: 'info' };
  }
  return null;
}

const inputClass =
  'w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-2 text-sm text-white font-mono tabular-nums focus:outline-none focus:border-blue-500';

export default function PriorYearLossesInput({
  rows,
  onChange,
}: {
  rows: PriorYearLossRow[];
  onChange: (rows: PriorYearLossRow[]) => void;
}) {
  const t = useT();
  const update = (index: number, patch: Partial<PriorYearLossRow>) =>
    onChange(rows.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  const remove = (index: number) => onChange(rows.filter((_, i) => i !== index));
  const add = () => onChange([...rows, { year: '', loss: '', deducted: '', oneOffUsed: false }]);

  return (
    <div className="mb-6 bg-slate-800 border border-slate-700 rounded-xl p-5">
      <span className="text-slate-200 font-medium">{t('losses.title')}</span>
      <span className="block text-slate-500 text-sm mt-1">
        {t('losses.intro')}
      </span>

      {rows.length > 0 && (
        <div className="mt-4 space-y-3">
          <div className="hidden md:grid grid-cols-[6rem_1fr_1fr_2.5rem] gap-3 text-slate-500 text-xs uppercase tracking-wider px-1">
            <span>{t('losses.colYear')}</span>
            <span>{t('losses.colLoss')}</span>
            <span>{t('losses.colDeducted')}</span>
            <span />
          </div>
          {rows.map((row, index) => {
            const hint = rowHint(row, t);
            return (
              <div key={index}>
                <div className="grid grid-cols-1 md:grid-cols-[6rem_1fr_1fr_2.5rem] gap-3 items-center">
                  <input
                    type="number"
                    inputMode="numeric"
                    min="1990"
                    max="2100"
                    step="1"
                    placeholder="2023"
                    aria-label={t('losses.colYear')}
                    value={row.year}
                    onChange={(e) => update(index, { year: e.target.value })}
                    className={inputClass}
                  />
                  <input
                    type="number"
                    inputMode="decimal"
                    min="0"
                    step="0.01"
                    placeholder={t('losses.lossPlaceholder')}
                    aria-label={t('losses.ariaLoss')}
                    value={row.loss}
                    onChange={(e) => update(index, { loss: e.target.value })}
                    className={inputClass}
                  />
                  <input
                    type="number"
                    inputMode="decimal"
                    min="0"
                    step="0.01"
                    placeholder="0.00"
                    aria-label={t('losses.ariaDeducted')}
                    value={row.deducted}
                    onChange={(e) => update(index, { deducted: e.target.value })}
                    className={inputClass}
                  />
                  <button
                    type="button"
                    onClick={() => remove(index)}
                    aria-label={t('losses.remove')}
                    className="justify-self-end md:justify-self-center text-slate-500 hover:text-red-400 transition-colors p-2"
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
                {Number(row.year) >= 2019 && (
                  <label className="mt-1 px-1 flex items-start gap-2 text-xs text-slate-400 cursor-pointer" title={t('losses.oneOffHint')}>
                    <input
                      type="checkbox"
                      checked={row.oneOffUsed}
                      onChange={(e) => update(index, { oneOffUsed: e.target.checked })}
                      className="mt-0.5 accent-blue-500"
                    />
                    <span>{t('losses.colOneOff')}</span>
                  </label>
                )}
                {hint && (
                  <p className={`mt-1 px-1 text-xs ${hint.tone === 'warn' ? 'text-amber-400' : 'text-slate-500'}`}>
                    {hint.text}
                  </p>
                )}
              </div>
            );
          })}
        </div>
      )}

      {rows.length < MAX_ROWS && (
        <button
          type="button"
          onClick={add}
          className="mt-4 inline-flex items-center gap-2 text-sm text-blue-400 hover:text-blue-300 transition-colors"
        >
          <Plus className="w-4 h-4" />
          {rows.length === 0 ? t('losses.addFirst') : t('losses.addAnother')}
        </button>
      )}

      <ul className="mt-4 text-slate-500 text-xs space-y-1 list-disc list-inside">
        <li>{t('losses.rule1')}</li>
        <li>{t('losses.rule2')}</li>
        <li>{t('losses.rule3')}</li>
        <li>{t('losses.rule4')}</li>
      </ul>
    </div>
  );
}
