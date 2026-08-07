import { useEffect, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { Mic } from 'lucide-react'

import { useT } from '../../contexts/LocaleContext'
import { LayerBoundary } from '../../contexts/LayerContext'
import { Button } from '../ui/Button'
import { ModalHeader } from '../ui/ModalHeader'

export type VoiceSetupStage = 'setup' | 'recovery'

interface VoiceSetupDialogProps {
  stage: VoiceSetupStage
  onContinue(): void
  onCancel(): void
}

export function VoiceSetupDialog({ stage, onContinue, onCancel }: VoiceSetupDialogProps): JSX.Element {
  const t = useT()
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  const setup = stage === 'setup'
  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="voice-setup-title"
      style={overlayStyle}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onCancel()
      }}
    >
      <div style={cardStyle} onMouseDown={(event) => event.stopPropagation()}>
        <ModalHeader
          icon={<Mic size={18} strokeWidth={2} aria-hidden />}
          title={t(setup ? 'voice.setup.title' : 'voice.permissionRecovery.title')}
          titleId="voice-setup-title"
          description={t(setup ? 'voice.setup.description' : 'voice.permissionRecovery.description')}
          onClose={onCancel}
          closeLabel={t('common.close')}
        />
        <div style={actionsStyle}>
          <Button variant="ghost" onClick={onCancel}>
            {t('voice.setup.notNow')}
          </Button>
          <Button variant="primary" onClick={onContinue}>
            {t(setup ? 'voice.setup.action' : 'voice.permissionRecovery.action')}
          </Button>
        </div>
      </div>
    </div>
  )
  return createPortal(<LayerBoundary>{dialog}</LayerBoundary>, document.body) as JSX.Element
}

const overlayStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 10000,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'var(--overlay-scrim)'
}

const cardStyle: CSSProperties = {
  width: 420,
  maxWidth: 'calc(100vw - 48px)',
  padding: 24,
  borderRadius: 12,
  background: 'var(--bg-secondary)',
  boxShadow: 'var(--shadow-level-3)'
}

const actionsStyle: CSSProperties = {
  display: 'flex',
  justifyContent: 'flex-end',
  gap: 8,
  marginTop: 22
}
