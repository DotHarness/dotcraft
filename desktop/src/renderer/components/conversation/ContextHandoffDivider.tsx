import { ArrowLeftRight, ArrowRight } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ProviderPresetMark } from '../settings/providers/ProviderPresetMark'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ProviderMarkKind } from '../ui/ProviderMark'
import styles from './ContextHandoffDivider.module.css'

export interface ContextHandoffEndpoint {
  providerName: string
  model: string
  mark: ProviderMarkKind | null
}

export function ContextHandoffDivider({ from, to }: {
  from: ContextHandoffEndpoint
  to: ContextHandoffEndpoint
}): JSX.Element {
  const t = useT()
  const title = 'Context handoff'
  const description = t('systemNotice.contextHandoff.description', {
    title,
    from: `${from.providerName} / ${from.model}`,
    to: `${to.providerName} / ${to.model}`
  })

  return (
    <div className={styles.container}>
      <div className={styles.divider} role="group" aria-label={description}>
        <span className={styles.label} aria-hidden="true">
          <ArrowLeftRight size={13} />
          {title}
        </span>
        <span className={styles.route}>
          <HandoffEndpoint endpoint={from} />
          <ArrowRight className={styles.arrow} size={13} aria-hidden="true" />
          <HandoffEndpoint endpoint={to} />
        </span>
      </div>
    </div>
  )
}

function HandoffEndpoint({ endpoint }: { endpoint: ContextHandoffEndpoint }): JSX.Element {
  return (
    <ActionTooltip label={endpoint.providerName} openDelayMs={300} multiline wrapperClassName={styles.anchor}>
      <span className={styles.endpoint} role="group" aria-label={endpoint.model} tabIndex={0}>
        <span className={styles.mark}><ProviderPresetMark mark={endpoint.mark} size={13} /></span>
        <span className={styles.model}>{endpoint.model}</span>
      </span>
    </ActionTooltip>
  )
}
