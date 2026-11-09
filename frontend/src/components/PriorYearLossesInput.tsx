import { Plus, Trash2 } from 'lucide-react';
import {
  CARRY_FORWARD_YEARS,
  MAX_PRIOR_YEAR_LOSS_ROWS as MAX_ROWS,
  ONE_OFF_FIRST_YEAR,
  parseAmount,
  type PriorYearLossRow,
} from '../priorYearLosses';

// Statements for tax year N are normally imported in N+1; the real tax year is only known after parsing,
// so client-side hints use this guess and the backend repeats the check against the actual year.
const likelyTaxYear = new Date().getFullYear() - 1;

function rowHint(row: PriorYearLossRow): { text: string; tone: 'warn' | 'info' } | null {
  const year = Number(row.year);
  if (!Number.isInteger(year) || row.year.trim() === '') return null;
  if (year >= likelyTaxYear) {
    return { text: `Only losses from years before the tax year can be deducted (for ${likelyTaxYear}: ${likelyTaxYear - CARRY_FORWARD_YEARS}-${likelyTaxYear - 1}).`, tone: 'warn' };
  }
  if (year < likelyTaxYear - CARRY_FORWARD_YEARS) {
    return { text: `Probably expired: a ${year} loss can be used until tax year ${year + CARRY_FORWARD_YEARS} only.`, tone: 'warn' };
  }
  const loss = parseAmount(row.loss);
  const deducted = parseAmount(row.deducted) ?? 0;
  if (year < ONE_OFF_FIRST_YEAR) {
    return { text: 'Pre-2019 loss: at most 50% of it can be deducted per year.', tone: 'info' };
  }
  if (loss !== null && loss > 0 && deducted > loss) {
    return { text: 'Already-deducted amount exceeds the loss.', tone: 'warn' };
  }
  if (year === likelyTaxYear - CARRY_FORWARD_YEARS) {
    return { text: `Last chance: a ${year} loss expires after tax year ${likelyTaxYear}.`, tone: 'info' };
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
  const update = (index: number, patch: Partial<PriorYearLossRow>) =>
    onChange(rows.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  const remove = (index: number) => onChange(rows.filter((_, i) => i !== index));
  const add = () => onChange([...rows, { year: '', loss: '', deducted: '' }]);

  return (
    <div className="mb-6 bg-slate-800 border border-slate-700 rounded-xl p-5">
      <span className="text-slate-200 font-medium">Prior-year capital losses (PIT-38 poz. 30)</span>
      <span className="block text-slate-500 text-sm mt-1">
        Optional. For each earlier year in which your PIT-38 showed a loss (poz. &quot;Strata&quot;), enter the loss and
        how much of it you have already deducted in the years since. Pitly checks each entry against the 5-year
        carry-forward window and the per-year limits, deducts the oldest losses first and caps the total at this
        year&apos;s income.
      </span>

      {rows.length > 0 && (
        <div className="mt-4 space-y-3">
          <div className="hidden md:grid grid-cols-[6rem_1fr_1fr_2.5rem] gap-3 text-slate-500 text-xs uppercase tracking-wider px-1">
            <span>Loss year</span>
            <span>Loss (PLN)</span>
            <span>Already deducted (PLN)</span>
            <span />
          </div>
          {rows.map((row, index) => {
            const hint = rowHint(row);
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
                    aria-label="Loss year"
                    value={row.year}
                    onChange={(e) => update(index, { year: e.target.value })}
                    className={inputClass}
                  />
                  <input
                    type="number"
                    inputMode="decimal"
                    min="0"
                    step="0.01"
                    placeholder="Loss from that year's PIT-38"
                    aria-label="Loss in PLN"
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
                    aria-label="Already deducted in PLN"
                    value={row.deducted}
                    onChange={(e) => update(index, { deducted: e.target.value })}
                    className={inputClass}
                  />
                  <button
                    type="button"
                    onClick={() => remove(index)}
                    aria-label="Remove this loss year"
                    className="justify-self-end md:justify-self-center text-slate-500 hover:text-red-400 transition-colors p-2"
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
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
          {rows.length === 0 ? 'Add a loss year' : 'Add another year'}
        </button>
      )}

      <ul className="mt-4 text-slate-500 text-xs space-y-1 list-disc list-inside">
        <li>A loss can be deducted in the 5 tax years following the loss year (art. 9 ust. 3 updof).</li>
        <li>Losses from 2019 onwards can be deducted in one go up to 5 000 000 zl; older losses at most 50% per year.</li>
        <li>Only capital-gains losses (art. 30b, PIT-38) count. They do not reduce the 19% tax on dividends or interest.</li>
        <li>The deduction can never exceed this year&apos;s income (poz. 26); the rest carries forward while the window lasts.</li>
      </ul>
    </div>
  );
}
