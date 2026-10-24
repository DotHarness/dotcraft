import { useEffect, useId } from 'react'
import { Mic } from 'lucide-react'

import { useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'

export type VoiceSetupStage = 'setup' | 'recovery'

interface VoiceSetupDialogProps {
  stage: VoiceSetupStage
  onContinue(): void
  onCancel(): void
}

export function VoiceSetupDialog({ stage, onContinue, onCancel }: VoiceSetupDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  const setup = stage === 'setup'
  return (
    <ModalDialog
      titleId={titleId}
      width={420}
      onDismiss={onCancel}
      header={(
        <ModalHeader
          icon={<Mic size={18} strokeWidth={2} aria-hidden />}
          title={t(setup ? 'voice.setup.title' : 'voice.permissionRecovery.title')}
          titleId={titleId}
          description={t(setup ? 'voice.setup.description' : 'voice.permissionRecovery.description')}
          onClose={onCancel}
          closeLabel={t('common.close')}
        />
      )}
      footer={(
        <ModalFooter>
          <Button variant="ghost" onClick={onCancel}>
            {t('voice.setup.notNow')}
          </Button>
          <Button variant="primary" onClick={onContinue}>
            {t(setup ? 'voice.setup.action' : 'voice.permissionRecovery.action')}
          </Button>
        </ModalFooter>
      )}
    />
  )
}
