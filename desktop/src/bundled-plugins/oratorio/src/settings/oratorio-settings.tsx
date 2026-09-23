import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Plus, RefreshCw, X } from 'lucide-react'
import {
  ActionTooltip,
  Button,
  Input,
  PillSwitch,
  Select,
  SettingsBreadcrumb,
  SettingsGroup,
  SettingsPanelShell,
  SettingsRow,
  Skeleton
} from '../ui'
import { GithubGlyph, GitlabGlyph } from '../ProviderGlyphs'
import { DurationPicker, FieldControl, IntervalPicker, NumberStepper } from './oratorio-settings-controls'
import { AllowlistDialog, InstallationDialog, SecretDialog, TokenDialog, type SettingsDialog } from './oratorio-settings-dialogs'
import { useOratorioSettingsT } from './oratorio-settings-i18n'
import {
  githubInstallationForOwner,
  gitlabProfileForProject,
  sameProjectKey,
  validateEndpoint,
  withGitHubInstallation,
  withGitLabProfile,
  type ApprovalPolicy,
  type DeliveryPolicy,
  type GitHubAppSecretKey,
  type OratorioProjectConfig,
  type SourceProvider
} from './oratorio-settings-model'
import { buildOratorioProjectDisplayOptions, oratorioProjectDisplay } from './oratorio-project-display'
import { useSettingsController, type NotifySettingsError, type SettingsController, type SettingsLoadState } from './oratorio-settings-controller'
import { ProjectSettings, useWorkspaceBindings } from './oratorio-project-settings'
import { OratorioConnectSource } from './oratorio-connect-source'
import { useOratorioConnectT } from './oratorio-connect-i18n'
import { githubAppConfigured } from './oratorio-connect-model'
import { oratorioClient } from '../oratorio-client'
import { oratorioHost } from '../runtime'

export type OratorioSettingsView = 'root' | 'github' | 'gitlab' | 'project' | 'connect'
type ProjectRef = Pick<OratorioProjectConfig, 'provider' | 'projectKey'>

function useSettingsErrorToast(): NotifySettingsError {
  const t = useOratorioSettingsT()
  const activeToastRef = useRef<(() => void) | null>(null)
  return useCallback((message: string, retry?: () => void) => {
    activeToastRef.current?.()
    let dismiss = (): void => undefined
    dismiss = oratorioHost().ui.showToast({
      message,
      tone: 'error',
      ...(retry ? {
        action: {
          label: t('retry'),
          run: () => {
            dismiss()
            if (activeToastRef.current === dismiss) activeToastRef.current = null
            retry()
          }
        }
      } : {})
    })
    activeToastRef.current = dismiss
  }, [t])
}

