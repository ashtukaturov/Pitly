import type { ImportResponse, TradesPage, DividendResult, TaxSummary, Pit38Fields, PriorYearLossInput } from './types';

const BASE = '/api';

function buildImportForm(files: File[], residencyStartDate?: string, priorYearLosses?: PriorYearLossInput[]): FormData {
  const form = new FormData();
  files.forEach(file => form.append('files', file));
  if (residencyStartDate) form.append('residencyStartDate', residencyStartDate);
  if (priorYearLosses && priorYearLosses.length > 0) form.append('priorYearLosses', JSON.stringify(priorYearLosses));
  return form;
}

export async function importFile(file: File, residencyStartDate?: string, priorYearLosses?: PriorYearLossInput[]): Promise<ImportResponse> {
  return importFiles([file], residencyStartDate, priorYearLosses);
}

export async function importFiles(files: File[], residencyStartDate?: string, priorYearLosses?: PriorYearLossInput[]): Promise<ImportResponse> {
  const form = buildImportForm(files, residencyStartDate, priorYearLosses);
  const res = await fetch(`${BASE}/import`, { method: 'POST', body: form });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: 'Import failed' }));
    throw new Error(err.error || 'Import failed');
  }
  return res.json();
}

export async function getTrades(
  sessionId: string,
  page = 1,
  pageSize = 25,
  sortBy?: string,
  sortOrder?: string,
  symbolFilter?: string,
): Promise<TradesPage> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (sortBy) params.set('sortBy', sortBy);
  if (sortOrder) params.set('sortOrder', sortOrder);
  if (symbolFilter) params.set('symbolFilter', symbolFilter);
  const res = await fetch(`${BASE}/session/${sessionId}/trades?${params}`);
  if (!res.ok) throw new Error('Failed to load trades');
  return res.json();
}

export async function getDividends(sessionId: string): Promise<DividendResult[]> {
  const res = await fetch(`${BASE}/session/${sessionId}/dividends`);
  if (!res.ok) throw new Error('Failed to load dividends');
  return res.json();
}

export async function getSummary(sessionId: string): Promise<TaxSummary> {
  const res = await fetch(`${BASE}/session/${sessionId}/summary`);
  if (!res.ok) throw new Error('Failed to load summary');
  return res.json();
}

/** PIT-38 field values computed on the backend (decimal arithmetic, single source of truth). */
export async function getPit38(sessionId: string): Promise<Pit38Fields> {
  const res = await fetch(`${BASE}/session/${sessionId}/pit38`);
  if (!res.ok) throw new Error('Failed to load PIT-38 fields');
  return res.json();
}

export function getExportCsvUrl(sessionId: string): string {
  return `${BASE}/session/${sessionId}/export/csv`;
}
