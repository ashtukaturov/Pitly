import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { LanguageContext, STORAGE_KEY, detectLanguage, translate, type Lang, type TFunction } from './context';
import { translations } from './translations';

export function LanguageProvider({ children, initialLang }: { children: ReactNode; initialLang?: Lang }) {
  const [lang, setLangState] = useState<Lang>(() => initialLang ?? detectLanguage());

  const setLang = useCallback((next: Lang) => {
    setLangState(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // remembering the choice is a convenience only
    }
  }, []);

  useEffect(() => {
    document.documentElement.lang = lang;
  }, [lang]);

  const t = useCallback<TFunction>((key, params) => translate(translations, lang, key, params), [lang]);

  const value = useMemo(() => ({ lang, setLang, t }), [lang, setLang, t]);
  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}
