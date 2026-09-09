import { useCallback, useEffect, useId, useRef, useState, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { AlertTriangle, Check, Copy, Monitor } from 'lucide-react'

import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { InputWithAction } from '../../../ui/InputWithAction'
import { ModalHeader } from '../../../ui/ModalHeader'
import { Skeleton } from '../../../ui/Skeleton'
import { LayerBoundary } from '../../../../contexts/LayerContext'
import { useT } from '../../../../contexts/LocaleContext'
import { useSatellitesStore } from '../../../../stores/satellitesStore'
import { isInviteExpired, type SatelliteInvite } from '../../../../../shared/satellites'
import * as s from '../servers/serversStyles'

/** The minted link lives in the store, so closing and reopening shows it again. */
export function SatelliteInviteDialog({ onClose }: { onClose: () => void }): JSX.Element {
  const t = useT()
  const invite = useSatellitesStore((state) => state.invite)
  const creating = useSatellitesStore((state) => state.creatingInvite)
  const inviteError = useSatellitesStore((state) => state.inviteError)
  const createInvite = useSatellitesStore((state) => state.createInvite)
  const clearInvite = useSatellitesStore((state) => state.clearInvite)
  const [copied, setCopied] = useState(false)
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const resultRef = useRef<HTMLDivElement>(null)
  const minted = useRef(false)

  const expired = invite != null && isInviteExpired(invite)
  const hasLink = invite != null && !expired

  // Whatever opened the dialog gets the focus back when it closes, so this runs
  // before anything here moves the focus.
  useEffect(() => {
    const opener = document.activeElement
    return () => {
      if (opener instanceof HTMLElement && opener.isConnected) opener.focus()
    }
  }, [])

  useEffect(() => {
    setCopied(false)
  }, [invite?.url])

  // There is nothing to fill in, so opening the dialog is the request. Every later
  // link comes from Create another or a retry.
  useEffect(() => {
    if (minted.current) return
    minted.current = true
    if (invite == null) void createInvite()
  }, [createInvite, invite])

  // The link is the point of the dialog, so it arrives selected and ready to copy.
  useEffect(() => {
    if (invite == null) return
    resultRef.current?.querySelector('input')?.select()
  }, [invite])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        onClose()
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
  }, [onClose])

  const create = useCallback(() => {
    void createInvite()
  }, [createInvite])

  function another(): void {
    clearInvite()
    void createInvite()
  }

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className="dc-satellite-invite-scrim"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
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
          onClose={onClose}
          closeLabel={t('common.close')}
        />

        {invite ? (
          <div ref={resultRef}>
            <InviteResult invite={invite} expired={expired} copied={copied} onCopy={() => setCopied(true)} />
          </div>
        ) : inviteError != null ? (
          <div className="dc-satellite-invite-banner" style={s.banner}>
            <span className="dc-satellite-invite-banner__glyph" aria-hidden>
              <AlertTriangle size={20} />
            </span>
            <div style={{ flex: 1 }}>
              <div className="dc-satellite-invite-banner__text">{t('settings.satellites.invite.error')}</div>
              {inviteError !== '' && <div className="dc-satellite-invite-banner__reason">{inviteError}</div>}
            </div>
          </div>
        ) : (
          <div
            className="dc-satellite-invite-result"
            role="status"
            aria-busy="true"
            aria-label={t('settings.satellites.invite.creating')}
          >
            <span className="dc-satellite-field__label">{t('settings.satellites.invite.linkLabel')}</span>
            <Skeleton height={34} radius={8} />
            <Skeleton width="72%" height={11} style={{ marginTop: '2px' }} />
          </div>
        )}

        <div className="dc-satellite-invite-foot">
          {hasLink ? (
            <>
              <Button variant="secondary" onClick={another}>
                {t('settings.satellites.invite.another')}
              </Button>
              <Button variant="primary" onClick={onClose}>
                {t('settings.satellites.invite.done')}
              </Button>
            </>
          ) : (
            <>
              <Button variant="secondary" onClick={onClose}>
                {t('common.cancel')}
              </Button>
              <Button variant="primary" loading={creating} onClick={create}>
                {expired
                  ? t('settings.satellites.invite.newLink')
                  : t('settings.satellites.invite.create')}
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
        {expired ? t('settings.satellites.invite.expired') : t('settings.satellites.invite.created')}
      </p>
    </div>
  )
}
