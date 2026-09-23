import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { KeyRound, ShieldCheck } from 'lucide-react'
import { ActionTooltip, Button, Checkbox, Input, ModalHeader, Select } from '../ui'
import { useOratorioConnectT } from './oratorio-connect-i18n'
import { useGitLabTokenKindOptions } from './oratorio-connect-parts'
import { useOratorioSettingsT } from './oratorio-settings-i18n'
import { GITLAB_TOKEN_KINDS, type GitHubAppSecretKey, type GitLabProjectSecretKey, type GitLabTokenKind, type OratorioProjectConfig, type ReviewListKey } from './oratorio-settings-model'
import { buildOratorioProjectDisplayOptions, projectValueMatchesOption, selectedOratorioProjectValue } from './oratorio-project-display'

export interface WorkspaceBindingOption {
  value: string
  label: string
  disabled?: boolean
}

export type SettingsDialog =
  | { kind: 'allowlist'; listKey: ReviewListKey }
  | { kind: 'appSecret'; secretKey: GitHubAppSecretKey; secretName: string }
  | { kind: 'projectSecret'; projectKey: string; secretKey: GitLabProjectSecretKey; secretName: string }
  | { kind: 'token'; projectKey: string }
  | { kind: 'installation'; owner: string }
  | null

export function AllowlistDialog({ listKey, values, projects, onClose, onApply }: {
  listKey: ReviewListKey
  values: string[]
  projects: OratorioProjectConfig[]
  onClose: () => void
  onApply: (values: string[]) => void
}) {
  const t = useOratorioSettingsT()
  const [draft, setDraft] = useState(values)
  const title = listKey === 'autoReview' ? t('automaticReview') : listKey === 'draftPublish' ? t('publishDrafts') : t('automaticFollowUp')
  const projectOptions = useMemo(() => buildOratorioProjectDisplayOptions(projects), [projects])
  return <DialogFrame ariaLabel={title} onClose={onClose}>
    <ModalHeader icon={<ShieldCheck size={18} />} title={title} description={t('selectProjects')} onClose={onClose} closeLabel={t('close')} />
    <div className="ora-dialog-list">{projectOptions.map((option) => <Checkbox key={option.projectId} checked={selectedOratorioProjectValue(draft, option) !== undefined} onChange={(checked) => setDraft((current) => checked
      ? selectedOratorioProjectValue(current, option) === undefined ? [...current, option.value] : current
      : current.filter((value) => !projectValueMatchesOption(value, option)))} label={<ActionTooltip label={option.tooltip} multiline><span>{option.label}</span></ActionTooltip>} ariaLabel={`${title}: ${option.tooltip}`} />)}</div>
    <DialogFooter onClose={onClose}><Button variant="primary" onClick={() => onApply(draft)}>{t('apply')}</Button></DialogFooter>
  </DialogFrame>
}

export function SecretDialog({ secretName, context, configured, onClose, onApply }: {
  secretName: string
  context: string
  configured: boolean
  onClose: () => void
  onApply: (action: 'replace' | 'clear', value: string | null) => void
}) {
  const t = useOratorioSettingsT()
  const [secret, setSecret] = useState('')
  return <DialogFrame ariaLabel={secretName} onClose={onClose}>
    <ModalHeader icon={<KeyRound size={18} />} title={secretName} description={context} onClose={onClose} closeLabel={t('close')} />
    <div className="oratorio-project-dialog__form">
      <Field label={configured ? t('newValue') : t('value')} hint={t('storedSecret')}><Input autoFocus type="password" autoComplete="off" value={secret} onChange={(event) => setSecret(event.target.value)} /></Field>
    </div>
    <DialogFooter onClose={onClose} start={configured ? <Button variant="danger" onClick={() => onApply('clear', null)}>{t('clearSecret')}</Button> : undefined}>
      <Button variant="primary" disabled={!secret.trim()} onClick={() => onApply('replace', secret)}>{configured ? t('replaceSecret') : t('save')}</Button>
    </DialogFooter>
  </DialogFrame>
}

export function TokenDialog({ projectKey, tokenKind, onClose, onApply }: {
  projectKey: string
  tokenKind: string
  onClose: () => void
  onApply: (tokenKind: GitLabTokenKind, token: string) => void
}) {
  const t = useOratorioSettingsT()
  const ct = useOratorioConnectT()
  const kinds = useGitLabTokenKindOptions()
  const [kind, setKind] = useState<GitLabTokenKind>(GITLAB_TOKEN_KINDS.includes(tokenKind as GitLabTokenKind) ? tokenKind as GitLabTokenKind : 'accessToken')
  const [token, setToken] = useState('')
  return <DialogFrame ariaLabel={t('accessToken')} onClose={onClose}>
    <ModalHeader icon={<KeyRound size={18} />} title={t('accessToken')} description={projectKey} onClose={onClose} closeLabel={t('close')} />
    <div className="oratorio-project-dialog__form">
      <Field label={ct('tokenKind')}><Select<GitLabTokenKind> ariaLabel={ct('tokenKind')} value={kind} options={kinds} onValueChange={setKind} /></Field>
      <Field label={ct('token')} hint={ct('tokenHint')}><Input autoFocus type="password" autoComplete="off" value={token} placeholder={ct('tokenPlaceholder')} onChange={(event) => setToken(event.target.value)} /></Field>
    </div>
    <DialogFooter onClose={onClose}><Button variant="primary" disabled={!token.trim()} onClick={() => onApply(kind, token)}>{t('save')}</Button></DialogFooter>
  </DialogFrame>
}

