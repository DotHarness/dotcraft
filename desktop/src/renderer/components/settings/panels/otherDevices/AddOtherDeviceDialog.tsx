import { useCallback, useEffect, useId, useState, type JSX } from 'react'
import { CircleCheck, Monitor } from 'lucide-react'
import { parsePairingUrl } from '@dotcraft/sdk/gateway'

import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { ModalHeader } from '../../../ui/ModalHeader'
import { Spinner } from '../../../ui/Spinner'
import { useT } from '../../../../contexts/LocaleContext'
import { usePairedComputersStore } from '../../../../stores/pairedComputersStore'
import type { MessageKey } from '../../../../../shared/locales'
import type { PairingFailureCode } from '../../../../../shared/pairedComputers'
import { PhonesDialog, PhonesDialogFailure } from '../phones/PhonesDialog'

const FAILURE_KEY: Record<PairingFailureCode, MessageKey> = {
  invalidLink: 'settings.otherDevices.pair.failure.invalidLink',
  ownComputer: 'settings.otherDevices.pair.failure.ownComputer',
  pairingDenied: 'settings.otherDevices.pair.failure.denied',
  pairingCodeInvalid: 'settings.otherDevices.pair.failure.expired',
  unreachable: 'settings.otherDevices.pair.failure.unreachable',
  storageUnavailable: 'settings.otherDevices.pair.failure.storage'
}

export function AddOtherDeviceDialog({
  initialLink = '',
  onClose,
  onOpenComputer
}: {
  initialLink?: string
  onClose: () => void
  onOpenComputer: (computerId: string) => void
}): JSX.Element {
  const t = useT()
  const titleId = useId()
  const linkId = useId()
  const flow = usePairedComputersStore((state) => state.flow)
  const pair = usePairedComputersStore((state) => state.pair)
  const cancelPairing = usePairedComputersStore((state) => state.cancelPairing)
  const resetFlow = usePairedComputersStore((state) => state.resetFlow)
  const [link, setLink] = useState(initialLink)
  const target = parsePairingUrl(link)

  const close = useCallback(() => {
    if (flow.step === 'connecting' || flow.step === 'waiting') cancelPairing()
    else resetFlow()
    onClose()
  }, [cancelPairing, flow.step, onClose, resetFlow])

  useEffect(() => () => usePairedComputersStore.getState().resetFlow(), [])

  let body: JSX.Element
  let footer: JSX.Element
  if (flow.step === 'paired') {
    body = (
      <div className="dc-add-phone__paired" role="status">
        <CircleCheck size={32} strokeWidth={1.7} aria-hidden />
        <strong>{t('settings.otherDevices.pair.pairedTitle', { name: flow.computer.name })}</strong>
        <span>{t('settings.otherDevices.pair.pairedDescription')}</span>
      </div>
    )
    footer = (
      <>
        <Button variant="secondary" onClick={close}>
          {t('settings.phones.pair.done')}
        </Button>
        <Button variant="primary" onClick={() => onOpenComputer(flow.computer.computerId)}>
          {t('settings.otherDevices.pair.chooseProjects')}
        </Button>
      </>
    )
  } else if (flow.step === 'connecting' || flow.step === 'waiting') {
    const waiting = flow.step === 'waiting'
    body = (
      <div className="dc-add-phone__request" role="status">
        {waiting ? <Monitor size={32} strokeWidth={1.7} aria-hidden /> : <Spinner size={28} />}
        <strong>
          {t(waiting ? 'settings.otherDevices.pair.waitingTitle' : 'settings.otherDevices.pair.connectingTitle', {
            name: flow.name
          })}
        </strong>
        {waiting && <span>{t('settings.otherDevices.pair.waitingDescription', { name: flow.name })}</span>}
      </div>
    )
    footer = (
      <Button variant="secondary" onClick={close}>
        {t('common.cancel')}
      </Button>
    )
  } else {
    const failure = flow.failure
    body = (
      <>
        <ol className="dc-other-device-steps">
          <li>{t('settings.otherDevices.pair.step.open')}</li>
          <li>{t('settings.otherDevices.pair.step.add')}</li>
          <li>{t('settings.otherDevices.pair.step.paste')}</li>
        </ol>
        <div className="dc-phones-relay-form__field">
          <label className="dc-satellite-field__label" htmlFor={linkId}>
            {t('settings.otherDevices.pair.linkLabel')}
          </label>
          <Input
            id={linkId}
            data-autofocus
            mono
            value={link}
            placeholder="dotcraft://pair?…"
            onChange={(event) => {
              setLink(event.target.value)
              if (failure) resetFlow()
            }}
          />
          {target && !failure && (
            <span className="dc-other-device-target">
              <Monitor size={14} aria-hidden />
              {target.name}
            </span>
          )}
        </div>
        {failure && (
          <PhonesDialogFailure
            title={t(FAILURE_KEY[failure.code], { name: failure.name ?? target?.name ?? '' })}
            reason={t(
              failure.code === 'unreachable' || failure.code === 'storageUnavailable' || failure.code === 'ownComputer'
                ? `${FAILURE_KEY[failure.code]}.detail` as MessageKey
                : 'settings.otherDevices.pair.failure.newLink'
            )}
          />
        )}
      </>
    )
    footer = (
      <>
        <Button variant="secondary" onClick={close}>
          {t('common.cancel')}
        </Button>
        <Button type="submit" variant="primary" disabled={link.trim() === ''}>
          {t('settings.otherDevices.pair.continue')}
        </Button>
      </>
    )
  }

  return (
    <PhonesDialog
      titleId={titleId}
      onClose={close}
      onSubmit={flow.step === 'input' ? () => void pair(link) : undefined}
      header={(
        <ModalHeader
          icon={<Monitor size={18} aria-hidden />}
          title={t('settings.otherDevices.add')}
          titleId={titleId}
          onClose={close}
          closeLabel={t('common.close')}
        />
      )}
      footer={footer}
    >
      {body}
    </PhonesDialog>
  )
}