export function OratorioSettingsPanel({ view = 'root', connectProvider = 'github', serviceError = false, readOnly = false, onViewChange, onConnect }: { view?: OratorioSettingsView; connectProvider?: SourceProvider; serviceError?: boolean; readOnly?: boolean; onViewChange: (view: OratorioSettingsView) => void; onConnect: (provider: SourceProvider) => void }): ReactNode {
  const t = useOratorioSettingsT()
  const notifyError = useSettingsErrorToast()
  const controller = useSettingsController(readOnly, notifyError)
  const workspaces = useWorkspaceBindings()
  const [dialog, setDialog] = useState<SettingsDialog>(null)
  const [selectedProject, setSelectedProject] = useState<ProjectRef | null>(null)
  const [syncingProjects, setSyncingProjects] = useState<ReadonlySet<string>>(new Set())
  const closeDialog = useCallback(() => setDialog(null), [])
  const project = selectedProject ? controller.draft.projects.find((item) => item.provider === selectedProject.provider && sameProjectKey(item.projectKey, selectedProject.projectKey)) : undefined
  const navigateProject = (target: ProjectRef): void => { setSelectedProject({ provider: target.provider, projectKey: target.projectKey }); onViewChange('project') }

  useEffect(() => {
    if (view === 'project' && controller.status === 'ready' && !project) onViewChange('root')
  }, [controller.status, onViewChange, project, view])

  const startProjectSync = (target: OratorioProjectConfig): void => {
    setSyncingProjects((current) => new Set(current).add(target.id))
    const settle = (): void => setSyncingProjects((current) => {
      const next = new Set(current)
      next.delete(target.id)
      return next
    })
    void oratorioClient.sync(target.provider, 'incremental', [target.projectKey]).then(settle, () => {
      settle()
      notifyError(t('syncFailed'), () => startProjectSync(target))
    })
  }

  const draft = controller.draft
  return <div className={`oratorio-native-settings${readOnly ? ' is-readonly' : ''}`} aria-disabled={readOnly}><div className="oratorio-native-settings__content">
      {serviceError ? <div className="oratorio-service-alert" role="alert">Oratorio is unavailable.</div> : null}
      {readOnly ? <div className="oratorio-service-alert" role="status">Remote Stack configuration is read-only. Source synchronization remains available.</div> : null}
      {controller.restartRequired ? <div className="oratorio-service-alert oratorio-service-alert--restart" role="status">{t('restartRequired')}</div> : null}
      {controller.status !== 'ready' ? <SettingsLoadState status={controller.status} onRetry={controller.reload} /> : null}
      {controller.status === 'ready' && view === 'root' ? <RootSettings controller={controller} syncingProjects={syncingProjects} onNavigate={onViewChange} onNavigateProject={navigateProject} onDialog={setDialog} onConnect={onConnect} /> : null}
      {controller.status === 'ready' && view === 'connect' ? <OratorioConnectSource controller={controller} provider={connectProvider} readOnly={readOnly} onExit={() => onViewChange('root')} onOpenBoard={() => oratorioHost().navigation.openMainView('board')} onConnected={setSelectedProject} /> : null}
      {controller.status === 'ready' && (view === 'github' || view === 'gitlab') ? <ProviderSettings provider={view} controller={controller} onBack={() => onViewChange('root')} onDialog={setDialog} onOperationError={notifyError} /> : null}
      {controller.status === 'ready' && view === 'project' && project ? <ProjectSettings project={project} workspaceOptions={workspaces.options} workspaceLoading={workspaces.loading} syncing={syncingProjects.has(project.id)} controller={controller} onSync={() => startProjectSync(project)} onBack={() => onViewChange('root')} onRemoved={() => onViewChange('root')} onDialog={setDialog} /> : null}
    </div>
    {dialog?.kind === 'allowlist' ? <AllowlistDialog listKey={dialog.listKey} values={draft[dialog.listKey]} projects={draft.projects} onClose={closeDialog} onApply={(values) => { controller.change(dialog.listKey, values); closeDialog() }} /> : null}
    {dialog?.kind === 'appSecret' ? <SecretDialog secretName={dialog.secretName} context="GitHub App" configured={draft.github.secrets[dialog.secretKey].configured} onClose={closeDialog} onApply={(mode, value) => {
      controller.change(`github.secrets.${dialog.secretKey}`, { configured: mode === 'replace', mode, value }); closeDialog()
    }} /> : null}
    {dialog?.kind === 'projectSecret' ? <SecretDialog secretName={dialog.secretName} context={dialog.projectKey} configured={gitlabProfileForProject(draft, dialog.projectKey)?.secrets[dialog.secretKey].configured ?? false} onClose={closeDialog} onApply={(mode, value) => {
      controller.change('gitlab.profiles', withGitLabProfile(controller.snapshot(), dialog.projectKey, (profile) => ({ ...profile, secrets: { ...profile.secrets, [dialog.secretKey]: { configured: mode === 'replace', mode, value } } }))); closeDialog()
    }} /> : null}
    {dialog?.kind === 'token' ? <TokenDialog projectKey={dialog.projectKey} tokenKind={gitlabProfileForProject(draft, dialog.projectKey)?.tokenKind ?? 'accessToken'} onClose={closeDialog} onApply={(tokenKind, token) => {
      controller.change('gitlab.profiles', withGitLabProfile(controller.snapshot(), dialog.projectKey, (profile) => ({ ...profile, tokenKind, secrets: { ...profile.secrets, token: { configured: true, mode: 'replace', value: token } } }))); closeDialog()
    }} /> : null}
    {dialog?.kind === 'installation' ? <InstallationDialog owner={dialog.owner} installationId={githubInstallationForOwner(draft, dialog.owner)?.installationId ?? ''} onClose={closeDialog} onApply={(installationId) => {
      controller.change('github.profiles', withGitHubInstallation(controller.snapshot(), dialog.owner, installationId)); closeDialog()
    }} /> : null}
  </div>
}

