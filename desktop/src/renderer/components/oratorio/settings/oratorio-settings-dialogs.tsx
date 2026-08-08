import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { FolderGit2, KeyRound, Plus, ShieldCheck } from 'lucide-react'
import { LayerBoundary } from '../../../contexts/LayerContext'
import { ModalHeader } from '../../ui/ModalHeader'
import { Button } from '../../ui/Button'
import { Checkbox } from '../../ui/Checkbox'
import { Input } from '../../ui/Input'
import { Select } from '../../ui/Select'
import { GithubGlyph, GitlabGlyph } from '../ProviderGlyphs'
import { useOratorioSettingsT } from './oratorio-settings-i18n'
import { normalizeProjectKey, projectKeyIsValid, type GitHubInstallationProfile, type GitLabProjectProfile, type OratorioProjectConfig, type ReviewListKey, type SourceProvider } from './oratorio-settings-model'

export interface WorkspaceBindingOption {
  value: string
  label: string
  disabled?: boolean
}

export function AddProjectDialog({ existing, profileOptions, workspaceOptions, workspaceLoading, providerInstances, onClose, onSubmit }: {
  existing: OratorioProjectConfig[]
  profileOptions: Record<SourceProvider, Array<{ value: string; label: string }>>
  workspaceOptions: WorkspaceBindingOption[]
  workspaceLoading: boolean
  providerInstances: Record<SourceProvider, string>
  onClose: () => void
  onSubmit: (project: OratorioProjectConfig, profile: GitHubInstallationProfile | GitLabProjectProfile | null) => void
}) {
  const t = useOratorioSettingsT()
  const [step, setStep] = useState(0)
  const [provider, setProvider] = useState<SourceProvider>('github')
  const [project, setProject] = useState('')
  const [workspace, setWorkspace] = useState(workspaceOptions[0]?.value ?? '')
  const [profileId, setProfileId] = useState(profileOptions.github[0]?.value ?? '')
  const [githubProfile, setGithubProfile] = useState<GitHubInstallationProfile>({ id: 'github-new-project', instance: providerInstances.github, owner: '', installationId: '', source: 'manual' })
  const [gitlabProfile, setGitlabProfile] = useState<GitLabProjectProfile>({ id: 'gitlab-new-project', instance: providerInstances.gitlab, projectPath: '', tokenKind: 'accessToken', secrets: { token: { configured: false, mode: 'unchanged', value: null }, webhookSecret: { configured: false, mode: 'unchanged', value: null }, webhookSigningToken: { configured: false, mode: 'unchanged', value: null } } })
  const normalized = normalizeProjectKey(project)
  const valid = projectKeyIsValid(project) && !existing.some((item) => item.provider === provider && item.projectKey.toLocaleLowerCase() === normalized.toLocaleLowerCase())
  const needsProfile = profileOptions[provider].length === 0
  const inlineGitlabProjectPath = gitlabProfile.projectPath || normalized
  const inlineProfile = provider === 'github' ? githubProfile : { ...gitlabProfile, projectPath: inlineGitlabProjectPath }
  const inlineProfileValid = provider === 'github'
    ? Boolean(githubProfile.instance.trim() && githubProfile.owner.trim() && githubProfile.installationId.trim())
    : Boolean(gitlabProfile.instance.trim() && projectKeyIsValid(inlineGitlabProjectPath) && gitlabProfile.tokenKind.trim())

  useEffect(() => {
    if (workspaceOptions.some((option) => option.value === workspace)) return
    setWorkspace(workspaceOptions[0]?.value ?? '')
  }, [workspace, workspaceOptions])

  function submit(): void {
    const idBase = `${provider}-${normalized.toLocaleLowerCase().replace(/[^a-z0-9]+/g, '-')}`
    let id = idBase; let suffix = 2
    while (existing.some((item) => item.id === id)) { id = `${idBase}-${suffix}`; suffix += 1 }
    const resolvedProfile = needsProfile ? { ...inlineProfile, id: `${provider}-${normalized.toLocaleLowerCase().replace(/[^a-z0-9]+/g, '-')}-profile` } : null
    onSubmit({
      id,
      provider,
      projectKey: normalized,
      workspacePath: workspace,
      profileId: resolvedProfile?.id ?? profileId,
      enabled: true,
    }, resolvedProfile)
  }

  return <DialogFrame ariaLabel={t('addProject')} onClose={onClose}>
    <ModalHeader icon={<FolderGit2 size={18} />} title={t('addProject')} description={step === 0 ? t('providersDescription') : step === 1 ? t('projectsDescription') : t('capturedForRun')} onClose={onClose} closeLabel={t('close')} />
    <div className="ora-dialog-progress" role="progressbar" aria-label={`${step + 1} / 3`} aria-valuemin={1} aria-valuemax={3} aria-valuenow={step + 1}><span data-active={step >= 0} /><span data-active={step >= 1} /><span data-active={step >= 2} /></div>
    <div className="oratorio-project-dialog__form">
      {step === 0 ? <Field label={t('providers')}><Select<SourceProvider> ariaLabel={t('providers')} value={provider} onValueChange={(value) => { setProvider(value); setProfileId(profileOptions[value][0]?.value ?? '') }} options={[{ value: 'github', label: <span className="ora-settings__label"><GithubGlyph />GitHub</span> }, { value: 'gitlab', label: <span className="ora-settings__label"><GitlabGlyph />GitLab</span> }]} /></Field> : null}
      {step === 1 ? <><Field label={t('project')}><Input autoFocus value={project} placeholder={provider === 'github' ? 'owner/repository' : 'group/project'} invalid={project.length > 0 && !valid} mono onChange={(event) => setProject(event.target.value)} aria-label={t('project')} /></Field>{!needsProfile ? <Field label={t('profile')}><Select ariaLabel={t('profile')} value={profileId} onValueChange={setProfileId} options={profileOptions[provider]} /></Field> : <div className="ora-provider-setup"><strong>{t('configureProvider')}</strong><Field label={t('instance')}><Input mono value={inlineProfile.instance} onChange={(event) => provider === 'github' ? setGithubProfile({ ...githubProfile, instance: event.target.value }) : setGitlabProfile({ ...gitlabProfile, instance: event.target.value })} /></Field>{provider === 'github' ? <><Field label={t('owner')}><Input mono value={githubProfile.owner} onChange={(event) => setGithubProfile({ ...githubProfile, owner: event.target.value })} /></Field><Field label={t('installationId')}><Input mono value={githubProfile.installationId} onChange={(event) => setGithubProfile({ ...githubProfile, installationId: event.target.value })} /></Field></> : <><Field label={t('project')}><Input mono value={gitlabProfile.projectPath || normalized} onChange={(event) => setGitlabProfile({ ...gitlabProfile, projectPath: event.target.value })} /></Field><Field label={t('tokenKind')}><Input mono value={gitlabProfile.tokenKind} onChange={(event) => setGitlabProfile({ ...gitlabProfile, tokenKind: event.target.value })} /></Field></>}</div>}</> : null}
      {step === 2 ? <Field label={t('workspace')}>{workspaceLoading ? <div className="ora-dialog-workspace-state" role="status">{t('workspaceLoading')}</div> : workspaceOptions.length > 0 ? <Select ariaLabel={t('workspace')} value={workspace} onValueChange={setWorkspace} options={workspaceOptions} /> : <div className="ora-dialog-workspace-state" role="status">{t('workspaceEmpty')}</div>}</Field> : null}
    </div>
    <DialogFooter onClose={onClose} showCancel={false}>
      {step > 0 ? <Button variant="secondary" onClick={() => setStep((value) => value - 1)}>{t('back')}</Button> : null}
      {step < 2 ? <Button variant="primary" disabled={step === 1 && (!valid || (needsProfile ? !inlineProfileValid : !profileId))} onClick={() => setStep((value) => value + 1)}>{t('next')}</Button> : <Button variant="primary" iconLeft={<Plus size={14} />} disabled={workspaceLoading || !valid || !workspace || (needsProfile ? !inlineProfileValid : !profileId)} onClick={submit}>{t('addProject')}</Button>}
    </DialogFooter>
  </DialogFrame>
}

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
  return <DialogFrame ariaLabel={title} onClose={onClose}>
    <ModalHeader icon={<ShieldCheck size={18} />} title={title} description={t('selectProjects')} onClose={onClose} closeLabel={t('close')} />
    <div className="ora-dialog-list">{projects.map((project) => <Checkbox key={project.id} checked={draft.includes(project.projectKey)} onChange={(checked) => setDraft((current) => checked ? [...current, project.projectKey] : current.filter((item) => item !== project.projectKey))} label={project.projectKey} />)}</div>
    <DialogFooter onClose={onClose}><Button variant="primary" onClick={() => onApply(draft)}>{t('apply')}</Button></DialogFooter>
  </DialogFrame>
}

