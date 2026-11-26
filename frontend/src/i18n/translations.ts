import { mergeTranslations, type Translations } from './context';
import { common } from './common';
import { landing } from './landing';
import { importPage } from './importPage';
import { dashboard } from './dashboard';
import { transactions } from './transactions';
import { dividends } from './dividends';
import { pit38 } from './pit38';
import { diagnostics } from './diagnostics';
import { decisions } from './decisions';

/** All UI strings, one module per page. Each module has identical keys in en, uk and pl. */
export const translations: Translations = mergeTranslations([
  common,
  landing,
  importPage,
  dashboard,
  transactions,
  dividends,
  pit38,
  diagnostics,
  decisions,
]);
