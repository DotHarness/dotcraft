import { Fragment, useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Plus, RefreshCw, Trash2, X } from 'lucide-react'
import {
  ActionTooltip,
  Button,
  Input,
  PillSwitch,
  Select,
  SettingsBreadcrumb,
  SettingsGroup,
  SettingsPanelShell,
  SettingsRow
} from '../ui'
import { GithubGlyph, GitlabGlyph } from '../ProviderGlyphs'
import { DurationPicker, FieldControl, IntervalPicker, NumberStepper } from './oratorio-settings-controls'
import { AddProjectDialog, AllowlistDialog, ProfileDialog, SecretDialog, type WorkspaceBindingOption } from './oratorio-settings-dialogs'
import { useOratorioSettingsT } from './oratorio-settings-i18n'
import { cloneSettings, createDefaultOratorioSettings, validateEndpoint, type ApprovalPolicy, type DeliveryPolicy, type GitHubInstallationProfile, type GitLabProjectProfile, type OratorioProjectConfig, type OratorioSettingsConfig, type ReviewListKey, type SourceProvider } from './oratorio-settings-model'
import { buildOratorioProjectDisplayOptions, oratorioProjectDisplay, projectValueMatchesOption } from './oratorio-project-display'
import { loadOratorioSettings, saveOratorioSettings, saveOratorioSyncSchedule } from './oratorio-settings-service'
import { OratorioConnectSource } from './oratorio-connect-source'
import { useOratorioConnectT } from './oratorio-connect-i18n'
import { githubAppConfigured } from './oratorio-connect-model'
import { oratorioClient } from '../oratorio-client'
import { oratorioHost } from '../runtime'

export type OratorioSettingsView = 'root' | 'github' | 'gitlab' | 'project' | 'connect'
type DialogState = { kind: 'add-project' } | { kind: 'allowlist'; listKey: ReviewListKey } | { kind: 'secret'; provider: SourceProvider; profileId?: string; secretKey: string; secretName: string } | { kind: 'profile'; provider: SourceProvider; profileId?: string } | null
type ProjectSyncState = 'idle' | 'queued' | 'syncing'
type NotifySettingsError = (message: string, retry?: () => void) => void

function providerInstance(provider: SourceProvider, endpoint: string): string {
  try {
    const hostname = new URL(endpoint).hostname.toLowerCase()
    return provider === 'github' && hostname === 'api.github.com' ? 'github.com' : hostname
  } catch {
    return ''
  }
}

function useWorkspaceBindings(): { options: WorkspaceBindingOption[]; loading: boolean } {
  const [projects, setProjects] = useState<readonly { path: string; active: boolean }[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let active = true
    void oratorioHost().workspaces.listLocalProjects()
      .then((payload) => { if (active) setProjects(payload) })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => { active = false }
  }, [])

  const options = useMemo(() => {
    const foregroundIndex = projects.findIndex((project) => project.active)
    const ordered = foregroundIndex > 0
      ? [projects[foregroundIndex], ...projects.slice(0, foregroundIndex), ...projects.slice(foregroundIndex + 1)]
      : projects
    return ordered.map((project) => ({ value: project.path, label: project.path }))
  }, [projects])

  return { options, loading }
}

function readPath(config: OratorioSettingsConfig, path: string): unknown {
  if (path === 'configuration') return config
  return path.split('.').reduce<unknown>((value, key) => (value as Record<string, unknown>)[key], config)
}
function writePath(config: OratorioSettingsConfig, path: string, value: unknown): OratorioSettingsConfig {
  if (path === 'configuration') return cloneSettings(value as OratorioSettingsConfig)
  const next = cloneSettings(config)
  const keys = path.split('.')
  let target = next as unknown as Record<string, unknown>
  for (const key of keys.slice(0, -1)) target = target[key] as Record<string, unknown>
  target[keys[keys.length - 1]] = structuredClone(value)
  return next
}

function normalizeConfirmedValue(path: string, value: unknown): unknown {
  if (!path.includes('.secrets.') || !value || typeof value !== 'object') return value
  const mode = (value as { mode?: string }).mode
  return { configured: mode === 'replace', mode: 'unchanged', value: null }
}