/** Until the server answers, the page has nothing true to show, so it never renders defaults that a save could write back. */
function SettingsLoadState({ status, onRetry }: { status: Exclude<SettingsLoadState, 'ready'>; onRetry: () => void }) {
  const t = useOratorioSettingsT()
  return <SettingsPanelShell title={t('oratorio')} description={t('rootDescription')}>
    <SettingsGroup>
      {status === 'failed'
        ? <SettingsRow label={t('loadFailed')} control={<Button variant="secondary" size="sm" iconLeft={<RefreshCw size={14} />} onClick={onRetry}>{t('retry')}</Button>} />
        : <div role="status" aria-busy="true" aria-label={t('loading')}>{['72%', '54%', '64%'].map((width) => <SettingsRow key={width}><Skeleton width={width} height={16} /></SettingsRow>)}</div>}
    </SettingsGroup>
  </SettingsPanelShell>
}

function RootSettings({ controller, syncingProjects, onNavigate, onNavigateProject, onDialog, onConnect }: { controller: SettingsController; syncingProjects: ReadonlySet<string>; onNavigate: (view: OratorioSettingsView) => void; onNavigateProject: (project: ProjectRef) => void; onDialog: (dialog: SettingsDialog) => void; onConnect: (provider: SourceProvider) => void }) {
  const t = useOratorioSettingsT(); const ct = useOratorioConnectT(); const c = controller.draft
  const projectCount = (provider: SourceProvider): string => {
    const count = c.projects.filter((item) => item.provider === provider).length
    return `${count} ${t(count === 1 ? 'project' : 'projects')}`
  }
  return <SettingsPanelShell title={t('oratorio')} description={t('rootDescription')} action={<Button variant="secondary" size="sm" iconLeft={<Plus size={14} />} onClick={() => onConnect('github')}>{ct('connectSource')}</Button>}>
    <SettingsGroup title={t('providers')} description={t('providersDescription')}>
      <SettingsRow label={<span className="ora-settings__label"><GithubGlyph size={15} />GitHub</span>} description={githubAppConfigured(c) ? `${ct('appConfiguredShort')} · ${projectCount('github')}` : projectCount('github')} control={<Button variant="secondary" size="sm" aria-label={`GitHub ${t('manage')}`} onClick={() => onNavigate('github')}>{t('manage')}</Button>} />
      <SettingsRow label={<span className="ora-settings__label"><GitlabGlyph size={15} />GitLab</span>} description={projectCount('gitlab')} control={<Button variant="secondary" size="sm" aria-label={`GitLab ${t('manage')}`} onClick={() => onNavigate('gitlab')}>{t('manage')}</Button>} />
    </SettingsGroup>
    <SettingsGroup title={t('projects')} description={t('projectsDescription')}>
      {c.projects.length === 0 ? <SettingsRow><span className="ora-settings__value">{ct('noProjectsYet')}</span></SettingsRow> : null}
      {c.projects.map((project) => <ProjectRow key={project.id} project={project} syncing={syncingProjects.has(project.id)} manageLabel={t('manage')} onManage={() => onNavigateProject(project)} onChange={(checked) => controller.change('projects', c.projects.map((item) => item.id === project.id ? { ...item, enabled: checked } : item))} />)}
    </SettingsGroup>
    <SettingsGroup title={t('agentExecution')} description={t('capturedForRun')}>
      <SettingsRow label={t('approvalPolicy')} description={t('approvalDescription')} control={<FieldControl><Select<ApprovalPolicy> ariaLabel={t('approvalPolicy')} value={c.approvalPolicy} onValueChange={(value) => controller.change('approvalPolicy', value)} options={[{ value: 'deny', label: t('approvalDeny') }, { value: 'autoApprove', label: t('approvalAuto') }]} /></FieldControl>} />
      <SettingsRow label={t('runTimeout')} description={t('runTimeoutDescription')} control={<FieldControl><DurationPicker valueSeconds={c.runTimeoutSeconds} minSeconds={30} maxSeconds={7200} label={t('runTimeout')} onChange={(value) => controller.change('runTimeoutSeconds', value)} /></FieldControl>} />
    </SettingsGroup>
    <SettingsGroup title={t('worktrees')} description={t('worktreesDescription')}>
      <SettingsRow label={t('managedWorktrees')} description={t('managedWorktreesDescription')} control={<FieldControl><PillSwitch checked={c.managedWorktreesEnabled} onChange={(value) => controller.change('managedWorktreesEnabled', value)} aria-label={t('managedWorktrees')} /></FieldControl>} />
      <SettingsRow label={t('worktreeRoot')} description={t('worktreeRootDescription')} control={<FieldControl><Input mono value={c.worktreeRoot} placeholder={t('repositoryDefault')} aria-label={t('worktreeRoot')} onChange={(event) => controller.change('worktreeRoot', event.target.value)} /></FieldControl>} />
      <SettingsRow label={t('branchPrefix')} description={t('branchPrefixDescription')} control={<FieldControl><Input mono value={c.worktreeBranchPrefix} aria-label={t('branchPrefix')} onChange={(event) => controller.change('worktreeBranchPrefix', event.target.value)} /></FieldControl>} />
    </SettingsGroup>
    <SettingsGroup title={t('dispatch')} description={t('dispatchDescription')}>
      <SettingsRow label={t('autoDispatch')} description={t('autoDispatchDescription')} control={<FieldControl><PillSwitch checked={c.autoDispatchEnabled} onChange={(value) => controller.change('autoDispatchEnabled', value)} aria-label={t('autoDispatch')} /></FieldControl>} />
      <SettingsRow label={t('allowedLabels')} description={t('allowedLabelsDescription')} control={<LabelList labels={c.allowedLabels} onChange={(value) => controller.change('allowedLabels', value)} />} />
      <SettingsRow label={t('blockedLabels')} description={t('blockedLabelsDescription')} control={<LabelList labels={c.blockedLabels} onChange={(value) => controller.change('blockedLabels', value)} />} />
      <SettingsRow label={t('implementationTurns')} description={t('implementationTurnsDescription')} control={<FieldControl><NumberStepper value={c.maxImplementationTurns} min={1} max={10} label={t('implementationTurns')} onChange={(value) => controller.change('maxImplementationTurns', value)} /></FieldControl>} />
      <SettingsRow label={t('deliveryPolicy')} description={t('deliveryPolicyDescription')} control={<FieldControl><Select<DeliveryPolicy> ariaLabel={t('deliveryPolicy')} value={c.deliveryPolicy} onValueChange={(value) => controller.change('deliveryPolicy', value)} options={[{ value: 'manualDelivery', label: t('manualDelivery') }, { value: 'autoPr', label: t('automaticPr') }]} /></FieldControl>} />
    </SettingsGroup>
    <SettingsGroup title={t('review')} description={t('reviewDescription')}>
      <AllowlistRow label={t('automaticReview')} description={t('automaticReviewDescription')} values={c.autoReview} projects={c.projects} onChange={(values) => controller.change('autoReview', values)} onManage={() => onDialog({ kind: 'allowlist', listKey: 'autoReview' })} />
      <AllowlistRow label={t('publishDrafts')} description={t('publishDraftsDescription')} values={c.draftPublish} projects={c.projects} onChange={(values) => controller.change('draftPublish', values)} onManage={() => onDialog({ kind: 'allowlist', listKey: 'draftPublish' })} />
      <AllowlistRow label={t('automaticFollowUp')} description={t('automaticFollowUpDescription')} values={c.followUp} projects={c.projects} onChange={(values) => controller.change('followUp', values)} onManage={() => onDialog({ kind: 'allowlist', listKey: 'followUp' })} />
      <SettingsRow label={t('maxFollowUp')} description={t('maxFollowUpDescription')} control={<FieldControl><NumberStepper value={c.maxFollowUpRounds} min={1} max={20} label={t('maxFollowUp')} onChange={(value) => controller.change('maxFollowUpRounds', value)} /></FieldControl>} />
    </SettingsGroup>
  </SettingsPanelShell>
}

