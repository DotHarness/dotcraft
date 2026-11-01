import type { JSX } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import type { ProviderUsageSnapshot } from '../../stores/providersStore'
import styles from './OpenRouterUsageRows.module.css'

export function OpenRouterUsageRows({ usage }: { usage: ProviderUsageSnapshot }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const format = new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD', maximumFractionDigits: 6 })
  const values = usage.values ?? {}
  return <div className={styles.rows}>
    {([
      ['keyUsage', values.usage],
      ['keyRemaining', values.limit_remaining],
      ['requestCost', values.last_request_cost]
    ] as const).filter(([, value]) => value != null).map(([key, value]) => <div className={styles.row} key={key}>
      <span>{t(`settings.llm.openrouter.${key}`)}</span>
      <span className={styles.value}>{format.format(value!)}</span>
    </div>)}
    {usage.credits?.unlimited && <div className={styles.row}>
      <span>{t('settings.llm.openrouter.keyLimit')}</span>
      <span>{t('settings.llm.openrouter.unlimited')}</span>
    </div>}
  </div>
}
