import type { Translations } from './index';

// PIT-38 page. Polish form field names, section titles and e-pity screen names stay Polish in every
// language because they name fields on the official form; notes, warnings and guide text are translated.
const en = {
  'pit38.title': 'Your PIT-38 Declaration for {year}',
  'pit38.subtitle': 'Field numbers (poz.) match the official PIT-38({version}) form.',
  'pit38.subtitleUnverified':
    'Field numbers are not shown: Pitly has no verified PIT-38 form layout for {year} (verified years: {years}). The amounts are calculated the same way; match them to the form by field name.',
  'pit38.noNumber': '—',
  'pit38.total.formula': '= {nalezny} + {roznica}',
  'pit38.dataWarnings.title': 'Check before filing: the result may be incomplete',
  'pit38.dataWarnings.intro':
    'These checks concern the data the amounts below were calculated from. Resolve them (usually by uploading the missing statement) or verify the affected positions by hand.',
  'pit38.reimport.title': 'This session was saved by an older version of Pitly',
  'pit38.reimport.body':
    'The stored data is not enough to rebuild the interest and foreign-tax-credit figures exactly, so the amounts in the dividend section may be wrong. Import the statements again to get a reliable result.',
  'pit38.taxablePeriod': 'Taxable period: {period}',
  'pit38.exportPdf': 'Export as PDF',
  'pit38.customPeriodNote':
    'Earlier uploaded history is still used to reconstruct FIFO costs and stock splits, but this PIT-38 summary includes only dividends and sells from the taxable period above.',

  'pit38.sectionC.title': 'C. Dochody / Straty — art. 30b ust. 1 ustawy',
  'pit38.sectionC.colSource': 'Source',
  'pit38.sectionC.colPrzychod': 'Przychód (b)',
  'pit38.sectionC.colKoszty': 'Koszty (c)',
  'pit38.sectionC.colDochod': 'Dochód (d)',
  'pit38.sectionC.colStrata': 'Strata (e)',
  'pit38.sectionC.rowInne': 'Inne przychody',
  'pit38.sectionC.rowRazem': 'Razem',
  'pit38.sectionC.note': 'Foreign brokers do not issue PIT-8C — all income goes to row 2 "Inne przychody" ({inne}).',

  'pit38.sectionD.title': 'D. Obliczenie zobowiązania podatkowego — art. 30b ust. 1 ustawy',
  'pit38.sectionD.poz30.name': 'Straty z lat ubiegłych',
  'pit38.sectionD.poz30.note':
    'Deduction of unused losses from the previous 5 years according to the entries made at import, after checking the 5-year window and the limits (art. 9 ust. 3); cannot exceed the income in {dochod}',
  'pit38.sectionD.poz31.name': 'Podstawa obliczenia podatku',
  'pit38.sectionD.poz31.note': '{dochodCap} minus {straty}, rounded to full złoty',
  'pit38.sectionD.poz33.name': 'Podatek ({podstawa} × 19%)',
  'pit38.sectionD.poz35.name': 'Podatek należny',
  'pit38.sectionD.poz35.note': 'After rounding to full złoty',

  'pit38.sectionG.title': 'G. Podatek do zapłaty — dywidendy zagraniczne (art. 30a)',
  'pit38.sectionG.grossLabel': 'Gross foreign dividends, interest and payments in lieu: ',
  'pit38.sectionG.informational': '(informational — not entered on PIT-38)',
  'pit38.sectionG.poz47.name': 'Zryczałtowany podatek 19% od zagranicznych dywidend, odsetek i payments in lieu',
  'pit38.sectionG.poz48.name': 'Podatek zapłacony za granicą (foreign withholding)',
  'pit38.sectionG.poz48.note':
    'Only the part consistent with the rate in the double-taxation treaty (e.g. 15% for the USA); cannot exceed the amount in {zrycz}',
  'pit38.sectionG.poz49.name': 'Różnica ({zrycz} − {zaplacony})',
  'pit38.sectionG.poz49.note': 'Only this difference is rounded: to full złoty for dividends, up to the grosz for interest (art. 63 § 1 and § 1a O.p.)',

  'pit38.total.label': 'PODATEK DO ZAPŁATY',
  'pit38.total.deadline': 'Deadline: {date}',
  'pit38.deadlineDate': 'April 30, {year}',

  'pit38.danina.title': 'Danina solidarnościowa (art. 30h): {amount}',
  'pit38.danina.body.before':
    'Your capital-gains base ({podstawa}) exceeds 1 000 000 zł by {amount}. The 4% solidarity levy is declared separately on ',
  'pit38.danina.body.after':
    ' and paid by {date}. Employment, business and other qualifying incomes also count toward the 1 000 000 zł threshold, so the real amount may be higher than shown here. Dividends and interest (art. 30a) are not included in the levy base.',
  'pit38.danina.notTriggered':
    'Danina solidarnościowa (4% above 1 000 000 zł, DSF-1) is not triggered by this capital-gains income alone. If your other incomes (employment, business) push the total above the threshold, the {podstawa} amount ({amount}) counts toward it.',

  'pit38.fieldTable.colPoz': 'Poz.',
  'pit38.fieldTable.colName': 'Field Name',
  'pit38.fieldTable.colValue': 'Value',

  'pit38.guide.title': 'Step-by-step guide: File PIT-38 via e-pity Kreator',
  'pit38.guide.intro.before': 'This guide walks you through the ',
  'pit38.guide.intro.product': 'e-pity.pl Kreator PIT',
  'pit38.guide.intro.middle': ' web version, step by step. Your calculated values are shown in ',
  'pit38.guide.intro.blue': 'blue',
  'pit38.guide.intro.after': '.',
  'pit38.guide.click': 'Click ',
  'pit38.guide.next': 'DALEJ',
  'pit38.guide.enter': 'enter',
  'pit38.guide.leaveEmpty': 'Leave empty.',

  'pit38.guide.step1.title': 'Sposób opodatkowania',
  'pit38.guide.step1.goTo': 'Go to ',
  'pit38.guide.step1.sidebar': ' (in the left sidebar). You can also go directly to the Kreator at ',
  'pit38.guide.step1.opt1': 'Sposób opodatkowania: select ',
  'pit38.guide.step1.opt1Value': 'indywidualnie',
  'pit38.guide.step1.opt2': 'Cel złożenia deklaracji: select ',
  'pit38.guide.step1.opt2Value': 'złożenie zeznania',
  'pit38.guide.step1.opt3': 'You can optionally check "Pobierz Twój e-PIT" to import pre-filled data from podatki.gov.pl',

  'pit38.guide.step2.title': 'Dane podatnika',
  'pit38.guide.step2.body':
    'Enter or verify your personal data: PESEL, full name, date of birth, and residential address as of December 31, {year}. If you used e-pity before, this may be pre-filled.',

  'pit38.guide.step3.title': 'Przychody podatnika — add income source',
  'pit38.guide.step3.p1.before': 'On the "Dodaj przychody podatnika" screen, click the ',
  'pit38.guide.step3.p1.button': '+ POZOSTAŁE',
  'pit38.guide.step3.p1.after': ' button.',
  'pit38.guide.step3.p2': 'From the dropdown, under "Pozostałe przychody", select:',
  'pit38.guide.step3.option': 'Przychody kapitałowe ze sprzedaży udziałów (PIT-38)',
  'pit38.guide.step3.warn': 'Do NOT click "PIT-8C" — foreign brokers do not issue PIT-8C.',

  'pit38.guide.step4.title': 'Przychody podatnika — enter capital gains values',
  'pit38.guide.step4.intro':
    'After selecting PIT-38 income, the Kreator shows two fields. These map to PIT-38 {inne} ("Inne przychody" row):',
  'pit38.guide.step4.field1.label': 'Wpisz przychody z części E podlegające opodatkowaniu',
  'pit38.guide.step4.field1.help': 'This is your total proceeds from selling securities in PLN ({innePrzychody}).',
  'pit38.guide.step4.field2.label': 'Wpisz koszty związane z przychodami z części E',
  'pit38.guide.step4.field2.help': 'This is your total acquisition cost (FIFO) plus commissions in PLN ({inneKoszty}).',
  'pit38.guide.step4.zeroHint': ' Enter 0 if you had no sell trades this year.',
  'pit38.guide.step4.noSales':
    'You had no sell trades in {year}, so both fields are 0. You still need to file PIT-38 to report your foreign dividends.',

  'pit38.guide.step5.title': 'PIT-38 — obliczenie zobowiązania podatkowego',
  'pit38.guide.step5.intro': 'This is the main tax calculation screen. Fill in the following fields from top to bottom:',
  'pit38.guide.step5.enter': 'Enter ',
  'pit38.guide.step5.loss.after':
    " ({straty}), computed from the losses you entered at import after checking the 5-year window and per-year limits, capped at this year's income.",
  'pit38.guide.step5.loss.none':
    'If you have undeducted PIT-38 losses from the past 5 years, enter them on the Import page so Pitly can check the limits and fill this field. Otherwise leave empty.',
  'pit38.guide.step5.flat.label': 'Zryczałtowany podatek dochodowy',
  'pit38.guide.step5.flat.body': 'Leave empty (this is for other flat-rate taxes not withheld by a payer, {niepobrany}).',
  'pit38.guide.step5.foreignFlat.label': 'Zryczałtowany podatek od przychodów uzyskanych poza granicami RP',
  'pit38.guide.step5.foreignFlat.after': ' ({zrycz})',
  'pit38.guide.step5.foreignFlat.help': 'This is 19% of your total gross dividends in PLN ({amount})',
  'pit38.guide.step5.foreignFlat.none': 'Leave empty — no foreign dividends to report.',
  'pit38.guide.step5.paidAbroad.label': 'Kwoty podatku zapłaconego za granicą (przeliczone na złote)',
  'pit38.guide.step5.paidAbroad.after': ' ({zaplacony})',
  'pit38.guide.step5.paidAbroad.help':
    "Foreign withholding tax deducted by your broker, converted to PLN and capped at the treaty rate for each issuer's country (e.g. 15% for US stocks). Cannot exceed {zrycz}.",
  'pit38.guide.step5.rest.label': 'Suma zaliczek / Dochody zwolnione z IPO / Monthly breakdown',
  'pit38.guide.step5.rest.body': 'Leave all remaining fields empty (unless you have other specific circumstances).',

  'pit38.guide.step6.title': 'Przekaż 1,5% podatku (optional)',
  'pit38.guide.step6.body':
    'You can donate 1.5% of your tax to a Public Benefit Organization (OPP). Enter the KRS number of your chosen organization. This does not increase your tax — it redirects part of what you already owe. Skip if not interested.',

  'pit38.guide.step7.title': 'Podsumowanie — review and submit',
  'pit38.guide.step7.intro': 'The summary screen shows your calculated tax:',
  'pit38.guide.step7.income': 'Kwota dochodu: ',
  'pit38.guide.step7.toPay': 'Kwota do zapłaty: ',
  'pit38.guide.step7.fromHere': 'From here you can:',
  'pit38.guide.step7.preview.button': 'ZOBACZ DEKLARACJĘ',
  'pit38.guide.step7.preview.after': ' to preview the full PIT-38 form and verify all field numbers',
  'pit38.guide.step7.send.button': 'WYŚLIJ E-DEKLARACJĘ',
  'pit38.guide.step7.send.after': ' to submit electronically to the tax office',
  'pit38.guide.step7.upo.before': 'After submission, save the ',
  'pit38.guide.step7.upo.after': ' (Urzędowe Poświadczenie Odbioru) — your official receipt',
  'pit38.guide.step7.pay.button': 'ZAPŁAĆ PODATEK ONLINE',
  'pit38.guide.step7.pay.after': ' or "DRUK PRZELEWU" to pay',
  'pit38.guide.step7.deadline': 'Deadline: ',
  'pit38.guide.step7.mustPay.before': ' Tax of ',
  'pit38.guide.step7.mustPay.after': ' must be paid by the same date.',

  'pit38.guide.notes.title': 'Important notes',
  'pit38.guide.notes.individual': 'PIT-38 is filed individually — joint filing with a spouse is not allowed for this form',
  'pit38.guide.notes.mustFile.before': 'Even if you only have losses or only dividends, you ',
  'pit38.guide.notes.mustFile.strong': 'must still file',
  'pit38.guide.notes.mustFile.after': ' PIT-38',
  'pit38.guide.notes.losses':
    "Losses from capital gains can be deducted over the next 5 years (max 50% of each year's loss per year)",
  'pit38.guide.notes.nbp': 'All PLN amounts use NBP Table A mid rates from the last business day before each transaction',
  'pit38.guide.notes.zg': 'e-pity automatically generates the PIT/ZG attachment for foreign income when you submit',

  'pit38.lossCheck.title': 'Prior-year losses check ({straty}, art. 9 ust. 3)',
  'pit38.lossCheck.colYear': 'Loss year',
  'pit38.lossCheck.colLoss': 'Loss',
  'pit38.lossCheck.colDeductedBefore': 'Deducted before',
  'pit38.lossCheck.colAvailable': 'Available',
  'pit38.lossCheck.colDeductedIn': 'Deducted in {year}',
  'pit38.lossCheck.colLeftAfter': 'Left after {year}',

  'pit38.zg.title': 'PIT/ZG — dochody zagraniczne według kraju (załącznik do PIT-38)',
  'pit38.zg.intro.before': 'One PIT/ZG per country. In section C.3 (art. 30b) enter the ',
  'pit38.zg.intro.dochod': 'dochód',
  'pit38.zg.intro.after':
    ' and the tax paid abroad on those sales. A country whose result is a loss is listed for reference only: losses are not entered in PIT/ZG, they already reduce the total in PIT-38 section C.',
  'pit38.zg.colCountry': 'Kraj (państwo)',
  'pit38.zg.colSales': 'Sales',
  'pit38.zg.colPrzychod': 'Przychód',
  'pit38.zg.colKoszty': 'Koszty',
  'pit38.zg.colDochod': 'Dochód (PIT/ZG)',
  'pit38.zg.colStrata': 'Strata',
  'pit38.zg.colTaxAbroad': 'Podatek zapłacony za granicą',
  'pit38.zg.noSales': 'No sales in the taxable period, so no PIT/ZG rows for art. 30b income are needed.',
  'pit38.zg.dividendsIntro':
    'Dividends, interest and payments in lieu by country (reference only: for PIT-38 they are not entered in PIT/ZG; the creditable withholding goes straight to {zaplacony}).',
  'pit38.zg.divColCountry': 'Kraj',
  'pit38.zg.colPayments': 'Payments',
  'pit38.zg.colGross': 'Gross (PLN)',
  'pit38.zg.colWithheld': 'Withheld (PLN)',
  'pit38.zg.colCreditable': 'Creditable (PLN)',
  'pit38.zg.countryNote':
    'Country = issuer country from the ISIN prefix. Many advisers attribute income to the country of the exchange instead; for US-listed shares both give the United States. Ireland-domiciled ETFs (IE) are reported under Ireland.',
} as const;