export function InstallationDialog({ owner, installationId, onClose, onApply }: {
  owner: string
  installationId: string
  onClose: () => void
  onApply: (installationId: string) => void
}) {
  const t = useOratorioSettingsT()
  const [value, setValue] = useState(installationId)
  const trimmed = value.trim()
  const valid = /^\d+$/.test(trimmed)
  return <DialogFrame ariaLabel={t('installation')} onClose={onClose}>
    <ModalHeader icon={<ShieldCheck size={18} />} title={t('installation')} description={owner} onClose={onClose} closeLabel={t('close')} />
    <div className="oratorio-project-dialog__form">
      <Field label={t('installationId')} hint={value && !valid ? undefined : t('installationIdHint')} error={value && !valid ? t('installationIdNumber') : undefined}><Input autoFocus mono inputMode="numeric" value={value} invalid={Boolean(value) && !valid} onChange={(event) => setValue(event.target.value)} /></Field>
    </div>
    <DialogFooter onClose={onClose}><Button variant="primary" disabled={!valid || trimmed === installationId} onClick={() => onApply(trimmed)}>{t('save')}</Button></DialogFooter>
  </DialogFrame>
}

function DialogFrame({ ariaLabel, onClose, children }: { ariaLabel: string; onClose: () => void; children: ReactNode }) {
  const panelRef = useRef<HTMLDivElement>(null)
  const returnFocusRef = useRef<HTMLElement | null>(document.activeElement as HTMLElement | null)
  useEffect(() => {
    const panel = panelRef.current
    if (!panel?.contains(document.activeElement)) panel?.querySelector<HTMLElement>('input, button, [role="combobox"], [tabindex]:not([tabindex="-1"])')?.focus()
    function keydown(event: KeyboardEvent): void {
      if (event.key === 'Escape') { event.preventDefault(); onClose(); return }
      if (event.key !== 'Tab' || !panel) return
      const nodes = Array.from(panel.querySelectorAll<HTMLElement>('input, button, [role="combobox"], [tabindex]:not([tabindex="-1"])')).filter((node) => !node.hasAttribute('disabled'))
      if (!nodes.length) return
      const first = nodes[0]; const last = nodes[nodes.length - 1]
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
      if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
    }
    document.addEventListener('keydown', keydown)
    return () => { document.removeEventListener('keydown', keydown); returnFocusRef.current?.focus() }
  }, [onClose])
  const dialog = <div role="dialog" aria-modal="true" aria-label={ariaLabel} style={overlayStyle} onMouseDown={(event) => { if (event.target === event.currentTarget) onClose() }}><div ref={panelRef} style={dialogStyle} onMouseDown={(event) => event.stopPropagation()}>{children}</div></div>
  return createPortal(dialog, document.body)
}

function DialogFooter({ onClose, start, children }: { onClose: () => void; start?: ReactNode; children: ReactNode }) {
  const t = useOratorioSettingsT()
  return <div style={footerStyle}>{start ? <span className="ora-dialog-footer__start">{start}</span> : null}<Button variant="secondary" onClick={onClose}>{t('cancel')}</Button>{children}</div>
}

function Field({ label, hint, error, children }: { label: string; hint?: string; error?: string; children: ReactNode }) {
  return <div className="ora-dialog-field"><label><span>{label}</span>{children}</label>{error ? <small className="ora-dialog-field__error" role="alert">{error}</small> : hint ? <small className="ora-dialog-field__hint">{hint}</small> : null}</div>
}

const overlayStyle: CSSProperties = { position: 'fixed', inset: 0, zIndex: 10000, display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--overlay-scrim)' }
const dialogStyle: CSSProperties = { width: 480, maxWidth: 'calc(100vw - 48px)', maxHeight: 'calc(100vh - 96px)', overflow: 'auto', padding: '20px 22px', borderRadius: 10, background: 'var(--bg-secondary)', boxShadow: 'var(--shadow-level-3)' }
const footerStyle: CSSProperties = { display: 'flex', justifyContent: 'flex-end', alignItems: 'center', gap: 8, marginTop: 22 }
