import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode
} from 'react'
import {
  DEFAULT_LOCALE,
  localeToHtmlLang,
  normalizeLocale,
  translate,
  type AppLocale,
  type MessageKey
} from '../../shared/locales'

interface LocaleContextValue {
  locale: AppLocale
  /** Update UI locale and `document.documentElement.lang` (settings persistence is separate). */
  setUiLocale: (locale: AppLocale) => void
  t: (key: MessageKey | string, vars?: Record<string, string | number>) => string
}

const LocaleContext = createContext<LocaleContextValue | null>(null)

export function LocaleProvider({ children }: { children: ReactNode }): JSX.Element {
  const [locale, setLocale] = useState<AppLocale>(() =>
    normalizeLocale(window.api?.initialLocale ?? DEFAULT_LOCALE)
  )

  useEffect(() => {
    window.api.settings
      .get()
      .then((s) => setLocale(normalizeLocale(s.locale)))
      .catch(() => {})
  }, [])

  useEffect(() => {
    document.documentElement.lang = localeToHtmlLang(locale)
  }, [locale])

  const setUiLocale = useCallback((next: AppLocale) => {
    setLocale(normalizeLocale(next))
  }, [])

  const t = useCallback(
    (key: MessageKey | string, vars?: Record<string, string | number>) =>
      translate(locale, key, vars),
    [locale]
  )

  const value = useMemo(
    () => ({ locale, setUiLocale, t }),
    [locale, setUiLocale, t]
  )

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>
}

export function useLocale(): AppLocale {
  const ctx = useContext(LocaleContext)
  if (!ctx) throw new Error('useLocale must be used within LocaleProvider')
  return ctx.locale
}

export function useSetUiLocale(): (locale: AppLocale) => void {
  const ctx = useContext(LocaleContext)
  if (!ctx) throw new Error('useSetUiLocale must be used within LocaleProvider')
  return ctx.setUiLocale
}

export function useT(): LocaleContextValue['t'] {
  const ctx = useContext(LocaleContext)
  if (!ctx) throw new Error('useT must be used within LocaleProvider')
  return ctx.t
}
