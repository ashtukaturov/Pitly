import { useState, useCallback } from 'react';
import { Upload, ChevronDown, ChevronUp, Loader2, CheckCircle2, AlertCircle } from 'lucide-react';
import { importFiles } from '../api';
import type { AppState } from '../types';
import PriorYearLossesInput from '../components/PriorYearLossesInput';
import { validatePriorYearLossRows, type PriorYearLossRow } from '../priorYearLosses';
import { useT, useLang, type Lang } from '../i18n/index';

type Step = 'idle' | 'parsing' | 'rates' | 'calculating' | 'done' | 'error';

// Labels are translation keys; the component resolves them with t() so they follow the current language
const steps: { key: Step; labelKey: string }[] = [
  { key: 'parsing', labelKey: 'import.stepParsing' },
  { key: 'rates', labelKey: 'import.stepRates' },
  { key: 'calculating', labelKey: 'import.stepCalculating' },
  { key: 'done', labelKey: 'import.stepDone' },
];

/** Picks the plural key suffix for a count: English has one/many, Ukrainian and Polish also have a "few" form (2-4). */
function pluralSuffix(lang: Lang, count: number): 'One' | 'Few' | 'Many' {
  if (lang === 'en') return count === 1 ? 'One' : 'Many';
  const mod10 = count % 10;
  const mod100 = count % 100;
  if (lang === 'uk' ? mod10 === 1 && mod100 !== 11 : count === 1) return 'One';
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return 'Few';
  return 'Many';
}

function HelpSection({ title, children }: { title: string; children: React.ReactNode }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="bg-slate-800 border border-slate-700 rounded-xl overflow-hidden">
      <button
        onClick={() => setOpen(!open)}
        className="w-full flex items-center justify-between px-6 py-4 text-left text-slate-300 hover:text-white transition-colors"
      >
        <span className="font-medium">{title}</span>
        {open ? <ChevronUp className="w-5 h-5" /> : <ChevronDown className="w-5 h-5" />}
      </button>
      {open && (
        <div className="px-6 pb-5 text-slate-400 text-sm space-y-2">
          {children}
        </div>
      )}
    </div>
  );
}

