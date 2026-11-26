import type { TaxDecisions } from './types';

/**
 * The answers of a new calculation (backend TaxDecisions.Default): the settlement date sets a trade's year and
 * rate, and nothing else is confirmed until the user chooses.
 */
export const DEFAULT_DECISIONS: TaxDecisions = {
  dateBasis: 'SettlementDate',
  securitiesLending: 'Unconfirmed',
  paymentsInLieu: 'Unconfirmed',
  crossYearRefunds: 'Unconfirmed',
};
