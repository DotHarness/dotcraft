import { useCallback, useEffect, useId, useRef, useState, type JSX } from 'react'
import { Check, CircleCheck, Copy, Smartphone } from 'lucide-react'

import { Button } from '../../../ui/Button'
import { ModalHeader } from '../../../ui/ModalHeader'
import { Skeleton } from '../../../ui/Skeleton'
import { useT } from '../../../../contexts/LocaleContext'
import { useCodexRemoteStore } from '../../../../stores/codexRemoteStore'
import { PairingQrCode } from './PairingQrCode'
import { PhonesDialog, PhonesDialogFailure } from './PhonesDialog'
import { formatCountdown } from './phonesFormat'

function secondsUntil(value: string): number {
  return Math.max(0, Math.ceil((Date.parse(value) - Date.now()) / 1000))
}

function useSecondTick(active: boolean): void {
  const [, setTick] = useState(0)
  useEffect(() => {
    if (!active) return
    const timer = window.setInterval(() => setTick((tick) => tick + 1), 1000)
    return () => window.clearInterval(timer)
  }, [active])
}

export function CodexPairDialog({ hostName, onClose }: { hostName: string; onClose: () => void }): JSX.Element {
  const t = useT()
  const pairing = useCodexRemoteStore((state) => state.pairing)
  const startPairing = useCodexRemoteStore((state) => state.startPairing)
  const closePairing = useCodexRemoteStore((state) => state.closePairing)
  const titleId = useId()
  const minted = useRef(false)
  const [copied, setCopied] = useState<string | null>(null)

  const secondsLeft = pairing.step === 'code' ? secondsUntil(pairing.pairing.expiresAt) : 0
  const counting = pairing.step === 'code' && secondsLeft > 0
  const expired = pairing.step === 'code' && !counting
  const creating = pairing.step === 'creating' || pairing.step === 'idle'
  useSecondTick(counting)

  const close = useCallback(() => {
    closePairing()
    onClose()
  }, [closePairing, onClose])

  useEffect(() => {
    if (minted.current) return
    minted.current = true
    void startPairing()
  }, [startPairing])

  let body: JSX.Element
  if (pairing.step === 'error') {
    body = (
      <PhonesDialogFailure
        title={t('settings.codexApp.pairDialog.error')}
        reason={
          pairing.failure.code === 'hostOff'
            ? t('settings.codexApp.pairDialog.hostOff')
            : pairing.failure.message || t('settings.phones.hubNoAnswer')
        }
      />
    )
  } else if (pairing.step === 'paired') {
    body = (
      <div className="dc-add-phone__paired" role="status">
        <CircleCheck size={32} strokeWidth={1.7} aria-hidden />
        <strong>{t('settings.codexApp.pairDialog.pairedTitle')}</strong>
        <span>{t('settings.codexApp.pairDialog.pairedDescription', { name: hostName })}</span>
      </div>
    )
  } else {
    body = (
      <div className="dc-add-phone__body">
        {pairing.step === 'code' ? (
          <PairingQrCode payload={pairing.pairing.pairingUrl} expired={expired} />
        ) : (
          <div className="dc-add-phone__qr" role="status" aria-busy="true" aria-label={t('settings.phones.pair.creatingLabel')}>
            <Skeleton width={184} height={184} radius={8} />
          </div>
        )}
        {pairing.step === 'code' && !expired && pairing.pairing.manualCode && (
          <p className="dc-codex-pair__manual">
            <span>{t('settings.codexApp.pairDialog.manualCode')}</span>
            <code>{pairing.pairing.manualCode}</code>
          </p>
        )}
        {pairing.step === 'code' && !expired && (
          <Button
            variant="secondary"
            iconLeft={copied === pairing.pairing.pairingUrl ? <Check size={15} aria-hidden /> : <Copy size={15} aria-hidden />}
            onClick={() => {
              void navigator.clipboard?.writeText(pairing.pairing.pairingUrl).catch(() => undefined)
              setCopied(pairing.pairing.pairingUrl)
            }}
          >
            {t(copied === pairing.pairing.pairingUrl ? 'settings.phones.pair.linkCopied' : 'settings.phones.pair.copyLink')}
          </Button>
        )}
        <p className="dc-add-phone__note">{t('settings.codexApp.pairDialog.note')}</p>
        <p className="dc-add-phone__status" role={counting ? 'timer' : 'status'}>
          {creating
            ? t('settings.phones.pair.creating')
            : expired
              ? t('settings.phones.pair.expired')
              : t('settings.phones.pair.expiresIn', { time: formatCountdown(secondsLeft) })}
        </p>
      </div>
    )
  }

  const footer =
    pairing.step === 'paired' ? (
      <Button variant="primary" onClick={close}>
        {t('settings.phones.pair.done')}
      </Button>
    ) : expired || pairing.step === 'error' ? (
      <>
        <Button variant="secondary" onClick={close}>
          {t('common.cancel')}
        </Button>
        <Button variant="primary" onClick={() => void startPairing()}>
          {pairing.step === 'error' ? t('settings.phones.retry') : t('settings.phones.pair.newCode')}
        </Button>
      </>
    ) : (
      <Button variant="secondary" onClick={close}>
        {t('common.cancel')}
      </Button>
    )

  return (
    <PhonesDialog
      titleId={titleId}
      onClose={close}
      header={
        <ModalHeader
          icon={<Smartphone size={18} aria-hidden />}
          title={t('settings.codexApp.pairDialog.title')}
          titleId={titleId}
          onClose={close}
          closeLabel={t('common.close')}
        />
      }
      footer={footer}
    >
      {body}
    </PhonesDialog>
  )
}
