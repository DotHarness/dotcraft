import { useCallback, useEffect, useId, useRef, useState, type JSX } from 'react'
import { CircleCheck, RefreshCw, Smartphone } from 'lucide-react'

import { Button } from '../../../ui/Button'
import { ModalHeader } from '../../../ui/ModalHeader'
import { Skeleton } from '../../../ui/Skeleton'
import { useT } from '../../../../contexts/LocaleContext'
import { useMobileStore } from '../../../../stores/mobileStore'
import { pairingSecondsLeft } from '../../../../../shared/mobile'
import { PairingQrCode } from './PairingQrCode'
import { PhonesDialog, PhonesDialogFailure } from './PhonesDialog'
import { failureReason, formatCountdown } from './phonesFormat'

function useSecondTick(active: boolean): void {
  const [, setTick] = useState(0)
  useEffect(() => {
    if (!active) return
    const timer = window.setInterval(() => setTick((tick) => tick + 1), 1000)
    return () => window.clearInterval(timer)
  }, [active])
}

export function AddPhoneDialog({ onClose }: { onClose: () => void }): JSX.Element {
  const t = useT()
  const pairing = useMobileStore((state) => state.pairing)
  const startPairing = useMobileStore((state) => state.startPairing)
  const closePairing = useMobileStore((state) => state.closePairing)
  const titleId = useId()
  const minted = useRef(false)

  const secondsLeft = pairing.step === 'code' ? pairingSecondsLeft(pairing.pairing) : 0
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

  const restart = (): void => void startPairing()

  let body: JSX.Element
  if (pairing.step === 'error') {
    body = (
      <PhonesDialogFailure
        title={t('settings.phones.pair.error')}
        reason={failureReason(t, pairing.failure, 'settings.phones.hubNoAnswer')}
      />
    )
  } else if (pairing.step === 'paired') {
    body = (
      <div className="dc-add-phone__paired" role="status">
        <CircleCheck size={32} strokeWidth={1.7} aria-hidden />
        <strong>{t('settings.phones.pair.pairedTitle', { name: pairing.displayName })}</strong>
        <span>{t('settings.phones.pair.pairedDescription')}</span>
      </div>
    )
  } else {
    body = (
      <div className="dc-add-phone__body">
        {pairing.step === 'code' ? (
          <PairingQrCode payload={pairing.pairing.qrPayload} expired={expired} />
        ) : (
          <div
            className="dc-add-phone__qr"
            role="status"
            aria-busy="true"
            aria-label={t('settings.phones.pair.creatingLabel')}
          >
            <Skeleton width={184} height={184} radius={8} />
          </div>
        )}
        <p className="dc-add-phone__note">{t('settings.phones.pair.note')}</p>
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

  let footer: JSX.Element
  if (pairing.step === 'paired') {
    footer = (
      <Button variant="primary" onClick={close}>
        {t('settings.phones.pair.done')}
      </Button>
    )
  } else if (expired || pairing.step === 'error') {
    footer = (
      <>
        <Button variant="secondary" onClick={close}>
          {t('common.cancel')}
        </Button>
        <Button variant="primary" onClick={restart}>
          {pairing.step === 'error' ? t('settings.phones.retry') : t('settings.phones.pair.newCode')}
        </Button>
      </>
    )
  } else {
    footer = (
      <>
        <Button variant="secondary" onClick={close}>
          {t('common.cancel')}
        </Button>
        <Button
          variant="secondary"
          iconLeft={<RefreshCw size={15} aria-hidden />}
          disabled={creating}
          onClick={restart}
        >
          {t('settings.phones.pair.refresh')}
        </Button>
      </>
    )
  }

  return (
    <PhonesDialog titleId={titleId} onClose={close}>
      <ModalHeader
        icon={<Smartphone size={18} aria-hidden />}
        title={t('settings.phones.pair.title')}
        titleId={titleId}
        onClose={close}
        closeLabel={t('common.close')}
      />
      {body}
      <div className="dc-satellite-invite-foot">{footer}</div>
    </PhonesDialog>
  )
}
