import { useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { AlertTriangle, Check, Copy, Monitor } from 'lucide-react'
import { Button, IconButton, Input, LayerBoundary, ModalHeader, Skeleton } from '@dotcraft/plugin'
import { useT } from '../i18n'
import { useSatellitesStore } from '../state'
import { isInviteExpired } from '../model'

export function SatelliteInviteDialog({ onClose }: { onClose: () => void }): JSX.Element {
  const t = useT()
  const state = useSatellitesStore((state) => state)
  const { invite, creatingInvite: creating, inviteError, createInvite, clearInvite } = state
  const [copied, setCopied] = useState(false)
  const [, refreshExpiry] = useState(0)
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const minted = useRef(false)
  const expired = invite != null && isInviteExpired(invite)
  const hasLink = invite != null && !expired

  useEffect(() => {
    const opener = document.activeElement
    return () => { if (opener instanceof HTMLElement && opener.isConnected) opener.focus() }
  }, [])
  useEffect(() => {
    if (minted.current) return
    minted.current = true
    if (invite == null) void createInvite()
  }, [createInvite, invite])
  useEffect(() => {
    setCopied(false)
    if (!invite) return
    dialogRef.current?.querySelector('input')?.select()
    const timer = setTimeout(() => refreshExpiry((value) => value + 1), Math.max(0, Date.parse(invite.expiresAt) - Date.now()))
    return () => clearTimeout(timer)
  }, [invite])
  useEffect(() => {
    const dialog = dialogRef.current
    dialog?.querySelector<HTMLElement>('button')?.focus()
    function onKey(event: KeyboardEvent): void {
      if (event.key === 'Escape') { event.preventDefault(); onClose(); return }
      if (event.key !== 'Tab' || !dialog) return
      const focusable = [...dialog.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled])')]
      const first = focusable[0]
      const last = focusable.at(-1)
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])
  async function copy(): Promise<void> {
    if (!invite || !navigator.clipboard) return
    try { await navigator.clipboard.writeText(invite.url); setCopied(true) } catch { }
  }
  return createPortal(<LayerBoundary blocksNativeViews><div className="satellite-dialog-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose() }}>
    <div ref={dialogRef} className="satellite-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId}>
      <div className="satellite-dialog-header">
        <ModalHeader icon={<Monitor size={18} />} title={t('settings.satellites.invite.title')} titleId={titleId} description={t('settings.satellites.invite.description')} onClose={onClose} closeLabel={t('common.close')} />
      </div>
      <div className="satellite-dialog-body">
        {invite ? <div className="dc-satellite-invite-result">
          <label className="dc-satellite-field__label" htmlFor={`${titleId}-link`}>{t('settings.satellites.invite.linkLabel')}</label>
          <div className="satellite-invite-field"><Input id={`${titleId}-link`} mono readOnly value={invite.url} />{!expired && <IconButton icon={copied ? <Check size={15} /> : <Copy size={15} />} label={t(copied ? 'settings.satellites.invite.copied' : 'settings.satellites.invite.copy')} onClick={() => void copy()} />}</div>
          <p className="dc-satellite-invite-note">{t(expired ? 'settings.satellites.invite.expired' : 'settings.satellites.invite.created')}</p>
        </div> : inviteError != null ? <div className="satellite-banner"><AlertTriangle size={20} /><div><strong>{t('settings.satellites.invite.error')}</strong>{inviteError && <p>{inviteError}</p>}</div></div> : <div className="dc-satellite-invite-result" role="status" aria-busy="true" aria-label={t('settings.satellites.invite.creating')}><span className="dc-satellite-field__label">{t('settings.satellites.invite.linkLabel')}</span><Skeleton height={34} radius={8} /><Skeleton width="72%" height={11} /></div>}
      </div>
      <div className="satellite-dialog-footer">
        {hasLink ? <><Button variant="secondary" onClick={() => { clearInvite(); void createInvite() }}>{t('settings.satellites.invite.another')}</Button><Button variant="primary" onClick={onClose}>{t('settings.satellites.invite.done')}</Button></> : <><Button variant="secondary" onClick={onClose}>{t('common.cancel')}</Button><Button variant="primary" loading={creating} onClick={() => void createInvite()}>{t(expired ? 'settings.satellites.invite.newLink' : 'settings.satellites.invite.create')}</Button></>}
      </div>
    </div>
  </div></LayerBoundary>, document.body)
}
