import type { JSX } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useModelCatalog } from '../../hooks/useModelCatalog'
import styles from './ModelCatalogNotice.module.css'

export function ModelCatalogNotice({ providerId }: { providerId?: string | null }): JSX.Element | null {
  const t = useT()
  const locale = useLocale()
  const state = useModelCatalog(providerId)
  const message = state.refreshNotice === 'usingCachedCatalog'
    ? t('settings.llm.openrouter.catalogStale')
    : state.refreshNotice === 'personalizationUnavailable' ? t('settings.llm.openrouter.catalogUnpersonalized') : null
  if (!message) return null
  const time = state.refreshedAt ? new Date(state.refreshedAt) : null
  return <div className={styles.notice} role="status">
    <span>{message}</span>
    {time && Number.isFinite(time.getTime()) && <time dateTime={time.toISOString()}>{time.toLocaleString(locale)}</time>}
  </div>
}