function ProviderSettings({ provider, controller, onBack, onDialog, onOperationError }: { provider: SourceProvider; controller: SettingsController; onBack: () => void; onDialog: (dialog: SettingsDialog) => void; onOperationError: NotifySettingsError }) {
  const t = useOratorioSettingsT(); const github = provider === 'github'; const Icon = github ? GithubGlyph : GitlabGlyph; const name = github ? 'GitHub' : 'GitLab'; const config = controller.draft[provider]
  const providerTitle = <span className="ora-provider-page-title"><Icon size={18} />{name}</span>
  const [endpointDraft, setEndpointDraft] = useState(config.endpoint); const endpointError = validateEndpoint(endpointDraft)
  const [apiBaseDraft, setApiBaseDraft] = useState(controller.draft.gitlab.apiBaseUrl); const apiBaseError = validateEndpoint(apiBaseDraft)
  const [sync, setSync] = useState<'idle' | 'busy'>('idle')
  useEffect(() => setEndpointDraft(config.endpoint), [config.endpoint])
  useEffect(() => setApiBaseDraft(controller.draft.gitlab.apiBaseUrl), [controller.draft.gitlab.apiBaseUrl])
  function startSync(): void {
    setSync('busy')
    void oratorioClient.sync(provider).then(() => setSync('idle')).catch(() => {
      setSync('idle')
      onOperationError(t('syncFailed'), startSync)
    })
  }
  const readable = github || controller.draft.gitlab.enabled
  const appSecrets: Array<{ key: GitHubAppSecretKey; label: string }> = [{ key: 'privateKey', label: t('privateKey') }, { key: 'privateKeyPath', label: t('privateKeyPath') }, { key: 'webhookSecret', label: t('webhookSecret') }]
  const installations = controller.draft.github.profiles
  return <SettingsPanelShell title={name} description={github ? t('githubProviderDescription') : t('gitlabProviderDescription')} breadcrumb={<SettingsBreadcrumb parentLabel={t('oratorio')} currentLabel={name} onBack={onBack} />} action={<span className="ora-sync-action"><Button variant="secondary" size="sm" disabled={!readable} loading={sync === 'busy'} iconLeft={<RefreshCw size={14} />} onClick={startSync}>{t('syncNow')}</Button></span>}>
    <SettingsGroup title={providerTitle as unknown as string}>
      <SettingsRow label={t('endpoint')} description={t('endpointDescription')} control={<FieldControl><span className="ora-validated-field"><Input mono value={endpointDraft} invalid={Boolean(endpointError)} aria-label={`${name} ${t('endpoint')}`} onChange={(event) => setEndpointDraft(event.target.value)} onBlur={() => { if (!endpointError) controller.change(`${provider}.endpoint`, endpointDraft) }} />{endpointError ? <small role="alert">{t('endpointInvalid')}</small> : null}</span></FieldControl>} controlMinWidth={260} />
      {github ? <SettingsRow label="App ID" control={<FieldControl><Input mono value={controller.draft.github.appId} aria-label="GitHub App ID" onChange={(event) => controller.change('github.appId', event.target.value)} /></FieldControl>} /> : null}
      {!github ? <><SettingsRow label={t('sourceReads')} control={<FieldControl><PillSwitch checked={controller.draft.gitlab.enabled} onChange={(value) => controller.change('gitlab.enabled', value)} aria-label={`${name} ${t('sourceReads')}`} /></FieldControl>} /><SettingsRow label={t('apiBaseUrl')} control={<FieldControl><span className="ora-validated-field"><Input mono value={apiBaseDraft} invalid={Boolean(apiBaseError)} aria-label={`${name} ${t('apiBaseUrl')}`} onChange={(event) => setApiBaseDraft(event.target.value)} onBlur={() => { if (!apiBaseError) controller.change('gitlab.apiBaseUrl', apiBaseDraft) }} />{apiBaseError ? <small role="alert">{t('endpointInvalid')}</small> : null}</span></FieldControl>} /></> : null}
      <SettingsRow label={t('sourceWrites')} control={<FieldControl><PillSwitch checked={config.writesEnabled} onChange={(value) => controller.change(`${provider}.writesEnabled`, value)} aria-label={`${name} ${t('sourceWrites')}`} /></FieldControl>} />
      {github ? appSecrets.map((secret) => {
        const configured = controller.draft.github.secrets[secret.key].configured
        const action = configured ? t('manage') : t('set')
        return <SettingsRow key={secret.key} label={secret.label} description={configured ? t('configured') : t('notSet')} control={<FieldControl><Button variant="secondary" size="sm" aria-label={`${name} ${secret.label} ${action}`} onClick={() => onDialog({ kind: 'appSecret', secretKey: secret.key, secretName: secret.label })}>{action}</Button></FieldControl>} />
      }) : null}
    </SettingsGroup>
    {github && installations.length > 0 ? <SettingsGroup title={t('installations')} description={t('installationsDescription')}>
      {installations.map((installation) => <SettingsRow key={installation.id} label={installation.owner} description={`${installation.installationId} · ${installation.source === 'detected' ? t('detected') : t('manual')}`} control={<FieldControl><Button variant="secondary" size="sm" aria-label={`${installation.owner} ${t('installation')} ${t('manage')}`} onClick={() => onDialog({ kind: 'installation', owner: installation.owner })}>{t('manage')}</Button></FieldControl>} />)}
    </SettingsGroup> : null}
    <SettingsGroup title={t('sourceSync')}><SettingsRow label={t('schedule')} control={<FieldControl><IntervalPicker disabled={!readable} label={`${name} ${t('schedule')}`} valueSeconds={config.syncIntervalSeconds} onChange={(value) => controller.change(`${provider}.syncIntervalSeconds`, value)} /></FieldControl>} /></SettingsGroup>
  </SettingsPanelShell>
}