export default function ImportPage({
  onComplete,
  existingFiles = [],
  onStartOver,
}: {
  onComplete: (data: AppState) => void;
  // Files from the previous import; new uploads are added to them so statements can arrive in several batches
  existingFiles?: File[];
  onStartOver?: () => void;
}) {
  const t = useT();
  const { lang } = useLang();
  const [step, setStep] = useState<Step>('idle');
  const [error, setError] = useState('');
  const [pendingFiles, setPendingFiles] = useState<File[]>([]);
  const [residencyStartDate, setResidencyStartDate] = useState('');
  const [priorYearLosses, setPriorYearLosses] = useState<PriorYearLossRow[]>([]);

  const [dragOver, setDragOver] = useState(false);

  const processFiles = useCallback(async (files: File[]) => {
    if (files.length === 0) {
      setError(t('import.errorNoFiles'));
      return;
    }

    if (files.some(file => !file.name.toLowerCase().endsWith('.csv'))) {
      setError(t('import.errorCsvOnly'));
      return;
    }

    // Merge with previously queued files (from a failed attempt), newer file wins on name collision
    const merged = new Map<string, File>();
    for (const f of existingFiles) merged.set(f.name, f);
    for (const f of pendingFiles) merged.set(f.name, f);
    for (const f of files) merged.set(f.name, f);
    const allFiles = Array.from(merged.values());

    setError('');
    setStep('parsing');

    const timer1 = setTimeout(() => setStep('rates'), 500);
    const timer2 = setTimeout(() => setStep('calculating'), 1200);

    try {
      const lossCheck = validatePriorYearLossRows(priorYearLosses, t);
      if (lossCheck.error) {
        clearTimeout(timer1);
        clearTimeout(timer2);
        setStep('error');
        setError(lossCheck.error);
        return;
      }
      const result = await importFiles(allFiles, residencyStartDate || undefined, lossCheck.losses.length > 0 ? lossCheck.losses : undefined);
      clearTimeout(timer1);
      clearTimeout(timer2);
      setPendingFiles([]);
      setStep('done');
      setTimeout(() => {
        onComplete({
          sessionId: result.sessionId,
          summary: result.summary,
          pit38: result.pit38,
          trades: result.trades,
          dividends: result.dividends,
          files: allFiles,
          availableYears: result.availableYears,
          residencyStartDate: residencyStartDate || undefined,
          priorYearLosses: lossCheck.losses,
        });
      }, 600);
    } catch (err: unknown) {
      clearTimeout(timer1);
      clearTimeout(timer2);
      setPendingFiles(allFiles);
      setStep('error');
      setError(err instanceof Error ? err.message : t('import.errorFailed'));
    }
  }, [onComplete, pendingFiles, existingFiles, residencyStartDate, priorYearLosses, t]);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const files = Array.from(e.dataTransfer.files);
    if (files.length > 0) processFiles(files);
  }, [processFiles]);

  const handleFileInput = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []);
    if (files.length > 0) processFiles(files);
  }, [processFiles]);

  const isProcessing = step !== 'idle' && step !== 'error';

  return (
    <div className="max-w-2xl mx-auto mt-16">
      <h1 className="text-white text-3xl font-bold text-center mb-2">{t('import.title')}</h1>
      <p className="text-slate-400 text-center mb-10">
        {t('import.subtitle')}
      </p>

      {!isProcessing ? (
        <>
          {existingFiles.length > 0 && (
            <div className="mb-6 bg-slate-800 border border-blue-500/40 rounded-xl p-5 text-sm">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <span className="text-slate-200 font-medium">
                    {t(`import.loaded${pluralSuffix(lang, existingFiles.length)}`, { count: existingFiles.length })}
                  </span>
                  <span className="block text-slate-500 mt-1">
                    {existingFiles.map(f => f.name).join(', ')}
                  </span>
                  <span className="block text-slate-500 mt-1">
                    {t('import.loadedHint')}
                  </span>
                </div>
                {onStartOver && (
                  <button
                    type="button"
                    onClick={onStartOver}
                    className="shrink-0 text-slate-400 hover:text-red-400 transition-colors text-sm"
                  >
                    {t('import.startOver')}
                  </button>
                )}
              </div>
            </div>
          )}
          <div className="mb-6 bg-slate-800 border border-slate-700 rounded-xl p-5">
            <label className="block">
              <span className="text-slate-200 font-medium">{t('import.residencyLabel')}</span>
              <span className="block text-slate-500 text-sm mt-1">
                {t('import.residencyHint')}
              </span>
              <input
                type="date"
                value={residencyStartDate}
                onChange={(e) => setResidencyStartDate(e.target.value)}
                className="mt-3 w-full md:w-auto bg-slate-900 border border-slate-700 rounded-lg px-4 py-2.5 text-sm text-white focus:outline-none focus:border-blue-500"
              />
            </label>
          </div>

          <PriorYearLossesInput rows={priorYearLosses} onChange={setPriorYearLosses} />

          <label
            onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
            onDragLeave={() => setDragOver(false)}
            onDrop={handleDrop}
            className={`block border-2 border-dashed rounded-2xl p-16 text-center cursor-pointer transition-colors bg-slate-800/50 ${
              dragOver ? 'border-blue-500 bg-blue-500/10' : 'border-slate-600 hover:border-blue-500'
            }`}
          >
            <Upload className="w-12 h-12 text-slate-500 mx-auto mb-4" />
            <p className="text-slate-300 font-medium mb-1">{t('import.dropTitle')}</p>
            <p className="text-slate-500 text-sm">{t('import.dropHint')}</p>
            <input type="file" accept=".csv" multiple className="hidden" onChange={handleFileInput} />
          </label>

          {error && (
            <div className="mt-4 bg-red-500/10 border border-red-500/30 rounded-lg p-4 text-red-400 text-sm space-y-2">
              <div className="flex items-start gap-2">
                <AlertCircle className="w-5 h-5 shrink-0 mt-0.5" />
                <span>{error}</span>
              </div>
              {pendingFiles.length > 0 && (
                <div className="ml-7 text-slate-400">
                  <span className="text-slate-300">{t('import.queuedLabel')}</span>{' '}
                  {pendingFiles.map(f => f.name).join(', ')}
                  <span className="block mt-1">{t('import.queuedHint')}</span>
                </div>
              )}
            </div>
          )}

          <div className="mt-8 space-y-2">
            <HelpSection title={t('import.ibTitle')}>
              <p>{t('import.ibStep1')}</p>
              <p>{t('import.ibStep2Prefix')}<strong className="text-slate-300">Reports &rarr; Statements &rarr; Activity</strong></p>
              <p>{t('import.ibStep3Prefix')}<strong className="text-slate-300">CSV</strong></p>
              <p>{t('import.ibStep4')}</p>
              <p>{t('import.ibStep5')}</p>
              <p>{t('import.ibStep6')}</p>
            </HelpSection>
            <HelpSection title={t('import.t212Title')}>
              <p>{t('import.t212Step1')}</p>
              <p>{t('import.t212Step2Prefix')}<strong className="text-slate-300">History</strong>{t('import.t212Step2Suffix')}</p>
              <p>{t('import.t212Step3Prefix')}<strong className="text-slate-300">Download</strong>{t('import.t212Step3Suffix')}</p>
              <p>{t('import.t212Step4')}</p>
              <p>{t('import.t212Step5')}</p>
              <p>{t('import.t212Step6')}</p>
              <p>{t('import.t212Step7')}</p>
            </HelpSection>
          </div>
        </>
      ) : (
        <div className="bg-slate-800 border border-slate-700 rounded-xl p-8">
          <div className="space-y-4">
            {(() => {
              const currentIdx = steps.findIndex(s => s.key === step);
              return steps.map(({ key, labelKey }, idx) => {
              const isDone = idx < currentIdx || step === 'done';
              const isCurrent = idx === currentIdx && step !== 'done';

              return (
                <div key={key} className="flex items-center gap-3">
                  {isDone ? (
                    <CheckCircle2 className="w-5 h-5 text-green-400" />
                  ) : isCurrent ? (
                    <Loader2 className="w-5 h-5 text-blue-400 animate-spin" />
                  ) : (
                    <div className="w-5 h-5 rounded-full border-2 border-slate-600" />
                  )}
                  <span className={isDone ? 'text-green-400' : isCurrent ? 'text-white' : 'text-slate-500'}>
                    {t(labelKey)}
                  </span>
                </div>
              );
            });
            })()}
          </div>
        </div>
      )}
    </div>
  );
}