type Pit38Dictionary = Record<keyof typeof en, string>;

const uk: Pit38Dictionary = {
  'pit38.title': 'Ваша декларація PIT-38 за {year} рік',
  'pit38.subtitle': 'Номери полів (poz.) відповідають офіційній формі PIT-38({version}).',
  'pit38.subtitleUnverified':
    'Номери полів не показано: Pitly не має перевіреного макета форми PIT-38 за {year} рік (перевірені роки: {years}). Суми розраховано так само; зіставляйте їх із формою за назвами полів.',
  'pit38.noNumber': '—',
  'pit38.total.formula': '= {nalezny} + {roznica}',
  'pit38.dataWarnings.title': 'Перевірте перед поданням: результат може бути неповним',
  'pit38.dataWarnings.intro':
    'Ці перевірки стосуються даних, з яких розраховано суми нижче. Усуньте їх (зазвичай завантаживши відсутню виписку) або перевірте відповідні позиції вручну.',
  'pit38.reimport.title': 'Цю сесію збережено старішою версією Pitly',
  'pit38.reimport.body':
    'Збережених даних недостатньо, щоб точно відновити суми відсотків та зарахованого іноземного податку, тому суми в розділі дивідендів можуть бути хибними. Імпортуйте виписки ще раз, щоб отримати надійний результат.',
  'pit38.taxablePeriod': 'Податковий період: {period}',
  'pit38.exportPdf': 'Експортувати в PDF',
  'pit38.customPeriodNote':
    'Раніше завантажена історія все ще використовується для відновлення витрат за FIFO та дроблень акцій, але цей підсумок PIT-38 включає лише дивіденди та продажі з податкового періоду, вказаного вище.',

  'pit38.sectionC.title': 'C. Dochody / Straty — art. 30b ust. 1 ustawy',
  'pit38.sectionC.colSource': 'Джерело',
  'pit38.sectionC.colPrzychod': 'Przychód (b)',
  'pit38.sectionC.colKoszty': 'Koszty (c)',
  'pit38.sectionC.colDochod': 'Dochód (d)',
  'pit38.sectionC.colStrata': 'Strata (e)',
  'pit38.sectionC.rowInne': 'Inne przychody',
  'pit38.sectionC.rowRazem': 'Razem',
  'pit38.sectionC.note': 'Іноземні брокери не видають PIT-8C — увесь дохід іде в рядок 2 «Inne przychody» ({inne}).',

  'pit38.sectionD.title': 'D. Obliczenie zobowiązania podatkowego — art. 30b ust. 1 ustawy',
  'pit38.sectionD.poz30.name': 'Straty z lat ubiegłych',
  'pit38.sectionD.poz30.note':
    'Відрахування невикористаних збитків за попередні 5 років згідно із записами, внесеними при імпорті, після перевірки 5-річного вікна та лімітів (art. 9 ust. 3); не може перевищувати дохід з {dochod}',
  'pit38.sectionD.poz31.name': 'Podstawa obliczenia podatku',
  'pit38.sectionD.poz31.note': '{dochodCap} мінус {straty}, заокруглено до повних злотих',
  'pit38.sectionD.poz33.name': 'Podatek ({podstawa} × 19%)',
  'pit38.sectionD.poz35.name': 'Podatek należny',
  'pit38.sectionD.poz35.note': 'Після заокруглення до повних злотих',

  'pit38.sectionG.title': 'G. Podatek do zapłaty — dywidendy zagraniczne (art. 30a)',
  'pit38.sectionG.grossLabel': 'Валові іноземні дивіденди, відсотки та payments in lieu: ',
  'pit38.sectionG.informational': '(довідково — не вноситься до PIT-38)',
  'pit38.sectionG.poz47.name': 'Zryczałtowany podatek 19% od zagranicznych dywidend, odsetek i payments in lieu',
  'pit38.sectionG.poz48.name': 'Podatek zapłacony za granicą (податок, утриманий за кордоном)',
  'pit38.sectionG.poz48.note':
    'Лише частина, що відповідає ставці з угоди про уникнення подвійного оподаткування (напр. 15% для США); не може перевищувати суму з {zrycz}',
  'pit38.sectionG.poz49.name': 'Różnica ({zrycz} − {zaplacony})',
  'pit38.sectionG.poz49.note': 'Заокруглюється лише ця різниця: до повних злотих для дивідендів, до гроша в більший бік для відсотків (art. 63 § 1 та § 1a O.p.)',

  'pit38.total.label': 'PODATEK DO ZAPŁATY',
  'pit38.total.deadline': 'Термін подання: {date}',
  'pit38.deadlineDate': '30 квітня {year} р.',

  'pit38.danina.title': 'Danina solidarnościowa (art. 30h): {amount}',
  'pit38.danina.body.before':
    'Ваша база доходу від капіталу ({podstawa}) перевищує 1 000 000 zł на {amount}. Солідарний внесок 4% декларується окремо у формі ',
  'pit38.danina.body.after':
    ' і сплачується до {date}. Доходи від працевлаштування, підприємницької діяльності та інші відповідні доходи також враховуються в порозі 1 000 000 zł, тож реальна сума може бути вищою за показану тут. Дивіденди та відсотки (art. 30a) до бази внеску не входять.',
  'pit38.danina.notTriggered':
    'Danina solidarnościowa (4% понад 1 000 000 zł, DSF-1) не виникає лише через цей дохід від капіталу. Якщо ваші інші доходи (праця, підприємництво) піднімають загальну суму вище порога, сума з {podstawa} ({amount}) враховується в ній.',

  'pit38.fieldTable.colPoz': 'Poz.',
  'pit38.fieldTable.colName': 'Назва поля',
  'pit38.fieldTable.colValue': 'Значення',

  'pit38.guide.title': 'Покрокова інструкція: подання PIT-38 через e-pity Kreator',
  'pit38.guide.intro.before': 'Цей посібник крок за кроком проведе вас через вебверсію ',
  'pit38.guide.intro.product': 'e-pity.pl Kreator PIT',
  'pit38.guide.intro.middle': '. Ваші розраховані значення показано ',
  'pit38.guide.intro.blue': 'синім',
  'pit38.guide.intro.after': '.',
  'pit38.guide.click': 'Натисніть ',
  'pit38.guide.next': 'DALEJ',
  'pit38.guide.enter': 'введіть',
  'pit38.guide.leaveEmpty': 'Залиште порожнім.',

  'pit38.guide.step1.title': 'Sposób opodatkowania',
  'pit38.guide.step1.goTo': 'Перейдіть на ',
  'pit38.guide.step1.sidebar': ' (у лівій бічній панелі). Також можна перейти безпосередньо до Kreator за адресою ',
  'pit38.guide.step1.opt1': 'Sposób opodatkowania: оберіть ',
  'pit38.guide.step1.opt1Value': 'indywidualnie',
  'pit38.guide.step1.opt2': 'Cel złożenia deklaracji: оберіть ',
  'pit38.guide.step1.opt2Value': 'złożenie zeznania',
  'pit38.guide.step1.opt3':
    'За бажанням можна поставити позначку «Pobierz Twój e-PIT», щоб імпортувати попередньо заповнені дані з podatki.gov.pl',

  'pit38.guide.step2.title': 'Dane podatnika',
  'pit38.guide.step2.body':
    'Введіть або перевірте свої особисті дані: PESEL, повне ім’я, дату народження та адресу проживання станом на 31 грудня {year} р. Якщо ви вже користувалися e-pity, вони можуть бути заповнені автоматично.',

  'pit38.guide.step3.title': 'Przychody podatnika — додайте джерело доходу',
  'pit38.guide.step3.p1.before': 'На екрані «Dodaj przychody podatnika» натисніть кнопку ',
  'pit38.guide.step3.p1.button': '+ POZOSTAŁE',
  'pit38.guide.step3.p1.after': '.',
  'pit38.guide.step3.p2': 'У випадному списку в розділі «Pozostałe przychody» оберіть:',
  'pit38.guide.step3.option': 'Przychody kapitałowe ze sprzedaży udziałów (PIT-38)',
  'pit38.guide.step3.warn': 'НЕ натискайте «PIT-8C» — іноземні брокери не видають PIT-8C.',

  'pit38.guide.step4.title': 'Przychody podatnika — введіть суми доходів від капіталу',
  'pit38.guide.step4.intro':
    'Після вибору доходу PIT-38 Kreator показує два поля. Вони відповідають {inne} PIT-38 (рядок «Inne przychody»):',
  'pit38.guide.step4.field1.label': 'Wpisz przychody z części E podlegające opodatkowaniu',
  'pit38.guide.step4.field1.help': 'Це загальна сума надходжень від продажу цінних паперів у PLN ({innePrzychody}).',
  'pit38.guide.step4.field2.label': 'Wpisz koszty związane z przychodami z części E',
  'pit38.guide.step4.field2.help': 'Це загальні витрати на придбання (FIFO) плюс комісії у PLN ({inneKoszty}).',
  'pit38.guide.step4.zeroHint': ' Введіть 0, якщо цього року у вас не було продажів.',
  'pit38.guide.step4.noSales':
    'У {year} році у вас не було продажів, тому обидва поля дорівнюють 0. PIT-38 усе одно потрібно подати, щоб задекларувати іноземні дивіденди.',

  'pit38.guide.step5.title': 'PIT-38 — obliczenie zobowiązania podatkowego',
  'pit38.guide.step5.intro': 'Це головний екран розрахунку податку. Заповніть наведені нижче поля зверху вниз:',
  'pit38.guide.step5.enter': 'Введіть ',
  'pit38.guide.step5.loss.after':
    ' ({straty}) — розраховано зі збитків, внесених при імпорті, після перевірки 5-річного вікна та річних лімітів, обмежено доходом цього року.',
  'pit38.guide.step5.loss.none':
    'Якщо у вас є невідраховані збитки PIT-38 за останні 5 років, внесіть їх на сторінці Імпорт, щоб Pitly перевірив ліміти та заповнив це поле. Інакше залиште порожнім.',
  'pit38.guide.step5.flat.label': 'Zryczałtowany podatek dochodowy',
  'pit38.guide.step5.flat.body': 'Залиште порожнім (це поле для інших паушальних податків, не утриманих платником, {niepobrany}).',
  'pit38.guide.step5.foreignFlat.label': 'Zryczałtowany podatek od przychodów uzyskanych poza granicami RP',
  'pit38.guide.step5.foreignFlat.after': ' ({zrycz})',
  'pit38.guide.step5.foreignFlat.help': 'Це 19% від загальної суми валових дивідендів у PLN ({amount})',
  'pit38.guide.step5.foreignFlat.none': 'Залиште порожнім — іноземних дивідендів для декларування немає.',
  'pit38.guide.step5.paidAbroad.label': 'Kwoty podatku zapłaconego za granicą (przeliczone na złote)',
  'pit38.guide.step5.paidAbroad.after': ' ({zaplacony})',
  'pit38.guide.step5.paidAbroad.help':
    'Податок, утриманий за кордоном вашим брокером, перерахований у PLN та обмежений ставкою за угодою для країни кожного емітента (напр. 15% для акцій США). Не може перевищувати {zrycz}.',
  'pit38.guide.step5.rest.label': 'Suma zaliczek / Dochody zwolnione z IPO / помісячна розбивка',
  'pit38.guide.step5.rest.body': 'Залиште всі інші поля порожніми (якщо у вас немає інших особливих обставин).',

  'pit38.guide.step6.title': 'Przekaż 1,5% podatku (необов’язково)',
  'pit38.guide.step6.body':
    'Ви можете передати 1,5% свого податку організації суспільної користі (OPP). Введіть номер KRS обраної організації. Це не збільшує ваш податок — лише перенаправляє частину того, що ви й так маєте сплатити. Пропустіть, якщо вам це нецікаво.',

  'pit38.guide.step7.title': 'Podsumowanie — перевірка та подання',
  'pit38.guide.step7.intro': 'Екран підсумку показує розрахований податок:',
  'pit38.guide.step7.income': 'Kwota dochodu: ',
  'pit38.guide.step7.toPay': 'Kwota do zapłaty: ',
  'pit38.guide.step7.fromHere': 'Звідси ви можете:',
  'pit38.guide.step7.preview.button': 'ZOBACZ DEKLARACJĘ',
  'pit38.guide.step7.preview.after': ', щоб переглянути повну форму PIT-38 та перевірити всі номери полів',
  'pit38.guide.step7.send.button': 'WYŚLIJ E-DEKLARACJĘ',
  'pit38.guide.step7.send.after': ', щоб надіслати декларацію в електронному вигляді до податкової',
  'pit38.guide.step7.upo.before': 'Після подання збережіть ',
  'pit38.guide.step7.upo.after': ' (Urzędowe Poświadczenie Odbioru) — ваше офіційне підтвердження отримання',
  'pit38.guide.step7.pay.button': 'ZAPŁAĆ PODATEK ONLINE',
  'pit38.guide.step7.pay.after': ' або «DRUK PRZELEWU», щоб сплатити',
  'pit38.guide.step7.deadline': 'Термін: ',
  'pit38.guide.step7.mustPay.before': ' Податок у розмірі ',
  'pit38.guide.step7.mustPay.after': ' потрібно сплатити до тієї ж дати.',

  'pit38.guide.notes.title': 'Важливі примітки',
  'pit38.guide.notes.individual': 'PIT-38 подається індивідуально — спільне подання з подружжям для цієї форми неможливе',
  'pit38.guide.notes.mustFile.before': 'Навіть якщо у вас лише збитки або лише дивіденди, ви ',
  'pit38.guide.notes.mustFile.strong': 'все одно повинні подати',
  'pit38.guide.notes.mustFile.after': ' PIT-38',
  'pit38.guide.notes.losses':
    'Збитки від доходів з капіталу можна відраховувати протягом наступних 5 років (не більше 50% збитку кожного року за один рік)',
  'pit38.guide.notes.nbp':
    'Усі суми в PLN розраховані за середніми курсами НБП (таблиця A) з останнього робочого дня перед кожною операцією',
  'pit38.guide.notes.zg': 'e-pity автоматично формує додаток PIT/ZG для іноземних доходів при поданні',

  'pit38.lossCheck.title': 'Перевірка збитків минулих років ({straty}, art. 9 ust. 3)',
  'pit38.lossCheck.colYear': 'Рік збитку',
  'pit38.lossCheck.colLoss': 'Збиток',
  'pit38.lossCheck.colDeductedBefore': 'Відраховано раніше',
  'pit38.lossCheck.colAvailable': 'Доступно',
  'pit38.lossCheck.colDeductedIn': 'Відраховано у {year}',
  'pit38.lossCheck.colLeftAfter': 'Залишок після {year}',

  'pit38.zg.title': 'PIT/ZG — dochody zagraniczne według kraju (załącznik do PIT-38)',
  'pit38.zg.intro.before': 'Один PIT/ZG на кожну країну. У розділі C.3 (art. 30b) введіть ',
  'pit38.zg.intro.dochod': 'dochód',
  'pit38.zg.intro.after':
    ' та податок, сплачений за кордоном з цих продажів. Країна, результат по якій є збитком, наведена лише довідково: збитки до PIT/ZG не вносяться, вони вже зменшують підсумок у розділі C PIT-38.',
  'pit38.zg.colCountry': 'Kraj (państwo)',
  'pit38.zg.colSales': 'Продажі',
  'pit38.zg.colPrzychod': 'Przychód',
  'pit38.zg.colKoszty': 'Koszty',
  'pit38.zg.colDochod': 'Dochód (PIT/ZG)',
  'pit38.zg.colStrata': 'Strata',
  'pit38.zg.colTaxAbroad': 'Podatek zapłacony za granicą',
  'pit38.zg.noSales': 'У податковому періоді не було продажів, тому рядки PIT/ZG для доходу за art. 30b не потрібні.',
  'pit38.zg.dividendsIntro':
    'Дивіденди, відсотки та payments in lieu за країнами (лише довідково: для PIT-38 вони не вносяться до PIT/ZG; податок, що підлягає заліку, іде одразу до {zaplacony}).',
  'pit38.zg.divColCountry': 'Kraj',
  'pit38.zg.colPayments': 'Виплати',
  'pit38.zg.colGross': 'Брутто (PLN)',
  'pit38.zg.colWithheld': 'Утримано (PLN)',
  'pit38.zg.colCreditable': 'До заліку (PLN)',
  'pit38.zg.countryNote':
    'Країна = країна емітента за префіксом ISIN. Багато консультантів натомість відносять дохід до країни біржі; для акцій, що котируються в США, обидва підходи дають США. ETF з доміцилем в Ірландії (IE) показані під Ірландією.',
};

