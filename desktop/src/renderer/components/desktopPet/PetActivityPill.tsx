import { AlertTriangle, CheckCircle2, Clock, PanelTop, X } from 'lucide-react'
import type { FocusEvent } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { PetSnapshot, PetStatus } from '../../../shared/desktopPet'
import { PetDecisionActions } from './PetDecisionActions'
import { PetQuickChat } from './PetQuickChat'
import { PetStatusLine } from './PetStatusLine'
import { PetSubmitControl } from './PetSubmitControl'

export interface PetActivityPillProps {
  snapshot: PetSnapshot
  text: string
  onHold: (held: boolean) => void
  onDismiss: () => void
  onStop: () => void
  onReturn: () => void
  onDecide: (value: string) => void
  onChange: (text: string) => void
  onSubmit: () => void
}

const ICON: Partial<Record<PetStatus, { glyph: JSX.Element; label: 'desktopPet.status.waiting' | 'desktopPet.status.blocked' | 'desktopPet.status.review' }>> = {
  waiting: { glyph: <Clock size={14} />, label: 'desktopPet.status.waiting' },
  failed: { glyph: <AlertTriangle size={14} />, label: 'desktopPet.status.blocked' },
  review: { glyph: <CheckCircle2 size={14} />, label: 'desktopPet.status.review' }
}

/** Title, one status line and the follow-up controls; the conversation itself stays in the main window. */
export function PetActivityPill({ snapshot, text, onHold, onDismiss, onStop, onReturn, onDecide, onChange, onSubmit }: PetActivityPillProps): JSX.Element {
  const t = useT()
  const status = snapshot.status
  const patch = status?.patch
  const icon = status ? ICON[status.status] : undefined
  const stoppable = status?.status === 'running' && (status.canStop || status.stopping === true)
  const blur = (event: FocusEvent<HTMLDivElement>): void => {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) onHold(false)
  }
  return <div className="desktop-pet-pill" data-motion={snapshot.reducedMotion ? 'off' : 'on'} data-status={status?.status ?? 'none'}
    onMouseEnter={() => onHold(true)} onMouseLeave={() => onHold(false)} onFocus={() => onHold(true)} onBlur={blur}>
    {status && <div className="desktop-pet-pill-head">
      {icon && <span className="desktop-pet-pill-icon" role="img" data-tone={status.lineTone} aria-label={t(icon.label)} title={t(icon.label)}>{icon.glyph}</span>}
      <span className="desktop-pet-pill-title" title={status.title}>{status.title}</span>
      <button className="desktop-pet-pill-action" aria-label={t('desktopPet.activity.dismiss')} onClick={onDismiss}><X size={12} /></button>
    </div>}
    {status?.line && <PetStatusLine line={status.line} tone={status.lineTone} running={status.status === 'running'} />}
    {patch && <div className="desktop-pet-pill-patch">
      <span className="desktop-pet-pill-patch-add">+{patch.additions}</span>
      <span className="desktop-pet-pill-patch-del">-{patch.deletions}</span>
      <span>{patch.files === 1 ? t('desktopPet.activity.file') : t('desktopPet.activity.files', { count: patch.files })}</span>
    </div>}
    {status?.decision
      ? <PetDecisionActions decision={status.decision} onDecide={onDecide} onReturn={onReturn} />
      : snapshot.canChat
        ? <div className="desktop-pet-pill-composer">
            <PetQuickChat text={text} busy={snapshot.busy === true} status={status} followUpMode={snapshot.followUpMode}
              onChange={onChange} onSubmit={onSubmit} onStop={onStop} />
          </div>
        : <div className="desktop-pet-pill-fallback">
            <button className="desktop-pet-pill-open" onClick={onReturn}><PanelTop size={14} />{t('desktopPet.return')}</button>
            {stoppable && <PetSubmitControl status={status} followUpMode={snapshot.followUpMode} hasDraft={false} busy onSubmit={() => {}} onStop={onStop} />}
          </div>}
  </div>
}
