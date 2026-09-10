import { PanelTop } from 'lucide-react'
import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { PetDecision } from '../../../shared/desktopPet'

/** The composer's approval options, answered from the pill; the choice locks until the next snapshot. */
export function PetDecisionActions({ decision, onDecide, onReturn }: {
  decision: PetDecision
  onDecide: (value: string) => void
  onReturn: () => void
}): JSX.Element {
  const t = useT()
  const [sent, setSent] = useState<string | null>(null)
  const locked = sent === decision.id
  const detail = decision.reason ? [decision.operation, decision.target].filter(Boolean).join(' ') : ''
  return <div className="desktop-pet-pill-decision" role="group" aria-label={t('desktopPet.decision.title')}>
    <div className="desktop-pet-pill-decision-question">{decision.question}</div>
    {detail && <div className="desktop-pet-pill-decision-detail" title={detail}>{detail}</div>}
    <div className="desktop-pet-pill-decision-options">
      {decision.options.map((option) => (
        <button key={option.value} className="desktop-pet-pill-choice" disabled={locked}
          data-decline={option.value === decision.declineValue || undefined}
          onClick={() => { setSent(decision.id); onDecide(option.value) }}>{option.label}</button>
      ))}
    </div>
    <button className="desktop-pet-pill-open" onClick={onReturn}><PanelTop size={14} />{t('desktopPet.decision.open')}</button>
  </div>
}
