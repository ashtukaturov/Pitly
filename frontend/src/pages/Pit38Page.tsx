import { useMemo, useState } from 'react';
import { Printer, FileText, ChevronDown, ChevronUp } from 'lucide-react';
import { formatPln } from '../format';
import type { AppState, Pit38Fields, TaxSummary } from '../types';
import EmptyState from '../components/EmptyState';

const TAX_RATE = 0.19;

function roundToFullPln(value: number): number {
  return Math.round(value);
}

function buildPit38(s: TaxSummary): Pit38Fields {
  const przychody = s.totalProceedsPln;
  const koszty = s.totalCostPln;
  const difference = przychody - koszty;
  const dochod = Math.max(difference, 0);
  const strata = Math.max(-difference, 0);

  const podstawa = roundToFullPln(dochod);
  const podatek = Math.round(podstawa * TAX_RATE * 100) / 100;
  const podatekNalezny = roundToFullPln(podatek);

  const zryczaltowanyPodatek = Math.round(s.totalDividendsPln * TAX_RATE * 100) / 100;
  const podatekZaGranica = Math.round(Math.min(s.totalWithholdingPln, zryczaltowanyPodatek) * 100) / 100;
  const roznica = roundToFullPln(Math.max(zryczaltowanyPodatek - podatekZaGranica, 0));
  const podatekDoZaplaty = podatekNalezny + roznica;

  return {
    year: s.year,
    poz22Przychody: przychody,
    poz23Koszty: koszty,
    poz24RazemPrzychody: przychody,
    poz25RazemKoszty: koszty,
    poz26Dochod: Math.round(dochod * 100) / 100,
    poz27Strata: Math.round(strata * 100) / 100,
    poz29PodstawaObliczenia: podstawa,
    poz31Podatek: podatek,
    poz33PodatekNalezny: podatekNalezny,
    poz45ZryczaltowanyPodatek: zryczaltowanyPodatek,
    poz46PodatekZaplaconyZaGranica: podatekZaGranica,
    poz47Roznica: roznica,
    poz49PodatekDoZaplaty: podatekDoZaplaty,
    totalDividendsPln: s.totalDividendsPln,
  };
}

