import type { Translations } from './index';

// The user's answers to the open tax questions (backend TaxDecisions) and the income left out of PIT-38 by them.
// Article references and the names of the Polish forms stay as they are in every language.
export const decisions: Translations = {
  en: {
    'decisions.title': 'Your decisions',
    'decisions.hint': 'The law does not settle these questions unambiguously. Pitly calculates with your answer, recalculates at once when you change it and prints it with the result.',
    'decisions.recalculating': 'Recalculating…',
    'decisions.dateBasis.label': 'Date of a trade (year and exchange rate)',
    'decisions.dateBasis.SettlementDate': 'Settlement date (recommended)',
    'decisions.dateBasis.TradeDate': 'Trade date',
    'decisions.dateBasis.hint':
      'Income from a sale arises when ownership passes, which for shares on an account is the settlement day (art. 17 ust. 1ab pkt 1 ustawy o PIT). Most interpretations of the tax information office (KIS) and Polish brokers use it. Use the same date every year, so that a trade at the year end is counted once.',
    'decisions.lending.label': 'Income from lending shares (SYEP)',
    'decisions.lending.Unconfirmed': 'Not confirmed (counted as interest, blocks filing)',
    'decisions.lending.Interest': 'Interest, section G of PIT-38 (19%)',
    'decisions.lending.OtherSources': 'Other sources, PIT-36 (tax scale)',
    'decisions.lending.hint':
      'No source settles whether a fee for lending shares is interest (art. 30a ust. 1 pkt 3) or income from other sources (art. 20 ust. 1). For a material amount, an individual interpretation (ORD-IN) gives legal protection.',
    'decisions.inLieu.label': 'Payments in lieu of dividends',
    'decisions.inLieu.Unconfirmed': 'Not confirmed (counted like a dividend, blocks filing)',
    'decisions.inLieu.Dividend': 'Like a dividend, section G of PIT-38 (19%)',
    'decisions.inLieu.OtherSources': 'Other sources, PIT-36 (tax scale)',
    'decisions.inLieu.hint':
      'A payment in lieu is paid by the borrower of your shares, not by the issuer, so it is not clearly a dividend (art. 30a ust. 1 pkt 4).',
    'decisions.crossYear.label': 'Tax refunds booked in a later year',
    'decisions.crossYear.Unconfirmed': 'Not confirmed (blocks filing)',
    'decisions.crossYear.CorrectOriginalYear': 'Correction of the dividend\'s year (recommended)',
    'decisions.crossYear.hint':
      'The credit is the tax actually paid abroad (art. 30a ust. 9). A refund means less was paid for the year of the dividend, so that year is corrected (art. 81 O.p.). If it was already filed, file a correction with the amounts shown for that year.',
    'otherSources.title': 'Not in PIT-38: income from other sources (PIT-36)',
    'otherSources.body':
      'As you decided, these payments are income from other sources (art. 20 ust. 1 ustawy o PIT). Declare them in PIT-36, where they are taxed at the scale together with your other income and the foreign tax is credited under art. 27 ust. 9. Pitly does not compute that tax.',
    'otherSources.colDate': 'Date',
    'otherSources.colIncome': 'Income',
    'otherSources.colAmount': 'Amount',
    'otherSources.colPln': 'In PLN',
    'otherSources.colTax': 'Tax withheld abroad',
    'otherSources.total': 'Total',
  },
  uk: {
    'decisions.title': 'Ваші рішення',
    'decisions.hint': 'Закон не дає однозначної відповіді на ці питання. Pitly рахує за вашою відповіддю, одразу перераховує суми, коли ви її змінюєте, і друкує її разом із результатом.',
    'decisions.recalculating': 'Перераховую…',
    'decisions.dateBasis.label': 'Дата угоди (рік і курс)',
    'decisions.dateBasis.SettlementDate': 'Дата розрахунку (рекомендовано)',
    'decisions.dateBasis.TradeDate': 'Дата укладення угоди',
    'decisions.dateBasis.hint':
      'Дохід від продажу виникає з переходом права власності, а для акцій на рахунку це день розрахунку (art. 17 ust. 1ab pkt 1 ustawy o PIT). Більшість інтерпретацій податкової інформаційної служби (KIS) і польські брокери використовують саме її. Використовуйте ту саму дату щороку, щоб угоду на межі року врахувати один раз.',
    'decisions.lending.label': 'Дохід від позики акцій (SYEP)',
    'decisions.lending.Unconfirmed': 'Не підтверджено (враховано як відсотки, блокує подання)',
    'decisions.lending.Interest': 'Відсотки, розділ G PIT-38 (19%)',
    'decisions.lending.OtherSources': 'Інші джерела, PIT-36 (за шкалою)',
    'decisions.lending.hint':
      'Жодне джерело не вирішує, чи плата за позику акцій — це відсотки (art. 30a ust. 1 pkt 3) чи дохід з інших джерел (art. 20 ust. 1). Якщо сума помітна, правовий захист дає індивідуальна інтерпретація (ORD-IN).',
    'decisions.inLieu.label': 'Виплати замість дивідендів (payment in lieu)',
    'decisions.inLieu.Unconfirmed': 'Не підтверджено (враховано як дивіденд, блокує подання)',
    'decisions.inLieu.Dividend': 'Як дивіденд, розділ G PIT-38 (19%)',
    'decisions.inLieu.OtherSources': 'Інші джерела, PIT-36 (за шкалою)',
    'decisions.inLieu.hint':
      'Таку виплату робить позичальник ваших акцій, а не емітент, тож вона не є однозначно дивідендом (art. 30a ust. 1 pkt 4).',
    'decisions.crossYear.label': 'Повернення податку, проведені наступного року',
    'decisions.crossYear.Unconfirmed': 'Не підтверджено (блокує подання)',
    'decisions.crossYear.CorrectOriginalYear': 'Корекція року дивіденду (рекомендовано)',
    'decisions.crossYear.hint':
      'Залік дорівнює податку, фактично сплаченому за кордоном (art. 30a ust. 9). Повернення означає, що за рік дивіденду сплачено менше, тож виправляється саме цей рік (art. 81 O.p.). Якщо декларацію вже подано, подайте корекцію із сумами, показаними для того року.',
    'otherSources.title': 'Не в PIT-38: дохід з інших джерел (PIT-36)',
    'otherSources.body':
      'За вашим рішенням ці виплати — дохід з інших джерел (art. 20 ust. 1 ustawy o PIT). Їх декларують у PIT-36: там вони оподатковуються за шкалою разом з іншими доходами, а іноземний податок зараховується за art. 27 ust. 9. Цей податок Pitly не рахує.',
    'otherSources.colDate': 'Дата',
    'otherSources.colIncome': 'Дохід',
    'otherSources.colAmount': 'Сума',
    'otherSources.colPln': 'У PLN',
    'otherSources.colTax': 'Податок, утриманий за кордоном',
    'otherSources.total': 'Разом',
  },
  pl: {
    'decisions.title': 'Twoje decyzje',
    'decisions.hint': 'Przepisy nie rozstrzygają jednoznacznie tych kwestii. Pitly liczy według Twojej odpowiedzi, od razu przelicza kwoty po jej zmianie i drukuje ją razem z wynikiem.',
    'decisions.recalculating': 'Przeliczam…',
    'decisions.dateBasis.label': 'Data transakcji (rok i kurs)',
    'decisions.dateBasis.SettlementDate': 'Data rozliczenia (zalecana)',
    'decisions.dateBasis.TradeDate': 'Data zawarcia transakcji',
    'decisions.dateBasis.hint':
      'Przychód ze sprzedaży powstaje z chwilą przeniesienia własności, a dla akcji na rachunku jest to dzień rozliczenia (art. 17 ust. 1ab pkt 1 ustawy o PIT). Większość interpretacji KIS i polscy brokerzy przyjmują właśnie tę datę. Stosuj tę samą datę co roku, aby transakcję z przełomu roku ująć tylko raz.',
    'decisions.lending.label': 'Przychód z pożyczania akcji (SYEP)',
    'decisions.lending.Unconfirmed': 'Niepotwierdzone (ujęte jako odsetki, blokuje złożenie)',
    'decisions.lending.Interest': 'Odsetki, część G PIT-38 (19%)',
    'decisions.lending.OtherSources': 'Inne źródła, PIT-36 (skala podatkowa)',
    'decisions.lending.hint':
      'Żadne źródło nie rozstrzyga, czy opłata za pożyczenie akcji to odsetki (art. 30a ust. 1 pkt 3), czy przychód z innych źródeł (art. 20 ust. 1). Przy istotnej kwocie ochronę prawną daje interpretacja indywidualna (ORD-IN).',
    'decisions.inLieu.label': 'Płatności w zamian za dywidendę (payment in lieu)',
    'decisions.inLieu.Unconfirmed': 'Niepotwierdzone (ujęte jak dywidenda, blokuje złożenie)',
    'decisions.inLieu.Dividend': 'Jak dywidenda, część G PIT-38 (19%)',
    'decisions.inLieu.OtherSources': 'Inne źródła, PIT-36 (skala podatkowa)',
    'decisions.inLieu.hint':
      'Taką płatność wypłaca pożyczkobiorca Twoich akcji, a nie emitent, więc nie jest ona jednoznacznie dywidendą (art. 30a ust. 1 pkt 4).',
    'decisions.crossYear.label': 'Zwroty podatku zaksięgowane w kolejnym roku',
    'decisions.crossYear.Unconfirmed': 'Niepotwierdzone (blokuje złożenie)',
    'decisions.crossYear.CorrectOriginalYear': 'Korekta roku dywidendy (zalecana)',
    'decisions.crossYear.hint':
      'Odliczeniu podlega podatek faktycznie zapłacony za granicą (art. 30a ust. 9). Zwrot oznacza, że za rok dywidendy zapłacono mniej, więc koryguje się ten rok (art. 81 O.p.). Jeśli zeznanie już złożono, złóż korektę z kwotami pokazanymi dla tego roku.',
    'otherSources.title': 'Poza PIT-38: przychody z innych źródeł (PIT-36)',
    'otherSources.body':
      'Zgodnie z Twoją decyzją te płatności to przychody z innych źródeł (art. 20 ust. 1 ustawy o PIT). Wykaż je w PIT-36, gdzie są opodatkowane według skali razem z innymi dochodami, a podatek zagraniczny odlicza się na podstawie art. 27 ust. 9. Pitly nie oblicza tego podatku.',
    'otherSources.colDate': 'Data',
    'otherSources.colIncome': 'Przychód',
    'otherSources.colAmount': 'Kwota',
    'otherSources.colPln': 'W PLN',
    'otherSources.colTax': 'Podatek pobrany za granicą',
    'otherSources.total': 'Razem',
  },
};