function newProfile(provider: SourceProvider, index: number, endpoint: string): GitHubInstallationProfile | GitLabProjectProfile {
  const instance = providerInstance(provider, endpoint)
  return provider === 'github'
    ? { id: `github-profile-${index}`, instance, owner: '', installationId: '', source: 'manual' }
    : { id: `gitlab-profile-${index}`, instance, projectPath: '', tokenKind: 'accessToken', secrets: { token: { configured: false, mode: 'unchanged', value: null }, webhookSecret: { configured: false, mode: 'unchanged', value: null }, webhookSigningToken: { configured: false, mode: 'unchanged', value: null } } }
}

function withoutProject(config: OratorioSettingsConfig, projectId: string): OratorioSettingsConfig {
  const transaction = cloneSettings(config)
  const option = buildOratorioProjectDisplayOptions(transaction.projects).find((item) => item.projectId === projectId)
  transaction.projects = transaction.projects.filter((item) => item.id !== projectId)
  if (!option) return transaction
  transaction.autoReview = transaction.autoReview.filter((value) => !projectValueMatchesOption(value, option))
  transaction.draftPublish = transaction.draftPublish.filter((value) => !projectValueMatchesOption(value, option))
  transaction.followUp = transaction.followUp.filter((value) => !projectValueMatchesOption(value, option))
  return transaction
}

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

function useSettingsController(readOnly: boolean, notifyError: NotifySettingsError) {
  const t = useOratorioSettingsT()
  const [draft, setDraft] = useState(createDefaultOratorioSettings)
  const [restartRequired, setRestartRequired] = useState(false)
  const draftRef = useRef(draft)
  const confirmedRef = useRef(cloneSettings(draft))
  const timersRef = useRef(new Map<string, number>())
  const versionsRef = useRef(new Map<string, number>())
  const serverConfigRef = useRef<Record<string, unknown>>({})
  const commitQueueRef = useRef(Promise.resolve())
  useEffect(() => { draftRef.current = draft }, [draft])
  useEffect(() => () => timersRef.current.forEach((timer) => window.clearTimeout(timer)), [])
  useEffect(() => {
    let active = true
    void loadOratorioSettings().then((loaded) => {
      if (!active) return
      confirmedRef.current = cloneSettings(loaded.settings)
      draftRef.current = cloneSettings(loaded.settings)
      serverConfigRef.current = loaded.serverConfiguration
      setDraft(loaded.settings)
      setRestartRequired(loaded.restartRequired)
    }).catch(() => {
      if (active) notifyError(t('loadFailed'))
    })
    return () => { active = false }
  }, [notifyError, t])

  const change = useCallback((path: string, value: unknown, onSaved?: () => void) => {
    if (readOnly) return
    const next = writePath(draftRef.current, path, value)
    draftRef.current = next
    setDraft(next)
    const oldTimer = timersRef.current.get(path)
    if (oldTimer) window.clearTimeout(oldTimer)
    const version = (versionsRef.current.get(path) ?? 0) + 1
    versionsRef.current.set(path, version)
    const timer = window.setTimeout(() => {
      timersRef.current.delete(path)
      if (versionsRef.current.get(path) !== version) return
      commitQueueRef.current = commitQueueRef.current.then(async () => {
        if (versionsRef.current.get(path) !== version) return
        try {
          const normalizedValue = normalizeConfirmedValue(path, value)
          let confirmed: OratorioSettingsConfig
          if (path === 'github.syncIntervalSeconds' || path === 'gitlab.syncIntervalSeconds') {
            const provider = path.startsWith('github.') ? 'github' : 'gitlab'
            await saveOratorioSyncSchedule(provider, value as number | null)
            confirmed = writePath(confirmedRef.current, path, normalizedValue)
          } else {
            const loaded = await saveOratorioSettings(draftRef.current, serverConfigRef.current)
            loaded.settings.github.syncIntervalSeconds = confirmedRef.current.github.syncIntervalSeconds
            loaded.settings.gitlab.syncIntervalSeconds = confirmedRef.current.gitlab.syncIntervalSeconds
            confirmed = writePath(loaded.settings, path, normalizedValue)
            confirmed.revision = loaded.settings.revision
            serverConfigRef.current = loaded.serverConfiguration
            setRestartRequired(loaded.restartRequired)
          }
          confirmedRef.current = confirmed
          const nextDraft = writePath(draftRef.current, path, normalizedValue)
          nextDraft.revision = confirmed.revision
          draftRef.current = nextDraft
          setDraft(nextDraft)
          onSaved?.()
        } catch {
          try {
            const latest = await loadOratorioSettings()
            confirmedRef.current = cloneSettings(latest.settings)
            serverConfigRef.current = latest.serverConfiguration
            setRestartRequired(latest.restartRequired)
          } catch {
            // Preserve the last confirmed snapshot when the service is unavailable.
          }
          const restored = writePath(draftRef.current, path, readPath(confirmedRef.current, path))
          restored.revision = confirmedRef.current.revision
          draftRef.current = restored
          setDraft(restored)
          notifyError(t('saveFailed'), () => change(path, value, onSaved))
        }
      })
    }, 500)
    timersRef.current.set(path, timer)
  }, [notifyError, t])

  // Undebounced save for the connect wizard; the server answer becomes the confirmed draft.
  const commit = useCallback(async (settings: OratorioSettingsConfig, options: { detectGitHubInstallations: boolean; schedule: { provider: SourceProvider; intervalSeconds: number | null } }) => {
    if (readOnly) throw new Error('oratorio.settings.read_only')
    const loaded = await saveOratorioSettings(settings, serverConfigRef.current, { detectGitHubInstallations: options.detectGitHubInstallations })
    await saveOratorioSyncSchedule(options.schedule.provider, options.schedule.intervalSeconds)
    loaded.settings.github.syncIntervalSeconds = options.schedule.provider === 'github' ? options.schedule.intervalSeconds : confirmedRef.current.github.syncIntervalSeconds
    loaded.settings.gitlab.syncIntervalSeconds = options.schedule.provider === 'gitlab' ? options.schedule.intervalSeconds : confirmedRef.current.gitlab.syncIntervalSeconds
    confirmedRef.current = cloneSettings(loaded.settings)
    draftRef.current = cloneSettings(loaded.settings)
    serverConfigRef.current = loaded.serverConfiguration
    setDraft(loaded.settings)
    setRestartRequired(loaded.restartRequired)
    return loaded
  }, [readOnly])

  return { draft, restartRequired, change, commit, snapshot: () => draftRef.current }
}

