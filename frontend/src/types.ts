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
}

/** Flat-tax (art. 30a) income stream. All are taxed at 19% and reported in PIT-38 poz. 47. */
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
}

export interface TaxSummary {
  totalProceedsPln: number;
  totalCostPln: number;
  capitalGainPln: number;
  capitalGainTaxPln: number;
  totalDividendsPln: number;
  totalWithholdingPln: number;
  totalCreditableWithholdingPln: number;
  dividendTaxOwedPln: number;
  year: number;
  taxableFrom: string;
  taxableTo: string;
  /** Amount actually deducted in poz. 30 (computed by the backend from the entered losses). */
  priorYearLossPln: number;
  /** Parser warnings: corporate actions handled with a conservative assumption, ignored rows, etc. */
  warnings: string[];
}

/** A capital-gains loss from an earlier PIT-38, as entered on the Import page. */
export interface PriorYearLossInput {
  year: number;
  lossPln: number;
  deductedPln: number;
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
  poz22Przychody: number;
  poz23Koszty: number;
  poz24RazemPrzychody: number;
  poz25RazemKoszty: number;
  poz26Dochod: number;
  poz27Strata: number;
  // Section D
  poz30StratyZLatUbieglych: number;
  poz31PodstawaObliczenia: number;
  poz33Podatek: number;
  poz35PodatekNalezny: number;
  // Section G
  poz47ZryczaltowanyPodatek: number;
  poz48PodatekZaplaconyZaGranica: number;
  poz49Roznica: number;
  poz51PodatekDoZaplaty: number;
  // Danina solidarnosciowa (DSF-1, art. 30h)
  daninaSolidarnosciowaNadwyzka: number;
  daninaSolidarnosciowaPln: number;
  // Informational
  totalDividendsPln: number;
  // Prior-year loss check (poz. 30 breakdown) and warnings about the deduction
  priorYearLosses: PriorYearLossLine[];
  warnings: string[];
}

export interface ImportResponse {
  sessionId: string;
  /** Statement years found in the upload; any of them can be chosen as the tax year. */
  availableYears: number[];
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
}
