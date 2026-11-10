import { Check, Languages } from 'lucide-react';
import { LANGUAGES, useLang, useT } from '../i18n';

/**
 * Language switcher (EN / UA / PL).
 *
 * - `inline`: one segmented control with full names, for places with room (the landing page header).
 * - `sidebar`: one row per language, built like the navigation rows: a short code that is always visible in the
 *   collapsed rail and the full name that appears when the sidebar expands. Must be rendered inside a `group`.
 */
export default function LanguageSwitcher({ variant = 'inline' }: { variant?: 'inline' | 'sidebar' }) {
  const { lang, setLang } = useLang();
  const t = useT();
  const label = t('nav.language');

  if (variant === 'sidebar') {
    return (
      <div role="group" aria-label={label} className="py-2 space-y-0.5">
        {LANGUAGES.map((option) => {
          const active = option.code === lang;
          return (
            <button
              key={option.code}
              type="button"
              onClick={() => setLang(option.code)}
              aria-pressed={active}
              title={option.label}
              className={`w-full flex items-center gap-3 px-4 py-1.5 text-sm whitespace-nowrap transition-colors ${
                active ? 'text-blue-400' : 'text-slate-400 hover:text-slate-200 hover:bg-slate-800/50'
              }`}
            >
              <span
                className={`w-7 h-5 shrink-0 flex items-center justify-center rounded border text-[10px] font-semibold tracking-wide ${
                  active ? 'bg-blue-500/20 border-blue-500/40 text-blue-300' : 'border-slate-700'
                }`}
              >
                {option.short}
              </span>
              <span className="flex-1 text-left opacity-0 group-hover:opacity-100 transition-opacity">{option.label}</span>
              {active && <Check className="w-4 h-4 shrink-0 opacity-0 group-hover:opacity-100 transition-opacity" />}
            </button>
          );
        })}
      </div>
    );
  }

  return (
    <div
      role="group"
      aria-label={label}
      className="inline-flex items-center gap-0.5 rounded-lg border border-slate-700/70 bg-slate-800/60 p-1"
    >
      <Languages className="w-4 h-4 text-slate-500 shrink-0 mx-1.5" aria-hidden="true" />
      {LANGUAGES.map((option) => {
        const active = option.code === lang;
        return (
          <button
            key={option.code}
            type="button"
            onClick={() => setLang(option.code)}
            aria-pressed={active}
            className={`px-2.5 py-1 rounded-md text-xs font-medium whitespace-nowrap transition-colors ${
              active ? 'bg-blue-500/20 text-blue-300' : 'text-slate-400 hover:text-slate-200 hover:bg-slate-700/60'
            }`}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