export function OratorioSettingsPanel({ view = 'root', connectProvider = 'github', serviceError = false, readOnly = false, onViewChange, onConnect }: { view?: OratorioSettingsView; connectProvider?: SourceProvider; serviceError?: boolean; readOnly?: boolean; onViewChange: (view: OratorioSettingsView) => void; onConnect: (provider: SourceProvider) => void }): ReactNode {
  const t = useOratorioSettingsT()
  const notifyError = useSettingsErrorToast()
  const controller = useSettingsController(readOnly, notifyError)
  const workspaces = useWorkspaceBindings()
  const [dialog, setDialog] = useState<DialogState>(null)
  const [selectedProjectId, setSelectedProjectId] = useState('')
  const [projectSyncStates, setProjectSyncStates] = useState<Record<string, ProjectSyncState>>({})
  const navigateProject = (id: string): void => { setSelectedProjectId(id); onViewChange('project') }
  const selectedProject = controller.draft.projects.find((item) => item.id === selectedProjectId) ?? controller.draft.projects[0]
  const startProjectSync = (project: OratorioProjectConfig, queued = false): void => {
    setProjectSyncStates((current) => ({ ...current, [project.id]: queued ? 'queued' : 'syncing' }))
    void oratorioHost().oratorio.request({
      method: 'POST', path: `/api/v1/sources/${project.provider}/sync-jobs`,
      body: { provider: project.provider, projects: [project.projectKey] }
    }).then(() => setProjectSyncStates((current) => ({ ...current, [project.id]: 'idle' })))
      .catch(() => {
        setProjectSyncStates((current) => ({ ...current, [project.id]: 'idle' }))
        notifyError(t('syncFailed'), () => startProjectSync(project))
      })
  }

  return <div className={`oratorio-native-settings${readOnly ? ' is-readonly' : ''}`} aria-disabled={readOnly}><div className="oratorio-native-settings__content">
      {serviceError ? <div className="oratorio-service-alert" role="alert">Oratorio is unavailable.</div> : null}
      {readOnly ? <div className="oratorio-service-alert" role="status">Remote Stack configuration is read-only. Source synchronization remains available.</div> : null}
      {controller.restartRequired ? <div className="oratorio-service-alert oratorio-service-alert--restart" role="status">{t('restartRequired')}</div> : null}
      {view === 'root' ? <RootSettings controller={controller} projectSyncStates={projectSyncStates} onNavigate={onViewChange} onNavigateProject={navigateProject} onDialog={setDialog} onConnect={onConnect} /> : null}
      {view === 'connect' ? <OratorioConnectSource controller={controller} provider={connectProvider} readOnly={readOnly} onExit={() => onViewChange('root')} onOpenBoard={() => oratorioHost().navigation.openMainView('board')} onConnected={setSelectedProjectId} /> : null}
      {view === 'github' || view === 'gitlab' ? <ProviderSettings provider={view} controller={controller} onBack={() => onViewChange('root')} onDialog={setDialog} onOperationError={notifyError} /> : null}
      {view === 'project' && selectedProject ? <ProjectSettings project={selectedProject} workspaceOptions={workspaces.options} workspaceLoading={workspaces.loading} sync={projectSyncStates[selectedProject.id] ?? 'idle'} controller={controller} onSync={() => startProjectSync(selectedProject)} onBack={() => onViewChange('root')} onRemoved={() => onViewChange('root')} /> : null}
    </div>
    {dialog?.kind === 'add-project' ? <AddProjectDialog existing={controller.draft.projects} profileOptions={{ github: controller.draft.github.profiles.map((profile) => ({ value: profile.id, label: `${profile.owner} · ${profile.installationId}` })), gitlab: controller.draft.gitlab.profiles.map((profile) => ({ value: profile.id, label: `${profile.instance} · ${profile.projectPath}` })) }} workspaceOptions={workspaces.options} workspaceLoading={workspaces.loading} providerInstances={{ github: providerInstance('github', controller.draft.github.endpoint), gitlab: providerInstance('gitlab', controller.draft.gitlab.endpoint) }} onClose={() => setDialog(null)} onSubmit={(project, profile) => {
      const transaction = cloneSettings(controller.snapshot())
      if (profile && project.provider === 'github') transaction.github.profiles.push(profile as GitHubInstallationProfile)
      if (profile && project.provider === 'gitlab') transaction.gitlab.profiles.push(profile as GitLabProjectProfile)
      transaction.projects.push(project)
      controller.change('configuration', transaction, () => startProjectSync(project, true)); setSelectedProjectId(project.id); setDialog(null)
    }} /> : null}
    {dialog?.kind === 'allowlist' ? <AllowlistDialog listKey={dialog.listKey} values={controller.draft[dialog.listKey]} projects={controller.draft.projects} onClose={() => setDialog(null)} onApply={(values) => { controller.change(dialog.listKey, values); setDialog(null) }} /> : null}
    {dialog?.kind === 'secret' ? <SecretDialog providerName={dialog.provider === 'github' ? 'GitHub' : 'GitLab'} secretName={dialog.secretName} onClose={() => setDialog(null)} onApply={(mode, value) => {
      const profileIndex = controller.draft.gitlab.profiles.findIndex((profile) => profile.id === dialog.profileId)
      const path = dialog.provider === 'github' ? `github.secrets.${dialog.secretKey}` : `gitlab.profiles.${Math.max(0, profileIndex)}.secrets.${dialog.secretKey}`
      controller.change(path, { configured: mode === 'replace', mode, value }); setDialog(null)
    }} /> : null}
    {dialog?.kind === 'profile' ? <ProfileDialog provider={dialog.provider} value={controller.draft[dialog.provider].profiles.find((profile) => profile.id === dialog.profileId) ?? newProfile(dialog.provider, controller.draft[dialog.provider].profiles.length + 1, controller.draft[dialog.provider].endpoint)} onClose={() => setDialog(null)} onApply={(value) => { const profiles = controller.draft[dialog.provider].profiles; controller.change(`${dialog.provider}.profiles`, profiles.some((profile) => profile.id === value.id) ? profiles.map((profile) => profile.id === value.id ? value : profile) : [...profiles, value]); setDialog(null) }} /> : null}
  </div>
}