export function SecretDialog({ providerName, secretName, onClose, onApply }: {
  providerName: string
  secretName: string
  onClose: () => void
  onApply: (action: 'replace' | 'clear', value: string | null) => void
}) {
  const t = useOratorioSettingsT()
  const [mode, setMode] = useState<'replace' | 'clear'>('replace')
  const [secret, setSecret] = useState('')
  const title = `${providerName} · ${secretName}`
  return <DialogFrame ariaLabel={title} onClose={onClose}>
    <ModalHeader icon={<KeyRound size={18} />} title={title} description={t('storedSecret')} onClose={onClose} closeLabel={t('close')} />
    <div className="oratorio-project-dialog__form">
      <Select ariaLabel={title} value={mode} onValueChange={setMode} options={[{ value: 'replace', label: t('replaceSecret') }, { value: 'clear', label: t('clearSecret') }]} />
      {mode === 'replace' ? <Input autoFocus type="password" value={secret} onChange={(event) => setSecret(event.target.value)} aria-label={title} /> : null}
    </div>
    <DialogFooter onClose={onClose}><Button variant={mode === 'clear' ? 'danger' : 'primary'} disabled={mode === 'replace' && !secret.trim()} onClick={() => onApply(mode, mode === 'replace' ? secret : null)}>{mode === 'clear' ? t('clearSecret') : t('replaceSecret')}</Button></DialogFooter>
  </DialogFrame>
}

