import { useId, useState, type JSX } from 'react'
import { Globe } from 'lucide-react'

import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { ModalHeader } from '../../../ui/ModalHeader'
import { SecretInput } from '../../../channels/FormShared'
import { useT } from '../../../../contexts/LocaleContext'
import { useMobileStore } from '../../../../stores/mobileStore'
import type { MobileFailure } from '../../../../../shared/mobile'
import { PhonesDialog, PhonesDialogFailure } from './PhonesDialog'
import { failureReason } from './phonesFormat'

export function RelayDialog({ initialUrl, onClose }: { initialUrl: string; onClose: () => void }): JSX.Element {
  const t = useT()
  const titleId = useId()
  const urlId = useId()
  const tokenId = useId()
  const [url, setUrl] = useState(initialUrl)
  const [token, setToken] = useState('')
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<MobileFailure | null>(null)
  const ready = url.trim() !== '' && token.trim() !== ''

  async function save(): Promise<void> {
    setSaving(true)
    setFailure(null)
    const result = await useMobileStore.getState().setRelay(url.trim(), token.trim())
    setSaving(false)
    if (result) setFailure(result)
    else onClose()
  }

  return (
    <PhonesDialog titleId={titleId} onClose={onClose}>
      <ModalHeader
        icon={<Globe size={18} aria-hidden />}
        title={t('settings.phones.relay.title')}
        titleId={titleId}
        description={t('settings.phones.relay.dialog.description')}
        onClose={onClose}
        closeLabel={t('common.close')}
      />
      <form
        noValidate
        onSubmit={(event) => {
          event.preventDefault()
          if (ready && !saving) void save()
        }}
      >
        <div className="dc-phones-relay-form">
          <div className="dc-phones-relay-form__field">
            <label className="dc-satellite-field__label" htmlFor={urlId}>
              {t('settings.phones.relay.dialog.address')}
            </label>
            <Input
              id={urlId}
              data-autofocus
              mono
              value={url}
              placeholder="wss://relay.example.com"
              onChange={(event) => setUrl(event.target.value)}
            />
          </div>
          <div className="dc-phones-relay-form__field">
            <label className="dc-satellite-field__label" htmlFor={tokenId}>
              {t('settings.phones.relay.dialog.token')}
            </label>
            <SecretInput
              id={tokenId}
              mono
              value={token}
              placeholder={t('settings.phones.relay.dialog.tokenPlaceholder')}
              onChange={setToken}
            />
          </div>
        </div>
        {failure && (
          <PhonesDialogFailure
            title={t('settings.phones.relay.dialog.failed')}
            reason={
              failure.code === 'invalidRequest'
                ? t('settings.phones.relay.dialog.invalidUrl')
                : failureReason(t, failure, 'settings.phones.hubNoAnswer')
            }
          />
        )}
        <div className="dc-satellite-invite-foot">
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" variant="primary" loading={saving} disabled={!ready}>
            {t('settings.phones.relay.dialog.save')}
          </Button>
        </div>
      </form>
    </PhonesDialog>
  )
}