function ProjectRow({ project, syncing, manageLabel, onManage, onChange }: { project: OratorioProjectConfig; syncing: boolean; manageLabel: string; onManage: () => void; onChange: (checked: boolean) => void }) {
  const t = useOratorioSettingsT(); const Icon = project.provider === 'github' ? GithubGlyph : GitlabGlyph
  const description = [project.provider === 'github' ? 'GitHub' : 'GitLab', project.workspacePath || t('workspaceNotBound'), ...(syncing ? [t('syncing')] : [])].join(' · ')
  return <SettingsRow label={<span className="ora-settings__label"><Icon size={15} />{project.projectKey}</span>} description={description} control={<span className="oratorio-native-settings__row-control"><Button variant="secondary" size="sm" aria-label={`${project.projectKey} ${manageLabel}`} onClick={onManage}>{manageLabel}</Button><PillSwitch checked={project.enabled} onChange={onChange} aria-label={`${t('enable')} ${project.projectKey}`} /></span>} />
}

function LabelList({ labels, onChange }: { labels: string[]; onChange: (labels: string[]) => void }) {
  const t = useOratorioSettingsT(); const [draft, setDraft] = useState(''); const [editing, setEditing] = useState(false); const [error, setError] = useState(false)
  function cancel(): void { setDraft(''); setEditing(false); setError(false) }
  function commit(): void {
    const value = draft.trim()
    if (!value) { cancel(); return }
    if (labels.some((label) => label.toLocaleLowerCase() === value.toLocaleLowerCase())) { setError(true); return }
    onChange([...labels, value]); cancel()
  }
  return <FieldControl><div className="ora-label-editor"><div className="ora-label-editor__pills">{labels.map((label) => <span className="ora-settings-label" key={label}><span>{label}</span><button type="button" aria-label={`${t('remove')} ${label}`} onClick={() => onChange(labels.filter((value) => value !== label))}><X size={12} /></button></span>)}{editing ? <span className="ora-label-editor__input" data-invalid={error ? 'true' : undefined}><Input bare autoFocus value={draft} onChange={(event) => { setDraft(event.target.value); setError(false) }} placeholder={t('addLabel')} aria-label={t('addLabel')} onBlur={commit} onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); commit() } else if (event.key === 'Escape') { event.preventDefault(); cancel() } }} /></span> : <button type="button" className="ora-label-editor__add" onClick={() => { setEditing(true); setError(false) }}><Plus size={12} aria-hidden="true" />{t('addLabel')}</button>}</div>{error ? <small role="alert">{t('duplicateLabel')}</small> : null}</div></FieldControl>
}
function AllowlistRow({ label, description, values, projects, onChange, onManage }: { label: string; description: string; values: string[]; projects: OratorioProjectConfig[]; onChange: (values: string[]) => void; onManage: () => void }) {
  const t = useOratorioSettingsT()
  const displayOptions = useMemo(() => buildOratorioProjectDisplayOptions(projects), [projects])
  return <SettingsRow label={label} description={description} control={<FieldControl><div className="ora-allowlist-row"><div>{values.map((value) => {
    const display = oratorioProjectDisplay(value, displayOptions)
    return <ActionTooltip key={value} label={display.tooltip} multiline><span className="ora-settings-label"><span>{display.label}</span><button type="button" aria-label={`${t('remove')} ${display.tooltip}`} onClick={() => onChange(values.filter((item) => item !== value))}><X size={12} /></button></span></ActionTooltip>
  })}</div><Button variant="secondary" size="sm" aria-label={`${label} ${t('manage')}`} onClick={onManage}>{t('manage')}</Button></div></FieldControl>} />
}
