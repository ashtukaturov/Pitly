import { Fragment, useCallback, useState } from 'react';
import { Printer, FileText, ChevronDown, ChevronUp, AlertTriangle } from 'lucide-react';
import { formatPln, formatTaxPeriod, hasCustomTaxPeriod } from '../format';
import type { AppState, Pit38Fields } from '../types';
import EmptyState from '../components/EmptyState';
import { useT, type TFunction } from '../i18n';
import { formRefs, VERIFIED_FORM_YEARS } from '../pit38Form';

// All PIT-38 values come from the backend (Pit38Fields.FromSummary, decimal arithmetic).
// Nothing is re-derived here with floating point.
// Translation function that also knows how to refer to each field of the form version for this tax year
// ("poz. 30", or the field name when no form is verified for the year), so dictionary strings can say "{straty}"
// instead of hard-coding the numbers of one form version.
function usePit38T(pit38: Pit38Fields | null): TFunction {
  const t = useT();
  const f = pit38?.form;
  return useCallback<TFunction>(
    (key, params) => t(key, { ...formRefs(f), ...params }),
    [t, f],
  );
}

export default function Pit38Page({ state }: { state: AppState }) {
  const t = usePit38T(state.pit38);
  const pit38 = state.pit38;

  if (!state.sessionId || !state.summary || !pit38) {
    return <EmptyState />;
  }
  const year = pit38.year;
  const form = pit38.form;
  // Field number as shown in the "Poz." column: a number only when the form of this tax year is verified.
  const poz = (n: number | undefined) => (n === undefined ? t('pit38.noNumber') : String(n));
  const customTaxPeriod = hasCustomTaxPeriod(state.summary.year, state.summary.taxableFrom);
  const deadline = t('pit38.deadlineDate', { year: year + 1 });

  return (
    <div className="space-y-8 max-w-4xl">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-white text-2xl font-bold">{t('pit38.title', { year })}</h1>
          <p className={form ? 'text-slate-400 mt-1' : 'text-amber-300 mt-1 max-w-2xl'}>
            {form ? t('pit38.subtitle') : t('pit38.subtitleUnverified', { year, years: VERIFIED_FORM_YEARS })}
          </p>
          <p className="text-slate-500 text-sm mt-1">
            {t('pit38.taxablePeriod', { period: formatTaxPeriod(state.summary.taxableFrom, state.summary.taxableTo) })}
          </p>
        </div>
        <button
          onClick={() => window.print()}
          className="no-print bg-blue-600 hover:bg-blue-500 text-white font-medium px-4 py-2 rounded-lg transition-colors flex items-center gap-2"
        >
          <Printer className="w-4 h-4" />
          {t('pit38.exportPdf')}
        </button>
      </div>

      {customTaxPeriod && (
        <div className="bg-amber-500/10 border border-amber-500/30 rounded-xl p-4 text-sm text-amber-200">
          {t('pit38.customPeriodNote')}
        </div>
      )}

      {pit38.needsReimport && (
        <div className="print-warning bg-red-500/10 border border-red-500/40 rounded-xl p-5 text-sm text-red-200 flex gap-3" role="alert">
          <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5 text-red-300" />
          <div>
            <p className="font-semibold text-red-100">{t('pit38.reimport.title')}</p>
            <p className="mt-1">{t('pit38.reimport.body')}</p>
          </div>
        </div>
      )}

      {pit38.dataWarnings.length > 0 && (
        <div className="print-warning bg-amber-500/10 border border-amber-500/40 rounded-xl p-5 text-sm text-amber-200" role="alert">
          <p className="font-semibold text-amber-100 flex items-center gap-2">
            <AlertTriangle className="w-4 h-4 text-amber-300" />
            {t('pit38.dataWarnings.title')}
          </p>
          <p className="mt-1 text-amber-200/80">{t('pit38.dataWarnings.intro')}</p>
          <ul className="mt-3 space-y-1.5 list-disc list-inside">
            {pit38.dataWarnings.map((warning, i) => (
              <li key={i}>{warning}</li>
            ))}
          </ul>
        </div>
      )}

      <Section title={t('pit38.sectionC.title')}>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-700">
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-left">{t('pit38.sectionC.colSource')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">{t('pit38.sectionC.colPrzychod')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">{t('pit38.sectionC.colKoszty')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">{t('pit38.sectionC.colDochod')}</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">{t('pit38.sectionC.colStrata')}</th>
              </tr>
            </thead>
            <tbody>
              <tr className="border-b border-slate-700/50">
                <td className="px-6 py-3 text-slate-500">
                  {form && <span className="text-slate-600 font-mono text-xs mr-2">poz. {form.pit8cPrzychody}, {form.pit8cKoszty}</span>}
                  PIT-8C
                </td>
                <td className="px-6 py-3 text-right font-mono text-slate-600">&mdash;</td>
                <td className="px-6 py-3 text-right font-mono text-slate-600">&mdash;</td>
                <td className="px-6 py-3"></td>
                <td className="px-6 py-3"></td>
              </tr>
              <tr className="border-b border-slate-700/50 bg-slate-700/20">
                <td className="px-6 py-3 text-slate-300">
                  {form && <span className="text-blue-400 font-mono text-xs mr-2">poz. {form.innePrzychody}, {form.inneKoszty}</span>}
                  {t('pit38.sectionC.rowInne')}
                </td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">{formatPln(pit38.innePrzychody)}</td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">{formatPln(pit38.inneKoszty)}</td>
                <td className="px-6 py-3"></td>
                <td className="px-6 py-3"></td>
              </tr>
              <tr className="border-b border-slate-700/50 bg-slate-700/30">
                <td className="px-6 py-3 text-slate-300 font-semibold">
                  {form && <span className="text-blue-400 font-mono text-xs mr-2">poz. {form.razemPrzychody}–{form.strata}</span>}
                  {t('pit38.sectionC.rowRazem')}
                </td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">{formatPln(pit38.razemPrzychody)}</td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">{formatPln(pit38.razemKoszty)}</td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">
                  {pit38.dochod > 0 ? formatPln(pit38.dochod) : <span className="text-slate-600">&mdash;</span>}
                </td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">
                  {pit38.strata > 0 ? formatPln(pit38.strata) : <span className="text-slate-600">&mdash;</span>}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div className="px-6 py-2 text-slate-500 text-xs">
          {t('pit38.sectionC.note')}
        </div>
      </Section>

      <Section title={t('pit38.sectionD.title')}>
        <FieldTable rows={[
          { poz: poz(form?.straty), name: t('pit38.sectionD.poz30.name'), value: pit38.stratyZLatUbieglych, note: t('pit38.sectionD.poz30.note') },
          { poz: poz(form?.podstawa), name: t('pit38.sectionD.poz31.name'), value: pit38.podstawaObliczenia, note: t('pit38.sectionD.poz31.note') },
          { poz: poz(form?.podatek), name: t('pit38.sectionD.poz33.name'), value: pit38.podatek },
          { poz: poz(form?.podatekNalezny), name: t('pit38.sectionD.poz35.name'), value: pit38.podatekNalezny, note: t('pit38.sectionD.poz35.note') },
        ]} />
        <PriorYearLossBreakdown pit38={pit38} />
      </Section>

      <Section title={t('pit38.sectionG.title')}>
        <div className="px-6 py-3 border-b border-slate-700/50">
          <span className="text-slate-500 text-xs">{t('pit38.sectionG.grossLabel')}</span>
          <span className="font-mono text-slate-400 text-sm">{formatPln(pit38.totalDividendsPln)}</span>
          <span className="text-slate-600 text-xs ml-2">{t('pit38.sectionG.informational')}</span>
        </div>
        <FieldTable rows={[
          { poz: poz(form?.zryczaltowany), name: t('pit38.sectionG.poz47.name'), value: pit38.zryczaltowanyPodatek },
          { poz: poz(form?.zaplacony), name: t('pit38.sectionG.poz48.name'), value: pit38.podatekZaplaconyZaGranica, note: t('pit38.sectionG.poz48.note') },
          { poz: poz(form?.roznica), name: t('pit38.sectionG.poz49.name'), value: pit38.roznica, note: t('pit38.sectionG.poz49.note') },
        ]} />
      </Section>

      <div className="bg-blue-500/10 border-2 border-blue-500 rounded-xl p-8 text-center">
        <FileText className="w-10 h-10 text-blue-400 mx-auto mb-3" />
        {form && <p className="text-slate-500 text-xs font-mono mb-1">Poz. {form.doZaplaty}</p>}
        <p className="text-blue-300 text-sm mb-1">{t('pit38.total.label')}</p>
        <p className="text-white text-3xl font-bold font-mono tabular-nums">{formatPln(pit38.podatekDoZaplaty)}</p>
        <p className="text-slate-400 text-xs mt-1">{t('pit38.total.formula')}</p>
        <p className="text-slate-400 text-sm mt-2">{t('pit38.total.deadline', { date: deadline })}</p>
      </div>

      {pit38.daninaSolidarnosciowaPln > 0 ? (
        <div className="bg-red-500/10 border border-red-500/40 rounded-xl p-5 text-sm text-red-200 flex gap-3">
          <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5 text-red-300" />
          <div>
            <p className="font-semibold text-red-100">
              {t('pit38.danina.title', { amount: formatPln(pit38.daninaSolidarnosciowaPln) })}
            </p>
            <p className="mt-1">
              {t('pit38.danina.body.before', { amount: formatPln(pit38.daninaSolidarnosciowaNadwyzka) })}
              <strong className="text-red-100">DSF-1</strong>
              {t('pit38.danina.body.after', { date: deadline })}
            </p>
          </div>
        </div>
      ) : (
        <p className="text-slate-500 text-xs px-1">
          {t('pit38.danina.notTriggered', { amount: formatPln(pit38.podstawaObliczenia) })}
        </p>
      )}

      <PitZgSection pit38={pit38} />

      <EpityGuide pit38={pit38} year={year} hasDividends={pit38.totalDividendsPln > 0} />
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden">
      <div className="px-6 py-4 border-b border-slate-700">
        <h2 className="text-white text-lg font-semibold">{title}</h2>
      </div>
      {children}
    </div>
  );
}

function FieldTable({ rows }: { rows: { poz: string; name: string; value: number; note?: string }[] }) {
  const t = useT();
  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="border-b border-slate-700">
          <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-left">{t('pit38.fieldTable.colPoz')}</th>
          <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-left">{t('pit38.fieldTable.colName')}</th>
          <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">{t('pit38.fieldTable.colValue')}</th>
        </tr>
      </thead>
      <tbody>
        {rows.map(({ poz, name, value, note }, index) => (
          <tr key={index} className="border-b border-slate-700/50 hover:bg-slate-700/30 transition-colors">
            <td className="px-6 py-3 text-blue-400 font-mono font-medium">{poz}</td>
            <td className="px-6 py-3">
              <span className="text-slate-300">{name}</span>
              {note && <span className="block text-slate-500 text-xs mt-0.5">{note}</span>}
            </td>
            <td className="px-6 py-3 text-right font-mono tabular-nums text-white font-medium">
              {formatPln(value)}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function Val({ v }: { v: number }) {
  return <span className="font-mono text-blue-400 font-medium">{formatPln(v)}</span>;
}

function EpityGuide({ pit38, year, hasDividends }: { pit38: Pit38Fields; year: number; hasDividends: boolean }) {
  const t = usePit38T(pit38);
  const [open, setOpen] = useState(false);
  const hasCapitalGains = pit38.innePrzychody > 0 || pit38.inneKoszty > 0;
  const clickNext = (
    <p>{t('pit38.guide.click')}<strong className="text-white">{t('pit38.guide.next')}</strong>.</p>
  );

  return (
    <div className="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden">
      <button
        onClick={() => setOpen(!open)}
        className="w-full px-6 py-4 flex items-center justify-between text-left hover:bg-slate-700/30 transition-colors"
      >
        <h2 className="text-white text-lg font-semibold">
          {t('pit38.guide.title')}
        </h2>
        {open
          ? <ChevronUp className="w-5 h-5 text-slate-400 shrink-0" />
          : <ChevronDown className="w-5 h-5 text-slate-400 shrink-0" />}
      </button>

      {open && (
        <div className="px-6 pb-6 space-y-6 border-t border-slate-700">
          <p className="text-slate-400 text-sm pt-4">
            {t('pit38.guide.intro.before')}
            <strong className="text-slate-300">{t('pit38.guide.intro.product')}</strong>
            {t('pit38.guide.intro.middle')}
            <span className="text-blue-400">{t('pit38.guide.intro.blue')}</span>
            {t('pit38.guide.intro.after')}
          </p>

          {/* Step 1 */}
          <GuideStep n={1} title={t('pit38.guide.step1.title')}>
            <p>
              {t('pit38.guide.step1.goTo')}<span className="text-blue-400">e-pity.pl</span> &rarr; <strong className="text-white">Kreator
                PIT</strong>{t('pit38.guide.step1.sidebar')}<span className="text-blue-400">e-pity.pl/pit-online/kreator-{year}/</span>.
            </p>
            <ul className="list-disc list-inside space-y-1 mt-2">
              <li>{t('pit38.guide.step1.opt1')}<strong className="text-white">{t('pit38.guide.step1.opt1Value')}</strong></li>
              <li>{t('pit38.guide.step1.opt2')}<strong className="text-white">{t('pit38.guide.step1.opt2Value')}</strong></li>
              <li>{t('pit38.guide.step1.opt3')}</li>
            </ul>
            {clickNext}
          </GuideStep>

          {/* Step 2 */}
          <GuideStep n={2} title={t('pit38.guide.step2.title')}>
            <p>
              {t('pit38.guide.step2.body', { year })}
            </p>
            {clickNext}
          </GuideStep>

          {/* Step 3 */}
          <GuideStep n={3} title={t('pit38.guide.step3.title')}>
            <p>
              {t('pit38.guide.step3.p1.before')}
              <strong className="text-white">{t('pit38.guide.step3.p1.button')}</strong>
              {t('pit38.guide.step3.p1.after')}
            </p>
            <p>
              {t('pit38.guide.step3.p2')}<br />
              <strong className="text-white">{t('pit38.guide.step3.option')}</strong>
            </p>
            <p className="text-slate-500 text-xs">
              {t('pit38.guide.step3.warn')}
            </p>
          </GuideStep>

          {/* Step 4 */}
          <GuideStep n={4} title={t('pit38.guide.step4.title')}>
            <p>
              {t('pit38.guide.step4.intro')}
            </p>
            <ul className="list-disc list-inside space-y-2 mt-2">
              <li>
                <strong className="text-white">&quot;{t('pit38.guide.step4.field1.label')}&quot;</strong>
                {' '}&rarr; {t('pit38.guide.enter')} <Val v={pit38.innePrzychody} />
                <span className="block text-slate-500 text-xs ml-6">
                  {t('pit38.guide.step4.field1.help')}
                  {!hasCapitalGains && t('pit38.guide.step4.zeroHint')}
                </span>
              </li>
              <li>
                <strong className="text-white">&quot;{t('pit38.guide.step4.field2.label')}&quot;</strong>
                {' '}&rarr; {t('pit38.guide.enter')} <Val v={pit38.inneKoszty} />
                <span className="block text-slate-500 text-xs ml-6">
                  {t('pit38.guide.step4.field2.help')}
                  {!hasCapitalGains && t('pit38.guide.step4.zeroHint')}
                </span>
              </li>
            </ul>
            {!hasCapitalGains && (
              <div className="bg-slate-700/30 rounded-lg p-3 mt-2">
                <p className="text-slate-400 text-xs">
                  {t('pit38.guide.step4.noSales', { year })}
                </p>
              </div>
            )}
            <p className="mt-2">{t('pit38.guide.click')}<strong className="text-white">{t('pit38.guide.next')}</strong>.</p>
          </GuideStep>

          {/* Step 5 */}
          <GuideStep n={5} title={t('pit38.guide.step5.title')}>
            <p>{t('pit38.guide.step5.intro')}</p>

            <div className="space-y-4 mt-3">
              <div>
                <p className="text-white font-medium text-xs uppercase tracking-wider mb-1">{t('pit38.sectionD.poz30.name')}</p>
                {pit38.stratyZLatUbieglych > 0 ? (
                  <p>
                    {t('pit38.guide.step5.enter')}<Val v={pit38.stratyZLatUbieglych} />{t('pit38.guide.step5.loss.after')}
                  </p>
                ) : (
                  <p>
                    {t('pit38.guide.step5.loss.none')}
                  </p>
                )}
              </div>

              <div>
                <p className="text-white font-medium text-xs uppercase tracking-wider mb-1">{t('pit38.guide.step5.flat.label')}</p>
                <p>{t('pit38.guide.step5.flat.body')}</p>
              </div>

              <div>
                <p className="text-white font-medium text-xs uppercase tracking-wider mb-1">
                  {t('pit38.guide.step5.foreignFlat.label')}
                </p>
                {hasDividends ? (
                  <p>
                    {t('pit38.guide.step5.enter')}<Val v={pit38.zryczaltowanyPodatek} />{t('pit38.guide.step5.foreignFlat.after')}
                    <span className="block text-slate-500 text-xs">
                      {t('pit38.guide.step5.foreignFlat.help', { amount: formatPln(pit38.totalDividendsPln) })}
                    </span>
                  </p>
                ) : (
                  <p>{t('pit38.guide.step5.foreignFlat.none')}</p>
                )}
              </div>

              <div>
                <p className="text-white font-medium text-xs uppercase tracking-wider mb-1">
                  {t('pit38.guide.step5.paidAbroad.label')}
                </p>
                {hasDividends ? (
                  <p>
                    {t('pit38.guide.step5.enter')}<Val v={pit38.podatekZaplaconyZaGranica} />{t('pit38.guide.step5.paidAbroad.after')}
                    <span className="block text-slate-500 text-xs">
                      {t('pit38.guide.step5.paidAbroad.help')}
                    </span>
                  </p>
                ) : (
                  <p>{t('pit38.guide.leaveEmpty')}</p>
                )}
              </div>

              <div>
                <p className="text-white font-medium text-xs uppercase tracking-wider mb-1">
                  {t('pit38.guide.step5.rest.label')}
                </p>
                <p>{t('pit38.guide.step5.rest.body')}</p>
              </div>
            </div>
            <p className="mt-2">{t('pit38.guide.click')}<strong className="text-white">{t('pit38.guide.next')}</strong>.</p>
          </GuideStep>

          {/* Step 6 */}
          <GuideStep n={6} title={t('pit38.guide.step6.title')}>
            <p>
              {t('pit38.guide.step6.body')}
            </p>
            {clickNext}
          </GuideStep>

          {/* Step 7 */}
          <GuideStep n={7} title={t('pit38.guide.step7.title')}>
            <p>{t('pit38.guide.step7.intro')}</p>
            <ul className="list-disc list-inside space-y-1 mt-2">
              <li>{t('pit38.guide.step7.income')}{hasCapitalGains ? formatPln(pit38.dochod) : '0,00 zł'}</li>
              <li>{t('pit38.guide.step7.toPay')}<Val v={pit38.podatekDoZaplaty} /></li>
            </ul>
            <p className="mt-3">{t('pit38.guide.step7.fromHere')}</p>
            <ul className="list-disc list-inside space-y-2 mt-1">
              <li>
                {t('pit38.guide.click')}<strong className="text-white">&quot;{t('pit38.guide.step7.preview.button')}&quot;</strong>
                {t('pit38.guide.step7.preview.after')}
              </li>
              <li>
                {t('pit38.guide.click')}<strong className="text-white">&quot;{t('pit38.guide.step7.send.button')}&quot;</strong>
                {t('pit38.guide.step7.send.after')}
              </li>
              <li>
                {t('pit38.guide.step7.upo.before')}<strong className="text-white">UPO</strong>
                {t('pit38.guide.step7.upo.after')}
              </li>
              {pit38.podatekDoZaplaty > 0 && (
                <li>
                  {t('pit38.guide.click')}<strong className="text-white">&quot;{t('pit38.guide.step7.pay.button')}&quot;</strong>
                  {t('pit38.guide.step7.pay.after')}
                </li>
              )}
            </ul>
            <p className="mt-2">
              {t('pit38.guide.step7.deadline')}<strong className="text-white">{t('pit38.deadlineDate', { year: year + 1 })}</strong>.
              {pit38.podatekDoZaplaty > 0 && (
                <span>{t('pit38.guide.step7.mustPay.before')}<Val v={pit38.podatekDoZaplaty} />{t('pit38.guide.step7.mustPay.after')}</span>
              )}
            </p>
          </GuideStep>

          <div className="bg-amber-500/10 border border-amber-500/30 rounded-lg p-4 text-sm">
            <p className="text-amber-300 font-medium mb-1">{t('pit38.guide.notes.title')}</p>
            <ul className="list-disc list-inside space-y-1 text-slate-400">
              <li>{t('pit38.guide.notes.individual')}</li>
              <li>
                {t('pit38.guide.notes.mustFile.before')}
                <strong className="text-slate-300">{t('pit38.guide.notes.mustFile.strong')}</strong>
                {t('pit38.guide.notes.mustFile.after')}
              </li>
              <li>{t('pit38.guide.notes.losses')}</li>
              <li>{t('pit38.guide.notes.nbp')}</li>
              <li>{t('pit38.guide.notes.zg')}</li>
            </ul>
          </div>
        </div>
      )}
    </div>
  );
}

function GuideStep({ n, title, children }: { n: number; title: string; children: React.ReactNode }) {
  return (
    <div>
      <h3 className="text-white font-semibold flex items-center gap-3 mb-2">
        <span className="bg-blue-600 text-white text-xs font-bold w-6 h-6 rounded-full flex items-center justify-center shrink-0">
          {n}
        </span>
        {title}
      </h3>
      <div className="text-slate-300 text-sm space-y-2 ml-9">
        {children}
      </div>
    </div>
  );
}

// Per-year check of the prior-year losses entered at import (computed by PriorYearLossCalculator on the backend).
function PriorYearLossBreakdown({ pit38 }: { pit38: Pit38Fields }) {
  const t = usePit38T(pit38);
  if (pit38.priorYearLosses.length === 0 && pit38.warnings.length === 0) return null;
  const th = 'text-slate-500 text-xs font-medium px-3 py-2';
  const td = 'px-3 py-2 font-mono tabular-nums text-right';

  return (
    <div className="border-t border-slate-700/50">
      {pit38.priorYearLosses.length > 0 && (
        <div className="px-6 py-4 overflow-x-auto">
          <p className="text-slate-400 text-xs uppercase tracking-wider font-medium mb-2">
            {t('pit38.lossCheck.title')}
          </p>
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-700/50">
                <th className={`${th} text-left`}>{t('pit38.lossCheck.colYear')}</th>
                <th className={`${th} text-right`}>{t('pit38.lossCheck.colLoss')}</th>
                <th className={`${th} text-right`}>{t('pit38.lossCheck.colDeductedBefore')}</th>
                <th className={`${th} text-right`}>{t('pit38.lossCheck.colAvailable')}</th>
                <th className={`${th} text-right`}>{t('pit38.lossCheck.colDeductedIn', { year: pit38.year })}</th>
                <th className={`${th} text-right`}>{t('pit38.lossCheck.colLeftAfter', { year: pit38.year })}</th>
              </tr>
            </thead>
            <tbody>
              {pit38.priorYearLosses.map((line) => {
                const problem = !line.eligible || line.expiresAfterThisYear;
                const muted = !line.eligible ? 'text-slate-500' : 'text-slate-200';
                return (
                  <Fragment key={line.year}>
                    <tr className={muted}>
                      <td className="px-3 py-2 font-mono">{line.year}</td>
                      <td className={td}>{formatPln(line.lossPln)}</td>
                      <td className={td}>{formatPln(line.deductedPln)}</td>
                      <td className={td}>{formatPln(line.remainingPln)}</td>
                      <td className={`${td} ${line.appliedPln > 0 ? 'text-green-400 font-medium' : ''}`}>{formatPln(line.appliedPln)}</td>
                      <td className={`${td} ${line.expiresAfterThisYear ? 'text-amber-400' : ''}`}>{formatPln(line.unusedPln)}</td>
                    </tr>
                    {line.notes.length > 0 && (
                      <tr>
                        <td colSpan={6} className={`px-3 pb-3 text-xs ${problem ? 'text-amber-400' : 'text-slate-500'}`}>
                          {line.notes.map((note, i) => (
                            <span key={i} className="block">{note}</span>
                          ))}
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {pit38.warnings.length > 0 && (
        <div className="mx-6 mb-4 bg-amber-500/10 border border-amber-500/30 rounded-lg p-4 text-sm text-amber-200 space-y-2">
          {pit38.warnings.map((warning, i) => (
            <div key={i} className="flex gap-2">
              <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5 text-amber-300" />
              <span>{warning}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// PIT/ZG annex: art. 30b income per issuer country, computed on the backend (CountryBreakdown).
function PitZgSection({ pit38 }: { pit38: Pit38Fields }) {
  const t = usePit38T(pit38);
  const rows = pit38.pitZg;
  if (rows.length === 0) return null;
  const capitalRows = rows.filter(r => r.saleCount > 0);
  const dividendRows = rows.filter(r => r.dividendCount > 0);
  const th = 'text-slate-400 text-xs uppercase tracking-wider font-medium px-4 py-3';
  const td = 'px-4 py-2.5 font-mono tabular-nums text-right';

  return (
    <Section title={t('pit38.zg.title')}>
      {capitalRows.length > 0 ? (
        <>
          <div className="px-6 pt-4 pb-2 text-slate-400 text-sm">
            {t('pit38.zg.intro.before')}<span className="text-slate-200">{t('pit38.zg.intro.dochod')}</span>{t('pit38.zg.intro.after')}
          </div>
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-700">
                <th className={`${th} text-left`}>{t('pit38.zg.colCountry')}</th>
                <th className={`${th} text-right`}>{t('pit38.zg.colSales')}</th>
                <th className={`${th} text-right`}>{t('pit38.zg.colPrzychod')}</th>
                <th className={`${th} text-right`}>{t('pit38.zg.colKoszty')}</th>
                <th className={`${th} text-right`}>{t('pit38.zg.colDochod')}</th>
                <th className={`${th} text-right`}>{t('pit38.zg.colStrata')}</th>
                <th className={`${th} text-right`}>{t('pit38.zg.colTaxAbroad')}</th>
              </tr>
            </thead>
            <tbody>
              {capitalRows.map((r) => (
                <tr key={r.countryCode} className="border-b border-slate-700/50 hover:bg-slate-700/30 transition-colors">
                  <td className="px-4 py-2.5 text-slate-200">
                    {r.countryName}
                    <span className="ml-2 text-xs font-mono text-slate-500">{r.countryCode}</span>
                  </td>
                  <td className={`${td} text-slate-400`}>{r.saleCount}</td>
                  <td className={`${td} text-slate-300`}>{formatPln(r.proceedsPln)}</td>
                  <td className={`${td} text-slate-300`}>{formatPln(r.costPln)}</td>
                  <td className={`${td} ${r.incomePln > 0 ? 'text-white font-medium' : 'text-slate-500'}`}>{formatPln(r.incomePln)}</td>
                  <td className={`${td} ${r.lossPln > 0 ? 'text-red-400' : 'text-slate-500'}`}>{formatPln(r.lossPln)}</td>
                  <td className={`${td} text-slate-300`}>{formatPln(r.taxPaidAbroadPln)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      ) : (
        <div className="px-6 py-4 text-slate-400 text-sm">
          {t('pit38.zg.noSales')}
        </div>
      )}

      {dividendRows.length > 0 && (
        <div className="border-t border-slate-700/50">
          <div className="px-6 pt-4 pb-2 text-slate-500 text-xs">
            {t('pit38.zg.dividendsIntro')}
          </div>
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-700/50">
                <th className={`${th} text-left`}>{t('pit38.zg.divColCountry')}</th>
                <th className={`${th} text-right`}>{t('pit38.zg.colPayments')}</th>
                <th className={`${th} text-right`}>{t('pit38.zg.colGross')}</th>
                <th className={`${th} text-right`}>{t('pit38.zg.colWithheld')}</th>
                <th className={`${th} text-right`}>{t('pit38.zg.colCreditable')}</th>
              </tr>
            </thead>
            <tbody>
              {dividendRows.map((r) => (
                <tr key={r.countryCode} className="border-b border-slate-700/50">
                  <td className="px-4 py-2 text-slate-300">
                    {r.countryName}
                    <span className="ml-2 text-xs font-mono text-slate-500">{r.countryCode}</span>
                  </td>
                  <td className={`${td} text-slate-400`}>{r.dividendCount}</td>
                  <td className={`${td} text-slate-300`}>{formatPln(r.dividendsPln)}</td>
                  <td className={`${td} text-slate-300`}>{formatPln(r.withholdingPln)}</td>
                  <td className={`${td} text-amber-300`}>{formatPln(r.creditableWithholdingPln)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="px-6 py-3 text-slate-500 text-xs">
        {t('pit38.zg.countryNote')}
      </div>
    </Section>
  );
}
