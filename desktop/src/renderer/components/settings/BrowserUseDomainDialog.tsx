import { useEffect, useId } from 'react'
import { Globe } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'

interface BrowserUseDomainDialogProps {
  target: 'blocked' | 'allowed'
  value: string
  error: string
  onChange: (value: string) => void
  onCancel: () => void
  onSubmit: () => void
}

export function BrowserUseDomainDialog({ target, value, error, onChange, onCancel, onSubmit }: BrowserUseDomainDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  const blocked = target === 'blocked'

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  return (
    <ModalDialog
      titleId={titleId}
      width={420}
      onDismiss={onCancel}
      onSubmit={onSubmit}
      header={(
        <ModalHeader
          icon={<Globe size={18} strokeWidth={2} aria-hidden />}
          title={blocked ? t('settings.browserUse.addBlockedDomain') : t('settings.browserUse.addAllowedDomain')}
          titleId={titleId}
          description={blocked ? t('settings.browserUse.addBlockedDomainHint') : t('settings.browserUse.addAllowedDomainHint')}
          onClose={onCancel}
          closeLabel={t('settings.browserUse.cancel')}
        />
      )}
      error={error || null}
      footer={(
        <ModalFooter>
          <Button onClick={onCancel}>{t('settings.browserUse.cancel')}</Button>
          <Button type="submit" variant="primary">{t('settings.browserUse.add')}</Button>
        </ModalFooter>
      )}
    >
      <Input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="example.com"
        aria-labelledby={titleId}
        autoFocus
        mono
      />
    </ModalDialog>
  )
}