export default function Pit38Page({ state }: { state: AppState }) {
  if (!state.sessionId || !state.summary) {
    return <EmptyState />;
  }

  const pit38 = useMemo(() => buildPit38(state.summary!), [state.summary]);
  const year = pit38.year;

  return (
    <div className="space-y-8 max-w-4xl">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-white text-2xl font-bold">Your PIT-38 Declaration for {year}</h1>
          <p className="text-slate-400 mt-1">
            Field numbers (poz.) match the official PIT-38(17) form.
          </p>
        </div>
        <button
          onClick={() => window.print()}
          className="no-print bg-blue-600 hover:bg-blue-500 text-white font-medium px-4 py-2 rounded-lg transition-colors flex items-center gap-2"
        >
          <Printer className="w-4 h-4" />
          Export as PDF
        </button>
      </div>

      <Section title="C. Dochody / Straty — art. 30b ust. 1 ustawy">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-700">
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-left">Source</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">Przychod (b)</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">Koszty (c)</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">Dochod (d)</th>
                <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">Strata (e)</th>
              </tr>
            </thead>
            <tbody>
              <tr className="border-b border-slate-700/50">
                <td className="px-6 py-3 text-slate-500">
                  <span className="text-slate-600 font-mono text-xs mr-2">poz. 20, 21</span>
                  PIT-8C
                </td>
                <td className="px-6 py-3 text-right font-mono text-slate-600">&mdash;</td>
                <td className="px-6 py-3 text-right font-mono text-slate-600">&mdash;</td>
                <td className="px-6 py-3"></td>
                <td className="px-6 py-3"></td>
              </tr>
              <tr className="border-b border-slate-700/50 bg-slate-700/20">
                <td className="px-6 py-3 text-slate-300">
                  <span className="text-blue-400 font-mono text-xs mr-2">poz. 22, 23</span>
                  Inne przychody
                </td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">{formatPln(pit38.poz22Przychody)}</td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">{formatPln(pit38.poz23Koszty)}</td>
                <td className="px-6 py-3"></td>
                <td className="px-6 py-3"></td>
              </tr>
              <tr className="border-b border-slate-700/50 bg-slate-700/30">
                <td className="px-6 py-3 text-slate-300 font-semibold">
                  <span className="text-blue-400 font-mono text-xs mr-2">poz. 24–27</span>
                  Razem
                </td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">{formatPln(pit38.poz24RazemPrzychody)}</td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">{formatPln(pit38.poz25RazemKoszty)}</td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">
                  {pit38.poz26Dochod > 0 ? formatPln(pit38.poz26Dochod) : <span className="text-slate-600">&mdash;</span>}
                </td>
                <td className="px-6 py-3 text-right font-mono text-white font-medium">
                  {pit38.poz27Strata > 0 ? formatPln(pit38.poz27Strata) : <span className="text-slate-600">&mdash;</span>}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div className="px-6 py-2 text-slate-500 text-xs">
          IB does not issue PIT-8C — all income goes to row 2 &quot;Inne przychody&quot; (poz. 22-23).
        </div>
      </Section>

      <Section title="D. Obliczenie zobowiazania podatkowego — art. 30b ust. 1 ustawy">
        <FieldTable rows={[
          { poz: '29', name: 'Podstawa obliczenia podatku', value: pit38.poz29PodstawaObliczenia, note: 'Poz. 26 minus straty z lat ubieglych, zaokraglone do pelnych zl' },
          { poz: '31', name: 'Podatek (poz. 29 \u00d7 19%)', value: pit38.poz31Podatek },
          { poz: '33', name: 'Podatek nalezny', value: pit38.poz33PodatekNalezny, note: 'Po zaokragleniu do pelnych zl' },
        ]} />
      </Section>

      <Section title="G. Podatek do zaplaty — dywidendy zagraniczne (art. 30a)">
        <div className="px-6 py-3 border-b border-slate-700/50">
          <span className="text-slate-500 text-xs">Gross dividends received: </span>
          <span className="font-mono text-slate-400 text-sm">{formatPln(pit38.totalDividendsPln)}</span>
          <span className="text-slate-600 text-xs ml-2">(informational — not entered on PIT-38)</span>
        </div>
        <FieldTable rows={[
          { poz: '45', name: 'Zryczaltowany podatek 19% od dywidend zagranicznych', value: pit38.poz45ZryczaltowanyPodatek },
          { poz: '46', name: 'Podatek zaplacony za granica (US withholding)', value: pit38.poz46PodatekZaplaconyZaGranica, note: 'Nie moze przekroczyc kwoty z poz. 45' },
          { poz: '47', name: 'Roznica (poz. 45 \u2212 poz. 46)', value: pit38.poz47Roznica, note: 'Po zaokragleniu do pelnych zl' },
        ]} />
      </Section>

      <div className="bg-blue-500/10 border-2 border-blue-500 rounded-xl p-8 text-center">
        <FileText className="w-10 h-10 text-blue-400 mx-auto mb-3" />
        <p className="text-slate-500 text-xs font-mono mb-1">Poz. 49</p>
        <p className="text-blue-300 text-sm mb-1">PODATEK DO ZAPLATY</p>
        <p className="text-white text-3xl font-bold font-mono tabular-nums">{formatPln(pit38.poz49PodatekDoZaplaty)}</p>
        <p className="text-slate-400 text-xs mt-1">= poz. 33 + poz. 47</p>
        <p className="text-slate-400 text-sm mt-2">Deadline: April 30, {year + 1}</p>
      </div>

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
  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="border-b border-slate-700">
          <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-left">Poz.</th>
          <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-left">Field Name</th>
          <th className="text-slate-400 text-xs uppercase tracking-wider font-medium px-6 py-3 text-right">Value</th>
        </tr>
      </thead>
      <tbody>
        {rows.map(({ poz, name, value, note }) => (
          <tr key={poz} className="border-b border-slate-700/50 hover:bg-slate-700/30 transition-colors">
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
  const [open, setOpen] = useState(false);

  return (
    <div className="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden">
      <button
        onClick={() => setOpen(!open)}
        className="w-full px-6 py-4 flex items-center justify-between text-left hover:bg-slate-700/30 transition-colors"
      >
        <h2 className="text-white text-lg font-semibold">
          Step-by-step guide: File PIT-38 via e-pity
        </h2>
        {open
          ? <ChevronUp className="w-5 h-5 text-slate-400 shrink-0" />
          : <ChevronDown className="w-5 h-5 text-slate-400 shrink-0" />}
      </button>

      {open && (
        <div className="px-6 pb-6 space-y-6 border-t border-slate-700">
          <p className="text-slate-400 text-sm pt-4">
            e-pity is a free program for filing Polish tax returns. Below is a complete walkthrough
            for filing your PIT-38 with the exact values from your Interactive Brokers statement.
            Your values are highlighted in <span className="text-blue-400">blue</span>.
          </p>

          {/* Step 1 */}
          <GuideStep n={1} title="Open e-pity and select PIT-38">
            <p>
              Go to <span className="text-blue-400">e-pity.pl</span> and download the program
              (available for Windows, Mac, Linux) or use the online version.
            </p>
            <p>Launch the program and select <strong className="text-white">PIT-38</strong> as
              the form type for tax year <strong className="text-white">{year}</strong>.</p>
          </GuideStep>

          {/* Step 2 */}
          <GuideStep n={2} title="Part A — Tax office and purpose">
            <p>
              Select <strong className="text-white">&quot;Zlozenie zeznania&quot;</strong> (filing
              a return) as the purpose. Choose your tax office (Urzad Skarbowy) based on your
              place of residence as of December 31, {year}.
            </p>
          </GuideStep>

          {/* Step 3 */}
          <GuideStep n={3} title="Part B — Personal data">
            <p>
              Enter your PESEL (or NIP), full name, date of birth, and current residential address.
              If you used e-pity before, this may be pre-filled.
            </p>
          </GuideStep>

          {/* Step 4 */}
          <GuideStep n={4} title="Part C — Capital gains (poz. 20-27)">
            <p>
              This is where you enter your stock trading results. Since Interactive Brokers is a
              foreign broker and does not issue PIT-8C, you must use row&nbsp;2 &quot;Inne
              przychody&quot;:
            </p>
            <ul className="list-disc list-inside space-y-2 mt-2">
              <li>
                <strong className="text-white">Poz. 20-21</strong> (Przychody z PIT-8C)
                — leave <strong className="text-white">empty</strong>
              </li>
              <li>
                <strong className="text-white">Poz. 22</strong> (Inne przychody — Przychod)
                — enter <Val v={pit38.poz22Przychody} />
              </li>
              <li>
                <strong className="text-white">Poz. 23</strong> (Inne przychody — Koszty)
                — enter <Val v={pit38.poz23Koszty} />
              </li>
            </ul>
            <p className="mt-2">
              The program will auto-calculate the totals (poz. 24-25) and determine whether you
              have a gain (poz. 26) or loss (poz. 27):
            </p>
            <ul className="list-disc list-inside space-y-1 mt-1">
              {pit38.poz26Dochod > 0 && (
                <li>Poz. 26 (Dochod): <Val v={pit38.poz26Dochod} /></li>
              )}
              {pit38.poz27Strata > 0 && (
                <li>Poz. 27 (Strata): <Val v={pit38.poz27Strata} /></li>
              )}
              {pit38.poz26Dochod === 0 && pit38.poz27Strata === 0 && (
                <li>No gain or loss (both poz. 26 and 27 are empty)</li>
              )}
            </ul>
          </GuideStep>

          {/* Step 5 */}
          <GuideStep n={5} title="Part D — Tax calculation (poz. 28-33)">
            <p>
              <strong className="text-white">Poz. 28</strong> (Straty z lat ubieglych) — if you
              have unused losses from the past 5 years, enter the amount to deduct here (max 50%
              of each year&apos;s loss). Otherwise leave empty.
            </p>
            <p>The program will auto-calculate:</p>
            <ul className="list-disc list-inside space-y-1 mt-1">
              <li>Poz. 29 (Podstawa obliczenia): <Val v={pit38.poz29PodstawaObliczenia} /> (rounded to full PLN)</li>
              <li>Poz. 30 (Stawka podatku): <span className="font-mono text-white">19%</span></li>
              <li>Poz. 31 (Podatek): <Val v={pit38.poz31Podatek} /></li>
              <li>Poz. 32 (Podatek zaplacony za granica) — leave empty for typical IB stock trades</li>
              <li>Poz. 33 (Podatek nalezny): <Val v={pit38.poz33PodatekNalezny} /> (rounded to full PLN)</li>
            </ul>
          </GuideStep>

          {/* Step 6 */}
          <GuideStep n={6} title="Parts E-F — Virtual currencies">
            <p>
              Skip these sections unless you traded cryptocurrency. They cover art. 30b ust. 1a
              (virtual currency transactions).
            </p>
          </GuideStep>

          {/* Step 7 */}
          <GuideStep n={7} title="Part G — Foreign dividends (poz. 44-50)">
            {hasDividends ? (
              <>
                <p>
                  This section is for reporting dividends received from foreign companies (US stocks
                  in your case). The values go into specific fields:
                </p>
                <ul className="list-disc list-inside space-y-2 mt-2">
                  <li>
                    <strong className="text-white">Poz. 45</strong> (Zryczaltowany podatek 19%
                    od dywidend) — enter <Val v={pit38.poz45ZryczaltowanyPodatek} />
                    <span className="block text-slate-500 text-xs ml-6">
                      This is 19% of your total gross dividends ({formatPln(pit38.totalDividendsPln)})
                    </span>
                  </li>
                  <li>
                    <strong className="text-white">Poz. 46</strong> (Podatek zaplacony za
                    granica) — enter <Val v={pit38.poz46PodatekZaplaconyZaGranica} />
                    <span className="block text-slate-500 text-xs ml-6">
                      US withholding tax already deducted by IB (cannot exceed poz. 45)
                    </span>
                  </li>
                </ul>
                <p className="mt-2">The program will calculate:</p>
                <ul className="list-disc list-inside space-y-1 mt-1">
                  <li>Poz. 47 (Roznica): <Val v={pit38.poz47Roznica} /> (rounded to full PLN)</li>
                  <li>Poz. 49 (Podatek do zaplaty): <Val v={pit38.poz49PodatekDoZaplaty} /></li>
                </ul>
              </>
            ) : (
              <p>
                You have no dividend income to report. Leave poz. 45-46 empty.
              </p>
            )}
          </GuideStep>

          {/* Step 8 */}
          <GuideStep n={8} title="Attachment PIT/ZG — Foreign income">
            <p>
              Since your income is from a foreign broker, you <strong className="text-white">must</strong> attach
              the <strong className="text-white">PIT/ZG</strong> form. In e-pity, go to
              &quot;Zalaczniki&quot; (Attachments) and add PIT/ZG.
            </p>
            <ul className="list-disc list-inside space-y-2 mt-2">
              <li>
                Select country: <strong className="text-white">Stany Zjednoczone (USA)</strong> /
                country code <strong className="text-white">US</strong>
              </li>
              <li>
                In Part C.2 of PIT/ZG (income from art. 30b — capital gains):
                enter your revenue (<Val v={pit38.poz22Przychody} />) and
                costs (<Val v={pit38.poz23Koszty} />)
              </li>
              {hasDividends && (
                <li>
                  In Part C.4 of PIT/ZG (income from art. 30a — dividends):
                  enter your dividend income (<Val v={pit38.totalDividendsPln} />) and
                  withholding tax paid (<Val v={pit38.poz46PodatekZaplaconyZaGranica} />)
                </li>
              )}
            </ul>
          </GuideStep>

          {/* Step 9 */}
          <GuideStep n={9} title="Part J — 1.5% for charity (optional)">
            <p>
              You can donate 1.5% of your tax to a Public Benefit Organization (OPP). Enter
              the KRS number of your chosen organization. This does not increase your tax — it
              redirects part of what you already owe.
            </p>
          </GuideStep>

          {/* Step 10 */}
          <GuideStep n={10} title="Review and submit">
            <p>
              Review all fields in the summary screen. The program will highlight any errors or
              missing fields.
            </p>
            <ul className="list-disc list-inside space-y-2 mt-2">
              <li>
                Click <strong className="text-white">&quot;Wyslij e-deklaracje&quot;</strong> to
                submit electronically
              </li>
              <li>
                Authorize with your PESEL, date of birth, and last year&apos;s revenue amount
                (from PIT for {year - 1})
              </li>
              <li>
                Save the <strong className="text-white">UPO</strong> (Urzedowe Poswiadczenie
                Odbioru) — this is your official receipt confirming submission
              </li>
            </ul>
            <p className="mt-2">
              Deadline: <strong className="text-white">April 30, {year + 1}</strong>.
              {pit38.poz49PodatekDoZaplaty > 0 && (
                <span> Tax of <Val v={pit38.poz49PodatekDoZaplaty} /> must be paid by the same date.</span>
              )}
            </p>
          </GuideStep>

          <div className="bg-amber-500/10 border border-amber-500/30 rounded-lg p-4 text-sm">
            <p className="text-amber-300 font-medium mb-1">Important notes</p>
            <ul className="list-disc list-inside space-y-1 text-slate-400">
              <li>PIT-38 is filed individually — joint filing with a spouse is not allowed for this form</li>
              <li>Even if you only have losses, you <strong className="text-slate-300">must still file</strong> PIT-38 to carry them forward</li>
              <li>Losses from capital gains can be deducted over the next 5 years (max 50% of each year&apos;s loss per year)</li>
              <li>All PLN amounts use NBP Table A mid rates from the last business day before each transaction</li>
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
