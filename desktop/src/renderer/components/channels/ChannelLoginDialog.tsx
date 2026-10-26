import { useId, useRef, type JSX } from 'react'
import { CircleCheck } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { IdentityMark } from '../ui/IdentityMark'
import { IdentityMarkFallback } from '../ui/IdentityMarkFallback'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import { Skeleton } from '../ui/Skeleton'
import { useImportDialogFocus } from '../settings/panels/useImportDialogFocus'
import { PhonesDialogFailure } from '../settings/panels/phones/PhonesDialog'
import styles from './ChannelLoginDialog.module.css'

export type ChannelLoginPhase = 'qr' | 'connected' | 'failed'

interface ChannelLoginDialogProps {
  channelName: string
  logoPath?: string
  phase: ChannelLoginPhase
  qrDataUrl: string | null
  onCancel: () => void
  onDone: () => void
  onRetry: () => void
}

export function ChannelLoginDialog({
  channelName,
  logoPath,
  phase,
  qrDataUrl,
  onCancel,
  onDone,
  onRetry
}: ChannelLoginDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const close = phase === 'connected' ? onDone : onCancel
  useImportDialogFocus(dialogRef, close, false)

  let body: JSX.Element
  let footer: JSX.Element
  if (phase === 'connected') {
    body = (
      <div className={styles.connected} role="status">
        <CircleCheck size={32} strokeWidth={1.7} aria-hidden />
        <strong>{t('channels.login.connectedTitle', { name: channelName })}</strong>
        <span>{t('channels.login.connectedDescription')}</span>
      </div>
    )
    footer = (
      <Button variant="primary" onClick={onDone}>
        {t('channels.login.done')}
      </Button>
    )
  } else if (phase === 'failed') {
    body = (
      <PhonesDialogFailure
        title={t('channels.login.failedTitle')}
        reason={t('channels.login.failedReason')}
      />
    )
    footer = (
      <>
        <Button variant="secondary" onClick={onCancel}>
          {t('common.cancel')}
        </Button>
        <Button variant="primary" onClick={onRetry}>
          {t('channels.login.retry')}
        </Button>
      </>
    )
  } else {
    body = (
      <div className={styles.body}>
        {qrDataUrl ? (
          <div className={styles.qr}>
            <img
              src={qrDataUrl}
              alt={t('channels.login.qrLabel', { name: channelName })}
              width={184}
              height={184}
            />
          </div>
        ) : (
          <div
            className={styles.qrPending}
            role="status"
            aria-busy="true"
            aria-label={t('channels.login.preparing')}
          >
            <Skeleton width={184} height={184} radius={8} />
          </div>
        )}
        <p className={styles.note}>{t('channels.login.note', { name: channelName })}</p>
        <p className={styles.status} role="status">
          {qrDataUrl ? t('channels.login.waiting') : t('channels.login.preparing')}
        </p>
      </div>
    )
    footer = (
      <Button variant="secondary" onClick={onCancel}>
        {t('common.cancel')}
      </Button>
    )
  }

  return (
    <ModalDialog
      ref={dialogRef}
      titleId={titleId}
      width={460}
      onDismiss={close}
      header={(
        <ModalHeader
          icon={(
            <IdentityMark
              role="compact"
              size={36}
              src={logoPath}
              fallback={<IdentityMarkFallback kind="channel" />}
            />
          )}
          badgedIcon={false}
          title={t('channels.login.title', { name: channelName })}
          titleId={titleId}
          onClose={close}
          closeLabel={t('common.close')}
        />
      )}
      footer={<ModalFooter>{footer}</ModalFooter>}
    >
      {body}
    </ModalDialog>
  )
}
