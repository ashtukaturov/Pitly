import type { PriorYearLossInput } from './types';
import type { TFunction } from './i18n/index';

export interface PriorYearLossRow {
  year: string;
  loss: string;
  deducted: string;
}

export const MAX_PRIOR_YEAR_LOSS_ROWS = 10;
export const CARRY_FORWARD_YEARS = 5;
export const ONE_OFF_FIRST_YEAR = 2019;

export function parseAmount(raw: string): number | null {
  if (raw.trim() === '') return null;
  const n = Number(raw.replace(/\s/g, '').replace(',', '.'));
  return Number.isFinite(n) ? n : null;
}

/** Client-side validation mirroring PriorYearLossJson.TryParseInput on the backend. Error messages come from `t`. */
export function validatePriorYearLossRows(rows: PriorYearLossRow[], t: TFunction): { losses: PriorYearLossInput[]; error?: string } {
  const losses: PriorYearLossInput[] = [];
  const seen = new Set<number>();
  for (const row of rows) {
    const isEmpty = row.year.trim() === '' && row.loss.trim() === '' && row.deducted.trim() === '';
    if (isEmpty) continue;

    const year = Number(row.year);
    if (!Number.isInteger(year) || year < 1990 || year > 2100) {
      return { losses: [], error: t('losses.errorInvalidYear') };
    }
    if (seen.has(year)) {
      return { losses: [], error: t('losses.errorDuplicateYear', { year }) };
    }
    seen.add(year);

    const loss = parseAmount(row.loss);
    if (loss === null || loss <= 0) {
      return { losses: [], error: t('losses.errorNonPositive', { year }) };
    }
    const deducted = row.deducted.trim() === '' ? 0 : parseAmount(row.deducted);
    if (deducted === null || deducted < 0) {
      return { losses: [], error: t('losses.errorDeductedNegative', { year }) };
    }
    if (deducted > loss) {
      return { losses: [], error: t('losses.errorDeductedExceeds', { year }) };
    }
    losses.push({ year, lossPln: Math.round(loss * 100) / 100, deductedPln: Math.round(deducted * 100) / 100 });
  }
  if (losses.length > MAX_PRIOR_YEAR_LOSS_ROWS) {
    return { losses: [], error: t('losses.errorTooMany', { max: MAX_PRIOR_YEAR_LOSS_ROWS }) };
  }
  return { losses };
}
