export interface TradeResult {
  id: number;
  symbol: string;
  dateTime: string;
  type: 'Buy' | 'Sell';
  quantity: number;
  priceOriginal: number;
  proceedsOriginal: number;
  commissionOriginal: number;
  currency: string;
  exchangeRate: number;
  proceedsPln: number;
  costPln: number;
  gainLossPln: number;
  rateUnavailable: boolean;
  /** Set on rows created from corporate actions (cash merger, spin-off shares, cash in lieu). */
  note?: string | null;
  isin?: string | null;
  /** Source country named by the statement (generic CSV); null when PIT/ZG uses the ISIN prefix. */
  countryCode?: string | null;
}

/** Flat-tax (art. 30a) income stream. All are taxed at 19% and reported in the same PIT-38 field. */
export type IncomeKind = 'Dividend' | 'PaymentInLieu' | 'Interest';

export interface DividendResult {
  id: number;
  symbol: string;
  currency: string;
  date: string;
  kind: IncomeKind;
  amountOriginal: number;
  withholdingTaxOriginal: number;
  amountPln: number;
  withholdingTaxPln: number;
  /** Part of withholdingTaxPln creditable in Poland (capped at the treaty rate, or 19% without a treaty). */
  creditableWithholdingPln: number;
  /** Treaty cap applied, e.g. 0.15 for US; null when the statutory 19% cap was used. */
  maxCreditableRate: number | null;
  exchangeRate: number;
  rateUnavailable: boolean;
  isin?: string | null;
  /** Source country named by the statement (generic CSV); null when PIT/ZG uses the ISIN prefix. */
  countryCode?: string | null;
}

/** One PIT/ZG row: income attributed to the source country (the statement's country column, else the ISIN prefix). */
export interface CountryIncome {
  countryCode: string;
  countryName: string;
  proceedsPln: number;
  costPln: number;
  incomePln: number;
  lossPln: number;
  taxPaidAbroadPln: number;
  saleCount: number;
  dividendsPln: number;
  withholdingPln: number;
  creditableWithholdingPln: number;
  dividendCount: number;
}

export interface TaxSummary {
  totalProceedsPln: number;
  totalCostPln: number;
  capitalGainPln: number;
  /** Same figure as PIT-38 "Podatek należny" (after the loss deduction); kept for the session API. */
  capitalGainTaxPln: number;
  totalDividendsPln: number;
  totalWithholdingPln: number;
  totalCreditableWithholdingPln: number;
  dividendTaxOwedPln: number;
  year: number;
  taxableFrom: string;
  taxableTo: string;
  /** Amount actually deducted as "Straty z lat ubiegłych" (computed by the backend from the entered losses). */
  priorYearLossPln: number;
  /** Parser warnings: corporate actions handled with a conservative assumption, ignored rows, etc. */
  warnings: string[];
  needsReimport?: boolean;
}

/** A capital-gains loss from an earlier PIT-38, as entered on the Import page. */
export interface PriorYearLossInput {
  year: number;
  lossPln: number;
  deductedPln: number;
  /** The one-off deduction (art. 9 ust. 3 pkt 2) was already used for this loss in an earlier year */
  oneOffUsed: boolean;
}

/** Backend check of one entered loss (PriorYearLossLine). */
export interface PriorYearLossLine {
  year: number;
  lossPln: number;
  deductedPln: number;
  remainingPln: number;
  maxDeductiblePln: number;
  appliedPln: number;
  unusedPln: number;
  eligible: boolean;
  expiresAfterThisYear: boolean;
  notes: string[];
}

/** Mirrors backend Pit38Fields (System.Text.Json camelCase). Computed server-side with decimal arithmetic. */
export interface Pit38Fields {
  year: number;
  // Section C
  innePrzychody: number;
  inneKoszty: number;
  razemPrzychody: number;
  razemKoszty: number;
  dochod: number;
  strata: number;
  // Section D
  stratyZLatUbieglych: number;
  podstawaObliczenia: number;
  podatek: number;
  podatekNalezny: number;
  // Section G
  zryczaltowanyPodatek: number;
  podatekZaplaconyZaGranica: number;
  roznica: number;
  podatekDoZaplaty: number;
  // Danina solidarnosciowa (DSF-1, art. 30h)
  daninaSolidarnosciowaNadwyzka: number;
  daninaSolidarnosciowaPln: number;
  // Informational
  totalDividendsPln: number;
  // Prior-year loss check (poz. 30 breakdown) and warnings about the deduction
  priorYearLosses: PriorYearLossLine[];
  warnings: string[];
  // PIT/ZG annex rows
  pitZg: CountryIncome[];
  // Field numbers of the verified form version for this tax year; null when no version is verified for it
  form: Pit38FormLayout | null;
  // Warnings that put the amounts themselves in doubt (history that does not add up, unattributed withholding)
  dataWarnings: string[];
  // Saved by an older version without the data needed to rebuild the amounts exactly
  needsReimport: boolean;
}

/** Field numbers (poz.) of one verified PIT-38 form version; mirrors backend Pit38FormLayout. */
export interface Pit38FormLayout {
  version: number;
  pit8cPrzychody: number;
  pit8cKoszty: number;
  innePrzychody: number;
  inneKoszty: number;
  razemPrzychody: number;
  razemKoszty: number;
  dochod: number;
  strata: number;
  straty: number;
  podstawa: number;
  podatek: number;
  podatekNalezny: number;
  zryczaltowanyNiepobrany: number;
  zryczaltowany: number;
  zaplacony: number;
  roznica: number;
  doZaplaty: number;
}

/** One uploaded file and the broker account it was attributed to. */
export interface ImportedFile {
  name: string;
  broker: string | null;
  account: string;
  /** The statement itself names its account (Interactive Brokers account number, generic CSV broker column). */
  accountKnown: boolean;
  /** The account is named by the statement or was chosen by the user. */
  accountConfirmed: boolean;
}

export interface ImportResponse {
  sessionId: string;
  /** Years found anywhere in the upload; any of them can be chosen as the tax year. */
  availableYears: number[];
  files: ImportedFile[];
  summary: TaxSummary;
  pit38: Pit38Fields;
  trades: TradeResult[];
  dividends: DividendResult[];
}

export interface TradesPage {
  items: TradeResult[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AppState {
  sessionId: string | null;
  summary: TaxSummary | null;
  pit38: Pit38Fields | null;
  trades: TradeResult[];
  dividends: DividendResult[];
  // Kept so the same upload can be recalculated for another tax year without re-selecting files
  files: File[];
  availableYears: number[];
  residencyStartDate?: string;
  priorYearLosses: PriorYearLossInput[];
  // Accounts the uploaded files were attributed to, and the user's choice for files without an account number
  importedFiles: ImportedFile[];
  accountLabels: Record<string, string>;
}

/** What the dashboard can ask to recalculate without re-selecting the files. */
export interface RecalculateOptions {
  year?: number;
  residencyStartDate?: string;
  /** For files without an account number: file name -> account label chosen by the user. */
  accountLabels?: Record<string, string>;
}
