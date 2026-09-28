import type { JSX } from 'react'
import type { ImportOutcome } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { importSourceLabel } from '../../../utils/sessionImport'
import { Button } from '../../ui/Button'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { importCategoryIcon } from './ImportSelectionRows'
import { openOutcome, outcomeActionKey, reasonText } from './importOutcomes'
import styles from './ImportHistory.module.css'

export function ImportAttention({ outcomes }: { outcomes: ImportOutcome[] }): JSX.Element | null {
  const t = useT()
  if (outcomes.length === 0) return null
  return (
    <SettingsGroup title={t('settings.import.setup.attention')} description={t('settings.import.attention.description')}>
      {outcomes.map((outcome, index) => {
        const action = outcomeActionKey(outcome)
        const reason = reasonText(outcome, t)
        const title = outcome.category === 'hooks'
          ? t('settings.import.attention.hooks', { source: importSourceLabel(outcome.source) })
          : outcome.title ?? t(`settings.import.category.${outcome.category}`)
        return (
          <SettingsRow key={`${outcome.source}:${outcome.sourceId}:${index}`}>
            <div className={styles.attentionRow}>
              <span className={styles.tile} aria-hidden="true">{importCategoryIcon(outcome.category)}</span>
              <span className={styles.text}>
                <span className={styles.title}>{title}</span>
                {reason && <span className={styles.meta}>{reason}</span>}
              </span>
              {action && <Button onClick={() => openOutcome(outcome)}>{t(action)}</Button>}
            </div>
          </SettingsRow>
        )
      })}
    </SettingsGroup>
  )
}