const pl: Pit38Dictionary = {
  'pit38.title': 'Twoja deklaracja PIT-38 za {year}',
  'pit38.subtitle': 'Numery pól (poz.) odpowiadają oficjalnemu formularzowi PIT-38({version}).',
  'pit38.subtitleUnverified':
    'Numery pól nie są wyświetlane: Pitly nie ma zweryfikowanego układu formularza PIT-38 za {year} r. (zweryfikowane lata: {years}). Kwoty są obliczane tak samo; dopasuj je do formularza według nazw pól.',
  'pit38.noNumber': '—',
  'pit38.total.formula': '= {nalezny} + {roznica}',
  'pit38.dataWarnings.title': 'Sprawdź przed złożeniem: wynik może być niepełny',
  'pit38.dataWarnings.intro':
    'Te kontrole dotyczą danych, z których obliczono poniższe kwoty. Usuń ich przyczynę (zwykle wgrywając brakujące zestawienie) albo sprawdź dane pozycje ręcznie.',
  'pit38.reimport.title': 'Ta sesja została zapisana przez starszą wersję Pitly',
  'pit38.reimport.body':
    'Zapisane dane nie wystarczają, aby dokładnie odtworzyć kwoty odsetek i zaliczonego podatku zagranicznego, więc kwoty w części dotyczącej dywidend mogą być błędne. Zaimportuj zestawienia ponownie, aby uzyskać wiarygodny wynik.',
  'pit38.taxablePeriod': 'Okres podatkowy: {period}',
  'pit38.exportPdf': 'Eksportuj do PDF',
  'pit38.customPeriodNote':
    'Wcześniej wgrana historia nadal służy do odtworzenia kosztów FIFO i splitów akcji, ale to podsumowanie PIT-38 obejmuje tylko dywidendy i sprzedaże z powyższego okresu podatkowego.',

  'pit38.sectionC.title': 'C. Dochody / Straty — art. 30b ust. 1 ustawy',
  'pit38.sectionC.colSource': 'Źródło',
  'pit38.sectionC.colPrzychod': 'Przychód (b)',
  'pit38.sectionC.colKoszty': 'Koszty (c)',
  'pit38.sectionC.colDochod': 'Dochód (d)',
  'pit38.sectionC.colStrata': 'Strata (e)',
  'pit38.sectionC.rowInne': 'Inne przychody',
  'pit38.sectionC.rowRazem': 'Razem',
  'pit38.sectionC.note': 'Zagraniczni brokerzy nie wystawiają PIT-8C — cały przychód trafia do wiersza 2 „Inne przychody” ({inne}).',

  'pit38.sectionD.title': 'D. Obliczenie zobowiązania podatkowego — art. 30b ust. 1 ustawy',
  'pit38.sectionD.poz30.name': 'Straty z lat ubiegłych',
  'pit38.sectionD.poz30.note':
    'Odliczenie niewykorzystanych strat z poprzednich 5 lat według wpisów z importu, po sprawdzeniu 5-letniego okna i limitów (art. 9 ust. 3); nie może przekroczyć dochodu z {dochod}',
  'pit38.sectionD.poz31.name': 'Podstawa obliczenia podatku',
  'pit38.sectionD.poz31.note': '{dochodCap} minus {straty}, zaokrąglone do pełnych zł',
  'pit38.sectionD.poz33.name': 'Podatek ({podstawa} × 19%)',
  'pit38.sectionD.poz35.name': 'Podatek należny',
  'pit38.sectionD.poz35.note': 'Po zaokrągleniu do pełnych zł',

  'pit38.sectionG.title': 'G. Podatek do zapłaty — dywidendy zagraniczne (art. 30a)',
  'pit38.sectionG.grossLabel': 'Zagraniczne dywidendy, odsetki i payments in lieu brutto: ',
  'pit38.sectionG.informational': '(informacyjnie — nie wpisuje się do PIT-38)',
  'pit38.sectionG.poz47.name': 'Zryczałtowany podatek 19% od zagranicznych dywidend, odsetek i payments in lieu',
  'pit38.sectionG.poz48.name': 'Podatek zapłacony za granicą (podatek u źródła)',
  'pit38.sectionG.poz48.note':
    'Tylko część zgodna ze stawką z umowy o unikaniu podwójnego opodatkowania (np. 15% dla USA); nie może przekroczyć kwoty z {zrycz}',
  'pit38.sectionG.poz49.name': 'Różnica ({zrycz} − {zaplacony})',
  'pit38.sectionG.poz49.note': 'Zaokrągla się tylko tę różnicę: do pełnych złotych dla dywidend, do pełnych groszy w górę dla odsetek (art. 63 § 1 i § 1a O.p.)',

  'pit38.total.label': 'PODATEK DO ZAPŁATY',
  'pit38.total.deadline': 'Termin: {date}',
  'pit38.deadlineDate': '30 kwietnia {year} r.',

  'pit38.danina.title': 'Danina solidarnościowa (art. 30h): {amount}',
  'pit38.danina.body.before':
    'Twoja podstawa z zysków kapitałowych ({podstawa}) przekracza 1 000 000 zł o {amount}. Danina solidarnościowa 4% jest deklarowana osobno na formularzu ',
  'pit38.danina.body.after':
    ' i płatna do {date}. Dochody z pracy, działalności gospodarczej i inne kwalifikujące się dochody również wliczają się do progu 1 000 000 zł, więc rzeczywista kwota może być wyższa niż pokazana tutaj. Dywidendy i odsetki (art. 30a) nie wchodzą do podstawy daniny.',
  'pit38.danina.notTriggered':
    'Danina solidarnościowa (4% powyżej 1 000 000 zł, DSF-1) nie powstaje z samego tego dochodu z zysków kapitałowych. Jeśli Twoje pozostałe dochody (praca, działalność) przekroczą łącznie próg, kwota z {podstawa} ({amount}) wlicza się do niego.',

  'pit38.fieldTable.colPoz': 'Poz.',
  'pit38.fieldTable.colName': 'Nazwa pola',
  'pit38.fieldTable.colValue': 'Wartość',

  'pit38.guide.title': 'Przewodnik krok po kroku: złożenie PIT-38 przez e-pity Kreator',
  'pit38.guide.intro.before': 'Ten przewodnik prowadzi krok po kroku przez wersję internetową ',
  'pit38.guide.intro.product': 'e-pity.pl Kreator PIT',
  'pit38.guide.intro.middle': '. Obliczone wartości są pokazane na ',
  'pit38.guide.intro.blue': 'niebiesko',
  'pit38.guide.intro.after': '.',
  'pit38.guide.click': 'Kliknij ',
  'pit38.guide.next': 'DALEJ',
  'pit38.guide.enter': 'wpisz',
  'pit38.guide.leaveEmpty': 'Pozostaw puste.',

  'pit38.guide.step1.title': 'Sposób opodatkowania',
  'pit38.guide.step1.goTo': 'Wejdź na ',
  'pit38.guide.step1.sidebar': ' (w lewym panelu bocznym). Możesz też przejść bezpośrednio do Kreatora pod adresem ',
  'pit38.guide.step1.opt1': 'Sposób opodatkowania: wybierz ',
  'pit38.guide.step1.opt1Value': 'indywidualnie',
  'pit38.guide.step1.opt2': 'Cel złożenia deklaracji: wybierz ',
  'pit38.guide.step1.opt2Value': 'złożenie zeznania',
  'pit38.guide.step1.opt3':
    'Opcjonalnie możesz zaznaczyć „Pobierz Twój e-PIT”, aby zaimportować wstępnie wypełnione dane z podatki.gov.pl',

  'pit38.guide.step2.title': 'Dane podatnika',
  'pit38.guide.step2.body':
    'Wpisz lub zweryfikuj swoje dane osobowe: PESEL, imię i nazwisko, datę urodzenia oraz adres zamieszkania na dzień 31 grudnia {year} r. Jeśli korzystałeś już z e-pity, mogą być wypełnione automatycznie.',

  'pit38.guide.step3.title': 'Przychody podatnika — dodaj źródło przychodu',
  'pit38.guide.step3.p1.before': 'Na ekranie „Dodaj przychody podatnika” kliknij przycisk ',
  'pit38.guide.step3.p1.button': '+ POZOSTAŁE',
  'pit38.guide.step3.p1.after': '.',
  'pit38.guide.step3.p2': 'Z listy rozwijanej, w sekcji „Pozostałe przychody”, wybierz:',
  'pit38.guide.step3.option': 'Przychody kapitałowe ze sprzedaży udziałów (PIT-38)',
  'pit38.guide.step3.warn': 'NIE klikaj „PIT-8C” — zagraniczni brokerzy nie wystawiają PIT-8C.',

  'pit38.guide.step4.title': 'Przychody podatnika — wpisz wartości zysków kapitałowych',
  'pit38.guide.step4.intro':
    'Po wybraniu przychodu PIT-38 Kreator pokazuje dwa pola. Odpowiadają one {inne} PIT-38 (wiersz „Inne przychody”):',
  'pit38.guide.step4.field1.label': 'Wpisz przychody z części E podlegające opodatkowaniu',
  'pit38.guide.step4.field1.help': 'To łączny przychód ze sprzedaży papierów wartościowych w PLN ({innePrzychody}).',
  'pit38.guide.step4.field2.label': 'Wpisz koszty związane z przychodami z części E',
  'pit38.guide.step4.field2.help': 'To łączny koszt nabycia (FIFO) plus prowizje w PLN ({inneKoszty}).',
  'pit38.guide.step4.zeroHint': ' Wpisz 0, jeśli w tym roku nie było sprzedaży.',
  'pit38.guide.step4.noSales':
    'W {year} r. nie było sprzedaży, więc oba pola wynoszą 0. PIT-38 nadal trzeba złożyć, aby wykazać zagraniczne dywidendy.',

  'pit38.guide.step5.title': 'PIT-38 — obliczenie zobowiązania podatkowego',
  'pit38.guide.step5.intro': 'To główny ekran obliczenia podatku. Wypełnij poniższe pola od góry do dołu:',
  'pit38.guide.step5.enter': 'Wpisz ',
  'pit38.guide.step5.loss.after':
    ' ({straty}) — obliczone ze strat wpisanych przy imporcie, po sprawdzeniu 5-letniego okna i limitów rocznych, ograniczone do dochodu z tego roku.',
  'pit38.guide.step5.loss.none':
    'Jeśli masz nieodliczone straty PIT-38 z ostatnich 5 lat, wpisz je na stronie Import, aby Pitly sprawdził limity i wypełnił to pole. W przeciwnym razie pozostaw puste.',
  'pit38.guide.step5.flat.label': 'Zryczałtowany podatek dochodowy',
  'pit38.guide.step5.flat.body': 'Pozostaw puste (to pole dotyczy innych podatków zryczałtowanych niepobranych przez płatnika, {niepobrany}).',
  'pit38.guide.step5.foreignFlat.label': 'Zryczałtowany podatek od przychodów uzyskanych poza granicami RP',
  'pit38.guide.step5.foreignFlat.after': ' ({zrycz})',
  'pit38.guide.step5.foreignFlat.help': 'To 19% łącznych dywidend brutto w PLN ({amount})',
  'pit38.guide.step5.foreignFlat.none': 'Pozostaw puste — brak zagranicznych dywidend do wykazania.',
  'pit38.guide.step5.paidAbroad.label': 'Kwoty podatku zapłaconego za granicą (przeliczone na złote)',
  'pit38.guide.step5.paidAbroad.after': ' ({zaplacony})',
  'pit38.guide.step5.paidAbroad.help':
    'Podatek u źródła pobrany przez brokera, przeliczony na PLN i ograniczony do stawki z umowy dla kraju każdego emitenta (np. 15% dla akcji z USA). Nie może przekroczyć {zrycz}.',
  'pit38.guide.step5.rest.label': 'Suma zaliczek / Dochody zwolnione z IPO / rozbicie miesięczne',
  'pit38.guide.step5.rest.body': 'Pozostałe pola pozostaw puste (chyba że masz inne szczególne okoliczności).',

  'pit38.guide.step6.title': 'Przekaż 1,5% podatku (opcjonalnie)',
  'pit38.guide.step6.body':
    'Możesz przekazać 1,5% swojego podatku organizacji pożytku publicznego (OPP). Wpisz numer KRS wybranej organizacji. Nie zwiększa to podatku — przekierowuje część kwoty, którą i tak musisz zapłacić. Pomiń, jeśli nie jesteś zainteresowany.',

  'pit38.guide.step7.title': 'Podsumowanie — sprawdź i wyślij',
  'pit38.guide.step7.intro': 'Ekran podsumowania pokazuje obliczony podatek:',
  'pit38.guide.step7.income': 'Kwota dochodu: ',
  'pit38.guide.step7.toPay': 'Kwota do zapłaty: ',
  'pit38.guide.step7.fromHere': 'Stąd możesz:',
  'pit38.guide.step7.preview.button': 'ZOBACZ DEKLARACJĘ',
  'pit38.guide.step7.preview.after': ', aby podejrzeć pełny formularz PIT-38 i zweryfikować wszystkie numery pól',
  'pit38.guide.step7.send.button': 'WYŚLIJ E-DEKLARACJĘ',
  'pit38.guide.step7.send.after': ', aby wysłać deklarację elektronicznie do urzędu skarbowego',
  'pit38.guide.step7.upo.before': 'Po wysłaniu zapisz ',
  'pit38.guide.step7.upo.after': ' (Urzędowe Poświadczenie Odbioru) — Twoje oficjalne potwierdzenie',
  'pit38.guide.step7.pay.button': 'ZAPŁAĆ PODATEK ONLINE',
  'pit38.guide.step7.pay.after': ' lub „DRUK PRZELEWU”, aby zapłacić',
  'pit38.guide.step7.deadline': 'Termin: ',
  'pit38.guide.step7.mustPay.before': ' Podatek w kwocie ',
  'pit38.guide.step7.mustPay.after': ' musi zostać zapłacony do tej samej daty.',

  'pit38.guide.notes.title': 'Ważne uwagi',
  'pit38.guide.notes.individual': 'PIT-38 składa się indywidualnie — wspólne rozliczenie z małżonkiem nie jest możliwe dla tego formularza',
  'pit38.guide.notes.mustFile.before': 'Nawet jeśli masz tylko straty albo tylko dywidendy, ',
  'pit38.guide.notes.mustFile.strong': 'nadal musisz złożyć',
  'pit38.guide.notes.mustFile.after': ' PIT-38',
  'pit38.guide.notes.losses':
    'Straty z zysków kapitałowych można odliczać przez kolejne 5 lat (maks. 50% straty z danego roku rocznie)',
  'pit38.guide.notes.nbp':
    'Wszystkie kwoty w PLN przeliczono po średnich kursach NBP (tabela A) z ostatniego dnia roboczego przed każdą transakcją',
  'pit38.guide.notes.zg': 'e-pity automatycznie generuje załącznik PIT/ZG dla dochodów zagranicznych przy wysyłce',

  'pit38.lossCheck.title': 'Sprawdzenie strat z lat ubiegłych ({straty}, art. 9 ust. 3)',
  'pit38.lossCheck.colYear': 'Rok straty',
  'pit38.lossCheck.colLoss': 'Strata',
  'pit38.lossCheck.colDeductedBefore': 'Odliczono wcześniej',
  'pit38.lossCheck.colAvailable': 'Dostępne',
  'pit38.lossCheck.colDeductedIn': 'Odliczono w {year}',
  'pit38.lossCheck.colLeftAfter': 'Pozostało po {year}',

  'pit38.zg.title': 'PIT/ZG — dochody zagraniczne według kraju (załącznik do PIT-38)',
  'pit38.zg.intro.before': 'Jeden PIT/ZG na każdy kraj. W części C.3 (art. 30b) wpisz ',
  'pit38.zg.intro.dochod': 'dochód',
  'pit38.zg.intro.after':
    ' oraz podatek zapłacony za granicą od tych sprzedaży. Kraj, dla którego wynikiem jest strata, podano tylko informacyjnie: strat nie wpisuje się do PIT/ZG, pomniejszają już sumę w części C PIT-38.',
  'pit38.zg.colCountry': 'Kraj (państwo)',
  'pit38.zg.colSales': 'Sprzedaże',
  'pit38.zg.colPrzychod': 'Przychód',
  'pit38.zg.colKoszty': 'Koszty',
  'pit38.zg.colDochod': 'Dochód (PIT/ZG)',
  'pit38.zg.colStrata': 'Strata',
  'pit38.zg.colTaxAbroad': 'Podatek zapłacony za granicą',
  'pit38.zg.noSales': 'Brak sprzedaży w okresie podatkowym, więc wiersze PIT/ZG dla dochodów z art. 30b nie są potrzebne.',
  'pit38.zg.dividendsIntro':
    'Dywidendy, odsetki i payments in lieu według kraju (tylko informacyjnie: w PIT-38 nie wpisuje się ich do PIT/ZG; podatek podlegający zaliczeniu trafia bezpośrednio do {zaplacony}).',
  'pit38.zg.divColCountry': 'Kraj',
  'pit38.zg.colPayments': 'Wypłaty',
  'pit38.zg.colGross': 'Brutto (PLN)',
  'pit38.zg.colWithheld': 'Pobrano (PLN)',
  'pit38.zg.colCreditable': 'Do zaliczenia (PLN)',
  'pit38.zg.countryNote':
    'Kraj = kraj emitenta według prefiksu ISIN. Wielu doradców przypisuje dochód do kraju giełdy; dla akcji notowanych w USA oba podejścia dają Stany Zjednoczone. ETF-y z siedzibą w Irlandii (IE) wykazano pod Irlandią.',
};

export const pit38: Translations = { en, uk, pl };
