import type { JSX } from 'react'
import type { MessageKey } from '../../../../shared/locales'
import { Skeleton } from '../../ui/Skeleton'
import styles from './ProfileInsights.module.css'

type TFn = (key: MessageKey | string, vars?: Record<string, string | number>) => string

const ROWS = Array.from({ length: 5 }, (_, index) => index)

export function ProfileInsightsSkeleton({ t }: { t: TFn }): JSX.Element {
  return (
    <div aria-hidden="true" className={styles.grid}>
      <div className={styles.column}>
        <div className={styles.heading}>{t('settings.profile.insights.title')}</div>
        <div className={styles.list}>
          {ROWS.map((index) => (
            <div key={index} className={styles.row}>
              <Skeleton width={96} height={12} />
              <Skeleton width={56} height={12} />
            </div>
          ))}
        </div>
      </div>
      <div className={styles.column}>
        <div className={styles.heading}>{t('settings.profile.plugins.title')}</div>
        <div className={styles.list}>
          {ROWS.map((index) => (
            <div key={index} className={styles.row}>
              <span className={styles.identity}>
                <Skeleton width={24} height={24} radius="var(--identity-mark-radius-compact)" />
                <Skeleton width={96} height={12} />
              </span>
              <Skeleton width={56} height={12} />
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
