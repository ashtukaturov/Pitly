import { Languages } from 'lucide-react';
import { LANGUAGES, useLang, type Lang } from '../i18n';

/**
 * Three-way language switcher (EN / UK / PL). `compact` renders short codes for the collapsed sidebar.
 */
export default function LanguageSwitcher({ compact = false, className = '' }: { compact?: boolean; className?: string }) {
  const { lang, setLang } = useLang();

  return (
    <div className={`flex items-center gap-1 ${className}`} role="group" aria-label="Language">
      {!compact && <Languages className="w-4 h-4 text-slate-500 shrink-0 mr-1" />}
      {LANGUAGES.map((option) => {
        const active = option.code === lang;
        return (
          <button
            key={option.code}
            type="button"
            onClick={() => setLang(option.code as Lang)}
            aria-pressed={active}
            title={option.label}
            className={`px-2 py-1 rounded-md text-xs font-medium transition-colors ${
              active
                ? 'bg-blue-500/20 text-blue-300'
                : 'text-slate-400 hover:text-slate-200 hover:bg-slate-800'
            }`}
          >
            {compact ? option.short : option.label}
          </button>
        );
      })}
    </div>
  );
}