type Controller = ReturnType<typeof useSettingsController>
function RootSettings({ controller, projectSyncStates, onNavigate, onNavigateProject, onDialog, onConnect }: { controller: Controller; projectSyncStates: Record<string, ProjectSyncState>; onNavigate: (view: OratorioSettingsView) => void; onNavigateProject: (id: string) => void; onDialog: (dialog: DialogState) => void; onConnect: (provider: SourceProvider) => void }) {
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
    <SettingsGroup title={t('projects')} description={t('projectsDescription')} headerAction={<Button variant="secondary" size="sm" iconLeft={<Plus size={14} />} onClick={() => onDialog({ kind: 'add-project' })}>{t('addProject')}</Button>}>
      {c.projects.length === 0 ? <SettingsRow><span className="ora-settings__value">{ct('noProjectsYet')}</span></SettingsRow> : null}
      {c.projects.map((project) => <ProjectRow key={project.id} project={project} sync={projectSyncStates[project.id] ?? 'idle'} manageLabel={t('manage')} onManage={() => onNavigateProject(project.id)} onChange={(checked) => controller.change('projects', c.projects.map((item) => item.id === project.id ? { ...item, enabled: checked } : item))} />)}
    </SettingsGroup>
    <SettingsGroup title={t('agentExecution')} description={t('capturedForRun')}>
      <SettingsRow label={t('approvalPolicy')} description={t('approvalDescription')} control={<FieldControl><Select<ApprovalPolicy> ariaLabel={t('approvalPolicy')} value={c.approvalPolicy} onValueChange={(value) => controller.change('approvalPolicy', value)} options={[{ value: 'default', label: t('approvalDefault') }, { value: 'interrupt', label: t('approvalAsk') }, { value: 'autoApprove', label: t('approvalAuto') }]} /></FieldControl>} />
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

function ProviderSettings({ provider, controller, onBack, onDialog, onOperationError }: { provider: SourceProvider; controller: Controller; onBack: () => void; onDialog: (dialog: DialogState) => void; onOperationError: NotifySettingsError }) {
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
  const secrets = [{ key: 'privateKey', label: t('privateKey') }, { key: 'privateKeyPath', label: t('privateKeyPath') }, { key: 'webhookSecret', label: t('webhookSecret') }]
  const gitlabSecrets = [{ key: 'token', label: t('projectToken') }, { key: 'webhookSecret', label: t('webhookSecret') }, { key: 'webhookSigningToken', label: t('signingToken') }]
  return <SettingsPanelShell title={name} description={t('providersDescription')} breadcrumb={<SettingsBreadcrumb parentLabel={t('oratorio')} currentLabel={name} onBack={onBack} />} action={<span className="ora-sync-action"><Button variant="secondary" size="sm" disabled={!readable} loading={sync === 'busy'} iconLeft={<RefreshCw size={14} />} onClick={startSync}>{t('syncNow')}</Button></span>}>
    <SettingsGroup title={providerTitle as unknown as string}>
      <SettingsRow label={t('endpoint')} description={t('endpointDescription')} control={<FieldControl><span className="ora-validated-field"><Input mono value={endpointDraft} invalid={Boolean(endpointError)} aria-label={`${name} ${t('endpoint')}`} onChange={(event) => setEndpointDraft(event.target.value)} onBlur={() => { if (!endpointError) controller.change(`${provider}.endpoint`, endpointDraft) }} />{endpointError ? <small role="alert">{t('endpointInvalid')}</small> : null}</span></FieldControl>} controlMinWidth={260} />
      {github ? <SettingsRow label="App ID" control={<FieldControl><Input mono value={controller.draft.github.appId} aria-label="GitHub App ID" onChange={(event) => controller.change('github.appId', event.target.value)} /></FieldControl>} /> : null}
      {!github ? <><SettingsRow label={t('sourceReads')} control={<FieldControl><PillSwitch checked={controller.draft.gitlab.enabled} onChange={(value) => controller.change('gitlab.enabled', value)} aria-label={`${name} ${t('sourceReads')}`} /></FieldControl>} /><SettingsRow label={t('apiBaseUrl')} control={<FieldControl><span className="ora-validated-field"><Input mono value={apiBaseDraft} invalid={Boolean(apiBaseError)} aria-label={`${name} ${t('apiBaseUrl')}`} onChange={(event) => setApiBaseDraft(event.target.value)} onBlur={() => { if (!apiBaseError) controller.change('gitlab.apiBaseUrl', apiBaseDraft) }} />{apiBaseError ? <small role="alert">{t('endpointInvalid')}</small> : null}</span></FieldControl>} /></> : null}
      <SettingsRow label={t('sourceWrites')} control={<FieldControl><PillSwitch checked={config.writesEnabled} onChange={(value) => controller.change(`${provider}.writesEnabled`, value)} aria-label={`${name} ${t('sourceWrites')}`} /></FieldControl>} />
      {github ? secrets.map((secret) => <SettingsRow key={secret.key} label={secret.label} description={t('storedSecret')} control={<FieldControl><Button variant="secondary" size="sm" aria-label={`${name} ${secret.label} ${t('manage')}`} onClick={() => onDialog({ kind: 'secret', provider, secretKey: secret.key, secretName: secret.label })}>{t('manage')}</Button></FieldControl>} />) : null}
    </SettingsGroup>
    <SettingsGroup title={github ? t('installationProfiles') : t('projectProfiles')} headerAction={<Button variant="secondary" size="sm" iconLeft={<Plus size={13} />} onClick={() => onDialog({ kind: 'profile', provider })}>{t('addProfile')}</Button>}>
      {config.profiles.map((item) => { const profileLabel = github ? `${(item as typeof controller.draft.github.profiles[number]).owner} · ${(item as typeof controller.draft.github.profiles[number]).installationId}` : `${(item as typeof controller.draft.gitlab.profiles[number]).instance} · ${(item as typeof controller.draft.gitlab.profiles[number]).projectPath}`; return <Fragment key={item.id}><SettingsRow label={profileLabel} control={<FieldControl><Button variant="secondary" size="sm" aria-label={`${name} ${profileLabel} ${t('manage')}`} onClick={() => onDialog({ kind: 'profile', provider, profileId: item.id })}>{t('manage')}</Button></FieldControl>} />{!github ? gitlabSecrets.map((secret) => <SettingsRow key={`${item.id}-${secret.key}`} label={`${profileLabel} · ${secret.label}`} description={t('storedSecret')} control={<FieldControl><Button variant="secondary" size="sm" aria-label={`${name} ${profileLabel} ${secret.label} ${t('manage')}`} onClick={() => onDialog({ kind: 'secret', provider, profileId: item.id, secretKey: secret.key, secretName: secret.label })}>{t('manage')}</Button></FieldControl>} />) : null}</Fragment> })}
    </SettingsGroup>
    <SettingsGroup title={t('sourceSync')}><SettingsRow label={t('schedule')} control={<FieldControl><IntervalPicker disabled={!readable} label={`${name} ${t('schedule')}`} valueSeconds={config.syncIntervalSeconds} onChange={(value) => controller.change(`${provider}.syncIntervalSeconds`, value)} /></FieldControl>} /></SettingsGroup>
  </SettingsPanelShell>
}

function ProjectSettings({ project, workspaceOptions, workspaceLoading, sync, controller, onSync, onBack, onRemoved }: { project: OratorioProjectConfig; workspaceOptions: WorkspaceBindingOption[]; workspaceLoading: boolean; sync: ProjectSyncState; controller: Controller; onSync: () => void; onBack: () => void; onRemoved: () => void }) {
  const t = useOratorioSettingsT(); const Icon = project.provider === 'github' ? GithubGlyph : GitlabGlyph
  function patch(change: Partial<OratorioProjectConfig>): void { controller.change('projects', controller.snapshot().projects.map((item) => item.id === project.id ? { ...item, ...change } : item)) }
  function startSync(): void { onSync() }
  const profileOptions = project.provider === 'github' ? controller.draft.github.profiles.map((profile) => ({ value: profile.id, label: `${profile.owner} · ${profile.installationId}` })) : controller.draft.gitlab.profiles.map((profile) => ({ value: profile.id, label: `${profile.instance} · ${profile.projectPath}` }))
  const resolvedWorkspaceOptions = project.workspacePath
    ? workspaceOptions.some((option) => option.value === project.workspacePath)
      ? workspaceOptions
      : [{ value: project.workspacePath, label: `${project.workspacePath} · ${t('workspaceUnavailable')}`, disabled: true }, ...workspaceOptions]
    : [{ value: '', label: t('workspaceNotBound'), disabled: true }, ...workspaceOptions]
  const providerReadable = project.provider === 'github' || controller.draft.gitlab.enabled
  return <SettingsPanelShell title={project.projectKey} description={t('projectsDescription')} breadcrumb={<SettingsBreadcrumb parentLabel={t('projects')} currentLabel={project.projectKey} onBack={onBack} />}>
    <SettingsGroup title={t('project')}>
      <SettingsRow label={t('providers')} control={<span className="ora-provider-identity"><Icon size={18} />{project.provider === 'github' ? 'GitHub' : 'GitLab'}</span>} />
      <SettingsRow label={t('project')} description={project.projectKey} control={<span className="ora-settings__value ora-settings__value--mono">{project.projectKey}</span>} />
      <SettingsRow label={project.provider === 'github' ? t('installationProfiles') : t('projectProfiles')} control={<FieldControl><Select ariaLabel={project.provider === 'github' ? t('installationProfiles') : t('projectProfiles')} value={project.profileId} onValueChange={(value) => patch({ profileId: value })} options={profileOptions} /></FieldControl>} />
      <SettingsRow label={t('enabled')} control={<FieldControl><PillSwitch checked={project.enabled} onChange={(value) => patch({ enabled: value })} aria-label={`${project.projectKey} ${t('enabled')}`} /></FieldControl>} />
    </SettingsGroup>
    <SettingsGroup title={t('workspace')}><SettingsRow label={t('workspace')} control={<FieldControl>{resolvedWorkspaceOptions.length > 0 ? <Select ariaLabel={t('workspace')} value={project.workspacePath} disabled={workspaceLoading} onValueChange={(value) => patch({ workspacePath: value })} options={resolvedWorkspaceOptions} /> : <span className="ora-settings__value">{t('workspaceEmpty')}</span>}</FieldControl>} /></SettingsGroup>
    <SettingsGroup title={t('sourceSync')}><SettingsRow label={t('syncNow')} control={<span className="ora-sync-action"><Button variant="secondary" size="sm" disabled={!providerReadable} loading={sync === 'queued' || sync === 'syncing'} iconLeft={<RefreshCw size={13} />} onClick={startSync}>{sync === 'queued' || sync === 'syncing' ? t('syncing') : t('syncNow')}</Button></span>} /></SettingsGroup>
    <SettingsGroup><SettingsRow label={t('remove')} description={t('removeProjectMessage')} control={<Button variant="danger" size="sm" iconLeft={<Trash2 size={13} />} onClick={() => void oratorioHost().ui.confirm({ title: t('removeProjectTitle'), message: t('removeProjectMessage'), confirmLabel: t('remove'), cancelLabel: t('cancel'), danger: true }).then((confirmed) => { if (!confirmed) return; controller.change('configuration', withoutProject(controller.snapshot(), project.id)); onRemoved() })}>{t('remove')}</Button>} /></SettingsGroup>
  </SettingsPanelShell>
}

function ProjectRow({ project, sync, manageLabel, onManage, onChange }: { project: OratorioProjectConfig; sync: ProjectSyncState; manageLabel: string; onManage: () => void; onChange: (checked: boolean) => void }) { const t = useOratorioSettingsT(); const Icon = project.provider === 'github' ? GithubGlyph : GitlabGlyph; const syncLabel = sync === 'queued' ? t('syncQueued') : sync === 'syncing' ? t('syncing') : ''; return <SettingsRow label={<span className="ora-settings__label"><Icon size={15} />{project.projectKey}</span>} description={`${project.provider === 'github' ? 'GitHub' : 'GitLab'} · ${project.workspacePath}${syncLabel ? ` · ${syncLabel}` : ''}`} control={<span className="oratorio-native-settings__row-control"><Button variant="secondary" size="sm" aria-label={`${project.projectKey} ${manageLabel}`} onClick={onManage}>{manageLabel}</Button><PillSwitch checked={project.enabled} onChange={onChange} aria-label={`${t('enable')} ${project.projectKey}`} /></span>} /> }

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
