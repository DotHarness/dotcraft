import type { JSX } from 'react'
import type { LocalImportOutcome } from '../../../../shared/localImport'
import { useT } from '../../../contexts/LocaleContext'
import { importSourceLabel } from '../../../utils/sessionImport'
import { Button } from '../../ui/Button'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { importCategoryIcon } from './ImportSelectionRows'
import { importDestinationLabel, outcomeActionKey, reasonText } from './importOutcomes'
import styles from './ImportHistory.module.css'

export function ImportAttention({ outcomes, onOpen, local }: {
  outcomes: LocalImportOutcome[]
  onOpen: (outcome: LocalImportOutcome) => void
  local: boolean
}): JSX.Element | null {
  const t = useT()
  if (outcomes.length === 0) return null
  return (
    <SettingsGroup title={t('settings.import.setup.attention')} description={t('settings.import.attention.description')}>
      {outcomes.map((outcome, index) => {
        const action = outcomeActionKey(outcome, local)
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
                {local && <span className={styles.meta}>{importDestinationLabel(outcome, t)}</span>}
                {reason && <span className={styles.meta}>{reason}</span>}
              </span>
              {action && <Button onClick={() => onOpen(outcome)}>{t(action)}</Button>}
            </div>
          </SettingsRow>
        )
      })}
    </SettingsGroup>
  )
}
