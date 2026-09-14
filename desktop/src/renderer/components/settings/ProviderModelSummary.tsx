import type { JSX } from 'react'
import { Zap } from 'lucide-react'
import styles from './ProviderModelSummary.module.css'
import { useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import type { ModelPreference, ModelPreferenceReasoningEffort } from '../../../shared/modelPreference'

interface ProviderModelSummaryProps {
  main: ModelPreference | null
  subAgent: ModelPreference | null
}

const reasoningLabelKeys: Record<ModelPreferenceReasoningEffort, MessageKey> = {
  low: 'composer.reasoning.low',
  medium: 'composer.reasoning.medium',
  high: 'composer.reasoning.high',
  extraHigh: 'composer.reasoning.extraHigh',
  ultra: 'composer.reasoning.ultra'
}

export function ProviderModelSummary({ main, subAgent }: ProviderModelSummaryProps): JSX.Element | null {
  const t = useT()
  if (!main && !subAgent) return null

  const reasoningLabel = (preference: ModelPreference): string => t(preference.reasoning.enabled
    ? reasoningLabelKeys[preference.reasoning.effort]
    : 'composer.reasoning.off')

  const clause = (label: string, value: string, fast: boolean): JSX.Element => (
    <span className={styles.clause}>
      <span>{label}</span>
      {fast && (
        <span role="img" aria-label={t('composer.speed.fast')} className={styles.fast}>
          <Zap aria-hidden size={11} strokeWidth={2.4} fill="currentColor" />
        </span>
      )}
      <span className={styles.value}>{value}</span>
    </span>
  )

  const preferenceClause = (label: string, preference: ModelPreference): JSX.Element => clause(
    label,
    [
      preference.model,
      reasoningLabel(preference),
      preference.contextWindow.mode === 'max' ? 'MAX' : null
    ].filter(Boolean).join(' · '),
    preference.speed === 'fast'
  )

  return (
    <div className={styles.row}>
      {main && preferenceClause(t('settings.llm.providerSummaryMain'), main)}
      {subAgent
        ? preferenceClause(t('settings.llm.providerSummarySubAgent'), subAgent)
        : clause(
          t('settings.llm.providerSummarySubAgent'),
          t('settings.llm.providerSummarySubAgentInherit'),
          false
        )}
    </div>
  )
}
