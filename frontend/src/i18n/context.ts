import { createContext, useContext } from 'react';

export type Lang = 'en' | 'uk' | 'pl';

export const LANGUAGES: { code: Lang; label: string; short: string }[] = [
  { code: 'en', label: 'English', short: 'EN' },
  { code: 'uk', label: 'Українська', short: 'UA' },
  { code: 'pl', label: 'Polski', short: 'PL' },
];

/** Flat key -> text dictionary for one language. Keys are namespaced, e.g. "dashboard.title". */
export type Dictionary = Record<string, string>;
/** A translation module: the same keys in every language. */
export type Translations = Record<Lang, Dictionary>;

export type TranslateParams = Record<string, string | number>;
export type TFunction = (key: string, params?: TranslateParams) => string;

export const STORAGE_KEY = 'pitly.lang';

export function detectLanguage(): Lang {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'en' || stored === 'uk' || stored === 'pl') return stored;
  } catch {
    // storage unavailable (private mode, blocked): fall through to the browser language
  }
  const browser = (typeof navigator !== 'undefined' ? navigator.language : '').toLowerCase();
  if (browser.startsWith('uk')) return 'uk';
  if (browser.startsWith('pl')) return 'pl';
  return 'en';
}

function interpolate(text: string, params?: TranslateParams): string {
  if (!params) return text;
  return text.replace(/\{(\w+)\}/g, (match, name: string) => (name in params ? String(params[name]) : match));
}

/** Merge every translation module into one dictionary per language (later modules win on duplicate keys). */
export function mergeTranslations(modules: Translations[]): Translations {
  const merged: Translations = { en: {}, uk: {}, pl: {} };
  for (const module of modules) {
    for (const lang of ['en', 'uk', 'pl'] as const) {
      Object.assign(merged[lang], module[lang]);
    }
  }
  return merged;
}

export function translate(dictionaries: Translations, lang: Lang, key: string, params?: TranslateParams): string {
  const text = dictionaries[lang][key] ?? dictionaries.en[key] ?? key;
  return interpolate(text, params);
}

export interface LanguageContextValue {
  lang: Lang;
  setLang: (lang: Lang) => void;
  t: TFunction;
}

export const LanguageContext = createContext<LanguageContextValue | null>(null);

function useLanguageContext(): LanguageContextValue {
  const ctx = useContext(LanguageContext);
  if (!ctx) throw new Error('useT/useLang must be used inside <LanguageProvider>');
  return ctx;
}

/** Translation function for the current language: t('dashboard.title'), t('x.y', { count: 3 }). */
export function useT(): TFunction {
  return useLanguageContext().t;
}

/** Current language and setter, for the switcher. */
export function useLang(): { lang: Lang; setLang: (lang: Lang) => void } {
  const { lang, setLang } = useLanguageContext();
  return { lang, setLang };
}
