import { useCallback, useEffect, useId, useRef, useState, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { AlertTriangle, Check, Copy, Monitor } from 'lucide-react'

import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { InputWithAction } from '../../../ui/InputWithAction'
import { ModalHeader } from '../../../ui/ModalHeader'
import { LayerBoundary } from '../../../../contexts/LayerContext'
import { useT } from '../../../../contexts/LocaleContext'
import { useSatellitesStore } from '../../../../stores/satellitesStore'
import { isInviteExpired, type SatelliteInvite } from '../../../../../shared/satellites'
import * as s from '../servers/serversStyles'

const PURPOSE_MAX_LENGTH = 280

/** The minted link lives in the store, so closing and reopening shows it again. */
export function SatelliteInviteDialog({ onClose }: { onClose: () => void }): JSX.Element {
  const t = useT()
  const invite = useSatellitesStore((state) => state.invite)
  const creating = useSatellitesStore((state) => state.creatingInvite)
  const inviteError = useSatellitesStore((state) => state.inviteError)
  const createInvite = useSatellitesStore((state) => state.createInvite)
  const clearInvite = useSatellitesStore((state) => state.clearInvite)
  const [purpose, setPurpose] = useState('')
  const [folder, setFolder] = useState('')
  const [copied, setCopied] = useState(false)
  const titleId = useId()
  const purposeId = useId()
  const folderId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const purposeRef = useRef<HTMLInputElement>(null)

  const expired = invite != null && isInviteExpired(invite)
  const dismissable = !creating
  const showForm = invite == null

  useEffect(() => {
    setCopied(false)
  }, [invite?.url])

  useEffect(() => {
    if (showForm) purposeRef.current?.focus()
  }, [showForm])

  // Whatever opened the dialog gets the focus back when it closes.
  useEffect(() => {
    const opener = document.activeElement
    return () => {
      if (opener instanceof HTMLElement && opener.isConnected) opener.focus()
    }
  }, [])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        if (dismissable) onClose()
        return
      }
      if (event.key !== 'Tab' || !dialogRef.current) return
      const focusable = Array.from(
        dialogRef.current.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled])')
      )
      if (focusable.length === 0) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [dismissable, onClose])

  const create = useCallback(() => {
    void createInvite({
      ...(purpose.trim() ? { purpose: purpose.trim() } : {}),
      ...(folder.trim() ? { folder: folder.trim() } : {})
    })
  }, [createInvite, folder, purpose])

  function another(): void {
    clearInvite()
    setPurpose('')
    setFolder('')
  }

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className="dc-satellite-invite-scrim"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && dismissable) onClose()
      }}
    >
      <div
        ref={dialogRef}
        className="dc-satellite-invite-dialog"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <ModalHeader
          icon={<Monitor size={18} aria-hidden />}
          title={t('settings.satellites.invite.title')}
          titleId={titleId}
          description={t('settings.satellites.invite.description')}
          onClose={dismissable ? onClose : undefined}
          closeLabel={t('common.close')}
        />

        {invite ? (
          <InviteResult invite={invite} expired={expired} copied={copied} onCopy={() => setCopied(true)} />
        ) : (
          <>
            <div className="dc-satellite-field">
              <label className="dc-satellite-field__label" htmlFor={purposeId}>
                {t('settings.satellites.invite.purpose')}
              </label>
              <Input
                id={purposeId}
                ref={purposeRef}
                disabled={creating}
                maxLength={PURPOSE_MAX_LENGTH}
                value={purpose}
                onChange={(event) => setPurpose(event.target.value)}
              />
              <p className="dc-satellite-field__hint">{t('settings.satellites.invite.purposeHint')}</p>
            </div>

            <div className="dc-satellite-field">
              <label className="dc-satellite-field__label" htmlFor={folderId}>
                {t('settings.satellites.invite.folder')}
              </label>
              <Input
                id={folderId}
                mono
                disabled={creating}
                value={folder}
                onChange={(event) => setFolder(event.target.value)}
              />
              <p className="dc-satellite-field__hint">{t('settings.satellites.invite.folderHint')}</p>
            </div>

            {inviteError != null && (
              <div className="dc-satellite-invite-banner" style={s.banner}>
                <span className="dc-satellite-invite-banner__glyph" aria-hidden>
                  <AlertTriangle size={20} />
                </span>
                <div style={{ flex: 1 }}>
                  <div className="dc-satellite-invite-banner__text">{t('settings.satellites.invite.error')}</div>
                  {inviteError !== '' && <div className="dc-satellite-invite-banner__reason">{inviteError}</div>}
                </div>
              </div>
            )}
          </>
        )}

        <div className="dc-satellite-invite-foot">
          {invite == null ? (
            <>
              <Button variant="secondary" disabled={creating} onClick={onClose}>
                {t('common.cancel')}
              </Button>
              <Button variant="primary" loading={creating} onClick={create}>
                {t('settings.satellites.invite.create')}
              </Button>
            </>
          ) : expired ? (
            <>
              <Button variant="secondary" onClick={onClose}>
                {t('common.cancel')}
              </Button>
              <Button variant="primary" loading={creating} onClick={create}>
                {t('settings.satellites.invite.newLink')}
              </Button>
            </>
          ) : (
            <>
              <Button variant="secondary" onClick={another}>
                {t('settings.satellites.invite.another')}
              </Button>
              <Button variant="primary" onClick={onClose}>
                {t('settings.satellites.invite.done')}
              </Button>
            </>
          )}
        </div>
      </div>
    </div>
  )

  return createPortal(<LayerBoundary>{dialog}</LayerBoundary>, document.body) as JSX.Element
}

function InviteResult({
  invite,
  expired,
  copied,
  onCopy
}: {
  invite: SatelliteInvite
  expired: boolean
  copied: boolean
  onCopy: () => void
}): JSX.Element {
  const t = useT()
  const fieldId = useId()

  function copy(): void {
    onCopy()
    void navigator.clipboard?.writeText(invite.url).catch(() => undefined)
  }

  return (
    <div className="dc-satellite-invite-result">
      <label className="dc-satellite-field__label" htmlFor={fieldId}>
        {t('settings.satellites.invite.linkLabel')}
      </label>
      {expired ? (
        <Input id={fieldId} mono readOnly value={invite.url} />
      ) : (
        <InputWithAction
          id={fieldId}
          mono
          value={invite.url}
          onChange={() => undefined}
          actionIcon={copied ? <Check size={15} aria-hidden /> : <Copy size={15} aria-hidden />}
          actionLabel={
            copied ? t('settings.satellites.invite.copied') : t('settings.satellites.invite.copy')
          }
          onAction={copy}
        />
      )}
      <p className="dc-satellite-invite-note">
        {copied && !expired && (
          <span className="dc-satellite-invite-note__glyph" aria-hidden>
            <Check size={14} />
          </span>
        )}
        <span>
          {expired
            ? t('settings.satellites.invite.expired')
            : copied
              ? t('settings.satellites.invite.copiedNote')
              : t('settings.satellites.invite.created')}
        </span>
      </p>
    </div>
  )
}
