import { createContext, useContext, useState, useCallback } from 'react';
import en from './en';
import da from './da';

const translations = { en, da };

const LanguageContext = createContext();

export function LanguageProvider({ children }) {
  const [lang, setLangState] = useState(() => localStorage.getItem('lang') || 'en');

  const setLang = useCallback((l) => {
    setLangState(l);
    localStorage.setItem('lang', l);
  }, []);

  const t = useCallback((key, params) => {
    const str = translations[lang]?.[key] || translations.en[key] || key;
    if (!params) return str;
    return str.replace(/\{(\w+)\}/g, (_, k) => params[k] ?? `{${k}}`);
  }, [lang]);

  return (
    <LanguageContext.Provider value={{ lang, setLang, t }}>
      {children}
    </LanguageContext.Provider>
  );
}

export function useTranslation() {
  const ctx = useContext(LanguageContext);
  if (!ctx) throw new Error('useTranslation must be used within a LanguageProvider');
  return ctx;
}