export function ProfileDialog({ provider, value, onClose, onApply }: { provider: SourceProvider; value: GitHubInstallationProfile | GitLabProjectProfile; onClose: () => void; onApply: (value: GitHubInstallationProfile | GitLabProjectProfile) => void }) {
  const t = useOratorioSettingsT()
  const [draft, setDraft] = useState(value)
  const github = provider === 'github'; const providerName = github ? 'GitHub' : 'GitLab'
  const valid = github ? Boolean((draft as GitHubInstallationProfile).instance.trim() && (draft as GitHubInstallationProfile).owner.trim() && (draft as GitHubInstallationProfile).installationId.trim()) : Boolean((draft as GitLabProjectProfile).instance.trim() && projectKeyIsValid((draft as GitLabProjectProfile).projectPath) && (draft as GitLabProjectProfile).tokenKind.trim())
  return <DialogFrame ariaLabel={`${providerName} ${t('manage')}`} onClose={onClose}>
    <ModalHeader icon={<ShieldCheck size={18} />} title={`${providerName} ${t('manage')}`} description={t('providersDescription')} onClose={onClose} closeLabel={t('close')} />
    <div className="oratorio-project-dialog__form">
      <Field label={t('instance')}><Input autoFocus mono value={draft.instance} onChange={(event) => setDraft({ ...draft, instance: event.target.value })} /></Field>
      {github ? <><Field label={t('owner')}><Input mono value={(draft as GitHubInstallationProfile).owner} onChange={(event) => setDraft({ ...(draft as GitHubInstallationProfile), owner: event.target.value })} /></Field><Field label={t('installationId')}><Input mono value={(draft as GitHubInstallationProfile).installationId} onChange={(event) => setDraft({ ...(draft as GitHubInstallationProfile), installationId: event.target.value })} /></Field><Field label={t('source')}><Select ariaLabel={t('source')} value={(draft as GitHubInstallationProfile).source} onValueChange={(source) => setDraft({ ...(draft as GitHubInstallationProfile), source })} options={[{ value: 'manual', label: t('manual') }, { value: 'detected', label: t('detected') }]} /></Field></> : <><Field label={t('project')}><Input mono value={(draft as GitLabProjectProfile).projectPath} onChange={(event) => setDraft({ ...(draft as GitLabProjectProfile), projectPath: event.target.value })} /></Field><Field label={t('tokenKind')}><Input mono value={(draft as GitLabProjectProfile).tokenKind} onChange={(event) => setDraft({ ...(draft as GitLabProjectProfile), tokenKind: event.target.value })} /></Field></>}
    </div>
    <DialogFooter onClose={onClose}><Button variant="primary" disabled={!valid} onClick={() => onApply(draft)}>{t('apply')}</Button></DialogFooter>
  </DialogFrame>
}

function DialogFrame({ ariaLabel, onClose, children }: { ariaLabel: string; onClose: () => void; children: ReactNode }) {
  const panelRef = useRef<HTMLDivElement>(null)
  const returnFocusRef = useRef<HTMLElement | null>(document.activeElement as HTMLElement | null)
  useEffect(() => {
    const panel = panelRef.current
    const focusable = panel?.querySelector<HTMLElement>('input, button, [role="combobox"], [tabindex]:not([tabindex="-1"])')
    focusable?.focus()
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
  return createPortal(<LayerBoundary>{dialog}</LayerBoundary>, document.body)
}

function DialogFooter({ onClose, children, showCancel = true }: { onClose: () => void; children: ReactNode; showCancel?: boolean }) {
  const t = useOratorioSettingsT()
  return <div style={footerStyle}>{showCancel ? <Button variant="secondary" onClick={onClose}>{t('cancel')}</Button> : null}{children}</div>
}
function Field({ label, children }: { label: string; children: ReactNode }) { return <label><span>{label}</span>{children}</label> }

const overlayStyle: CSSProperties = { position: 'fixed', inset: 0, zIndex: 10000, display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--overlay-scrim)' }
const dialogStyle: CSSProperties = { width: 480, maxWidth: 'calc(100vw - 48px)', maxHeight: 'calc(100vh - 96px)', overflow: 'auto', padding: '20px 22px', borderRadius: 10, background: 'var(--bg-secondary)', boxShadow: 'var(--shadow-level-3)' }
const footerStyle: CSSProperties = { display: 'flex', justifyContent: 'flex-end', alignItems: 'center', gap: 8, marginTop: 22 }
