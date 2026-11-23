import type { ImportResponse, TradesPage, DividendResult, TaxSummary, Pit38Fields, PriorYearLossInput, CreditMethod, WithholdingEvent } from './types';
import type { TFunction } from './i18n';

const BASE = '/api';

// Mirrors UploadLimits on the backend (and client_max_body_size in the Nginx configs).
export const MAX_FILE_MB = 10;
export const MAX_REQUEST_MB = 64;
const MIB = 1024 * 1024;

export interface ImportOptions {
  residencyStartDate?: string;
  priorYearLosses?: PriorYearLossInput[];
  taxYear?: number;
  /** For files without an account number: file name -> label of the account the user says it belongs to. */
  accountLabels?: Record<string, string>;
  /** How the foreign tax credit is limited; omitted means the treaty cap. */
  creditMethod?: CreditMethod;
}

/** A failed import, with what the server said about it so the UI can localise the message and offer a way out. */
export class ImportError extends Error {
  code?: 'file_too_large' | 'request_too_large';
  fileName?: string;
  /** Years found in the upload; lets the user calculate another year when the default one fails. */
  availableYears: number[] = [];
  taxYear?: number;

  constructor(message: string) {
    super(message);
    this.name = 'ImportError';
  }
}

/** Message for the user: size problems are translated, everything else is the server's explanation. */
export function describeImportError(err: unknown, t: TFunction): string {
  if (err instanceof ImportError) {
    if (err.code === 'file_too_large') return t('import.errorFileTooLarge', { name: err.fileName ?? '', max: MAX_FILE_MB });
    if (err.code === 'request_too_large') return t('import.errorRequestTooLarge', { max: MAX_REQUEST_MB, maxFile: MAX_FILE_MB });
  }
  return err instanceof Error && err.message ? err.message : t('import.errorFailed');
}

/** Size check before anything is sent, so the user gets the same clear message without waiting for the upload. */
function checkUploadSize(files: File[]): ImportError | null {
  const tooLarge = files.find(file => file.size > MAX_FILE_MB * MIB);
  if (tooLarge) {
    const error = new ImportError(`File '${tooLarge.name}' is too large. Maximum allowed size is ${MAX_FILE_MB} MB per file.`);
    error.code = 'file_too_large';
    error.fileName = tooLarge.name;
    return error;
  }
  // Leave room for the multipart boundaries and the form fields.
  const total = files.reduce((sum, file) => sum + file.size, 0);
  if (total > MAX_REQUEST_MB * MIB - 64 * 1024) {
    const error = new ImportError(`The upload is too large. All files together may not exceed ${MAX_REQUEST_MB} MB.`);
    error.code = 'request_too_large';
    return error;
  }
  return null;
}

function buildImportForm(files: File[], options: ImportOptions): FormData {
  const form = new FormData();
  files.forEach(file => form.append('files', file));
  if (options.residencyStartDate) form.append('residencyStartDate', options.residencyStartDate);
  if (options.priorYearLosses && options.priorYearLosses.length > 0) form.append('priorYearLosses', JSON.stringify(options.priorYearLosses));
  if (options.taxYear) form.append('taxYear', String(options.taxYear));
  if (options.accountLabels && Object.keys(options.accountLabels).length > 0) form.append('accountLabels', JSON.stringify(options.accountLabels));
  if (options.creditMethod && options.creditMethod !== 'TreatyCap') form.append('creditMethod', options.creditMethod);
  return form;
}

export async function importFiles(files: File[], options: ImportOptions = {}): Promise<ImportResponse> {
  const sizeError = checkUploadSize(files);
  if (sizeError) throw sizeError;

  const res = await fetch(`${BASE}/import`, { method: 'POST', body: buildImportForm(files, options) });
  if (res.ok) return res.json();

  // A proxy in front of the API (Nginx) answers 413 with an HTML page, not JSON.
  const body = await res.json().catch(() => null) as
    | { error?: string; code?: ImportError['code']; fileName?: string; availableYears?: number[]; taxYear?: number }
    | null;
  const error = new ImportError(body?.error || '');
  error.code = body?.code ?? (res.status === 413 ? 'request_too_large' : undefined);
  error.fileName = body?.fileName;
  error.availableYears = body?.availableYears ?? [];
  error.taxYear = body?.taxYear ?? undefined;
  throw error;
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

/** Every withholding event of the session's year and what the calculation did with it. */
export async function getWithholdingEvents(sessionId: string): Promise<WithholdingEvent[]> {
  const res = await fetch(`${BASE}/session/${sessionId}/withholding-events`);
  if (!res.ok) throw new Error('Failed to load withholding events');
  return res.json();
}

export function getExportCsvUrl(sessionId: string): string {
  return `${BASE}/session/${sessionId}/export/csv`;
}
