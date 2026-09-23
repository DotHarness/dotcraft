import { useEffect, useMemo, useRef, useState } from 'react'
import { Check, CircleAlert, Pencil } from 'lucide-react'
import { Button, IconButton, SettingsBreadcrumb, SettingsGroup, SettingsPanelShell, SettingsRow } from '../ui'
import { describeOratorioError, oratorioClient } from '../oratorio-client'
import { oratorioHost, showOratorioToast } from '../runtime'
import { useOratorioConnectT } from './oratorio-connect-i18n'
import {
  CONNECT_STEPS,
  buildConnectTransaction,
  connectStepIssues,
  createConnectDraft,
  scheduleSeconds,
  shouldDetectGitHubInstallation,
  type ConnectContext,
  type ConnectDraft,
  type WorkspaceListState
} from './oratorio-connect-model'
import { ProviderGlyph, StepHeading, providerName, useGitLabTokenKindLabel } from './oratorio-connect-parts'
import { AutomationStep, ProjectStep, SourceStep, WorkspaceStep } from './oratorio-connect-steps'
import type { ConnectionSave } from './oratorio-settings-controller'
import { projectOwner, providerInstance, type OratorioProjectConfig, type OratorioSettingsConfig, type SourceProvider } from './oratorio-settings-model'
import type { LoadedOratorioSettings } from './oratorio-settings-service'
import '../oratorio-connect-source.css'

export interface ConnectController {
  draft: OratorioSettingsConfig
  snapshot(): OratorioSettingsConfig
  saveConnection<T extends ConnectionSave>(build: (snapshot: OratorioSettingsConfig) => T): Promise<{ loaded: LoadedOratorioSettings; built: T }>
  saveSchedule(provider: SourceProvider, intervalSeconds: number | null): Promise<void>
}

type ConnectPhase = 'idle' | 'pending' | 'success' | 'error'
type ConnectResult =
  | { kind: 'synced'; issues: number; requests: number }
  | { kind: 'running' }
  | { kind: 'detection' }
  | { kind: 'saveFailed'; message: string }
  | { kind: 'syncFailed'; message: string }

/** What a successful save already committed, so a retry resumes after it instead of saving the connection twice. */
interface SavedConnection {
  draft: ConnectDraft
  project: OratorioProjectConfig
  scheduleSaved: boolean
}

const POLL_INTERVAL_MS = 1500
const POLL_WINDOW_MS = 45_000

function useLocalProjects(): { projects: readonly { path: string; name: string; active: boolean }[]; state: WorkspaceListState } {
  const [projects, setProjects] = useState<readonly { path: string; name: string; active: boolean }[]>([])
  const [loading, setLoading] = useState(true)
  useEffect(() => {
    let active = true
    void oratorioHost().workspaces.listLocalProjects()
      .then((payload) => { if (active) setProjects(payload) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])
  return { projects, state: loading ? 'loading' : projects.length === 0 ? 'empty' : 'ready' }
}

function sameDraft(left: ConnectDraft, right: ConnectDraft): boolean {
  return JSON.stringify(left) === JSON.stringify(right)
}

export function OratorioConnectSource({ controller, provider, readOnly, onExit, onOpenBoard, onConnected }: {
  controller: ConnectController
  provider: SourceProvider
  readOnly: boolean
  onExit: () => void
  onOpenBoard: () => void
  onConnected: (project: OratorioProjectConfig) => void
}): JSX.Element {
  const t = useOratorioConnectT()
  const workspaces = useLocalProjects()
  const [draft, setDraft] = useState<ConnectDraft>(() => createConnectDraft(controller.snapshot(), provider))
  const [step, setStep] = useState(0)
  const [phase, setPhase] = useState<ConnectPhase>('idle')
  const [result, setResult] = useState<ConnectResult | null>(null)
  const [saved, setSaved] = useState<SavedConnection | null>(null)
  const savedRef = useRef<SavedConnection | null>(null)

  useEffect(() => {
    if (draft.workspacePath || workspaces.state !== 'ready') return
    const foreground = workspaces.projects.find((project) => project.active) ?? workspaces.projects[0]
    if (foreground) setDraft((current) => ({ ...current, workspacePath: foreground.path }))
  }, [draft.workspacePath, workspaces])

  const context: ConnectContext = useMemo(() => ({ settings: controller.draft, workspaces: workspaces.state, readOnly, connectedProjectKey: saved?.project.projectKey }), [controller.draft, readOnly, saved, workspaces.state])
  const stepId = CONNECT_STEPS[step]
  const issues = useMemo(() => connectStepIssues(stepId, draft, context), [context, draft, stepId])
  const last = step === CONNECT_STEPS.length - 1
  const pending = phase === 'pending'
  const canAdvance = issues.length === 0 && (!last || CONNECT_STEPS.slice(0, -1).every((id) => connectStepIssues(id, draft, context).length === 0))
  const update = (patch: Partial<ConnectDraft>): void => setDraft((current) => ({ ...current, ...patch }))
  const goTo = (index: number): void => { setStep(index); if (phase !== 'pending') setPhase('idle') }
  const stepLabels = [t('stepSource'), t('stepProject'), t('stepWorkspace'), t('stepAutomation'), t('stepConnect')]

  function remember(next: SavedConnection | null): void {
    savedRef.current = next
    setSaved(next)
  }

  function saveDraft() {
    return controller.saveConnection((snapshot) => ({
      ...buildConnectTransaction(snapshot, draft),
      detectGitHubInstallations: shouldDetectGitHubInstallation(snapshot, draft)
    }))
  }

  async function connect(): Promise<void> {
    setPhase('pending')
    setResult(null)
    let connection = savedRef.current && sameDraft(savedRef.current.draft, draft) ? savedRef.current : null
    if (!connection) {
      let saveResult: Awaited<ReturnType<typeof saveDraft>>
      try {
        saveResult = await saveDraft()
      } catch (error) {
        setResult({ kind: 'saveFailed', message: describeOratorioError(error) })
        setPhase('error')
        return
      }
      const { loaded, built } = saveResult
      connection = { draft, project: built.project, scheduleSaved: false }
      remember(connection)
      showOratorioToast({ message: t('configurationSaved'), tone: 'success' })
      onConnected(built.project)
      const owner = projectOwner(built.project.projectKey).toLowerCase()
      if (built.detectGitHubInstallations && loaded.gitHubInstallationWarnings.some((warning) => warning.owner.toLowerCase() === owner)) {
        setResult({ kind: 'detection' })
        setPhase('error')
        return
      }
    }
    try {
      if (!connection.scheduleSaved) {
        await controller.saveSchedule(draft.provider, scheduleSeconds(draft))
        connection = { ...connection, scheduleSaved: true }
        remember(connection)
      }
      const job = await oratorioClient.sync(draft.provider, 'incremental', [connection.project.projectKey])
      const settled = await pollSyncJob(draft.provider, job.jobId, job.status)
      if (!settled) {
        setResult({ kind: 'running' })
        setPhase('success')
        return
      }
      const status = settled.status.toLowerCase()
      if (status === 'succeeded' && !(settled.projectsFailed ?? 0)) {
        setResult({ kind: 'synced', issues: settled.issuesImported ?? 0, requests: settled.reviewTargetsImported ?? 0 })
        setPhase('success')
      } else {
        setResult({ kind: 'syncFailed', message: settled.errorMessage ?? settled.errorCode ?? status })
        setPhase('error')
      }
    } catch (error) {
      setResult({ kind: 'syncFailed', message: describeOratorioError(error) })
      setPhase('error')
    }
  }

  const currentLabel = draft.provider === 'github' ? t('connectGitHub') : t('connectGitLab')
  return (
    <SettingsPanelShell title={currentLabel} description={t('wizardDescription')} breadcrumb={<SettingsBreadcrumb parentLabel={t('oratorioSettings')} currentLabel={currentLabel} onBack={() => { if (!pending) onExit() }} />}>
      {readOnly ? <div className="oratorio-service-alert" role="status">{t('readOnlyNotice')}</div> : null}
      <div className="setup-wizard-title-block ora-connect__stepper">
        <nav className="setup-stepper-row" aria-label={currentLabel}>
          <div className="setup-stepper-compact-heading"><span>{t('stepOf', { n: step + 1, total: CONNECT_STEPS.length })}</span><strong>{stepLabels[step]}</strong></div>
          <ol className="setup-stepper-list" style={{ gridTemplateColumns: `repeat(${CONNECT_STEPS.length}, minmax(0, 1fr))` }}>
            {stepLabels.map((label, index) => {
              const active = index === step
              const completed = index < step
              const returnable = completed && !pending && !readOnly
              return (
                <li key={label} className="setup-stepper-item" data-state={completed ? 'complete' : active ? 'active' : 'future'}>
                  <button type="button" className="setup-stepper-button" disabled={!returnable} aria-label={label} aria-current={active ? 'step' : undefined} onClick={() => { if (returnable) goTo(index) }}>
                    <span className="setup-stepper-marker" aria-hidden="true">{completed ? <Check size={14} strokeWidth={2.4} /> : index + 1}</span>
                    <span className="setup-stepper-label">{label}</span>
                  </button>
                </li>
              )
            })}
          </ol>
        </nav>
      </div>
      <form className="ora-connect__form" onSubmit={(event) => { event.preventDefault(); if (!canAdvance || pending) return; if (last) void connect(); else goTo(step + 1) }}>
        <div key={step} className="setup-wizard-step-panel ora-connect__panel">
          {stepId === 'source' ? <SourceStep draft={draft} settings={controller.draft} issues={issues} readOnly={readOnly} update={update} /> : null}
          {stepId === 'project' ? <ProjectStep draft={draft} settings={controller.draft} issues={issues} readOnly={readOnly} update={update} /> : null}
          {stepId === 'workspace' ? <WorkspaceStep draft={draft} settings={controller.draft} issues={issues} readOnly={readOnly} update={update} projects={workspaces.projects} workspaces={workspaces.state} /> : null}
          {stepId === 'automation' ? <AutomationStep draft={draft} settings={controller.draft} issues={issues} readOnly={readOnly} update={update} /> : null}
          {stepId === 'connect' ? <ConnectSummary draft={draft} phase={phase} result={result} onChange={goTo} onOpenBoard={onOpenBoard} onExit={onExit} onConnectAnother={() => { setDraft(createConnectDraft(controller.snapshot(), draft.provider)); remember(null); setResult(null); setPhase('idle'); setStep(0) }} /> : null}
        </div>
        <div className="ora-connect__footer">
          {phase === 'success' ? <><span className="ora-connect__footer-spacer" /><Button variant="secondary" onClick={onExit}>{t('done')}</Button></> : <>
            <Button variant="ghost" onClick={onExit} disabled={pending}>{t('cancel')}</Button>
            <span className="ora-connect__footer-spacer" />
            {step > 0 ? <Button variant="secondary" onClick={() => goTo(step - 1)} disabled={pending}>{t('back')}</Button> : null}
            <Button type="submit" variant="primary" disabled={!canAdvance || pending} loading={pending}>{last ? (phase === 'error' ? t('retry') : t('connectAndSync')) : t('next')}</Button>
          </>}
        </div>
      </form>
    </SettingsPanelShell>
  )
}

async function pollSyncJob(provider: SourceProvider, jobId: string, initialStatus: string): Promise<Awaited<ReturnType<typeof oratorioClient.syncJob>> | null> {
  const terminal = (status: string): boolean => !['queued', 'running'].includes(status.toLowerCase())
  if (terminal(initialStatus)) return oratorioClient.syncJob(provider, jobId)
  const deadline = Date.now() + POLL_WINDOW_MS
  while (Date.now() < deadline) {
    await new Promise((resolve) => window.setTimeout(resolve, POLL_INTERVAL_MS))
    const job = await oratorioClient.syncJob(provider, jobId)
    if (terminal(job.status)) return job
  }
  return null
}

function ConnectSummary({ draft, phase, result, onChange, onOpenBoard, onExit, onConnectAnother }: {
  draft: ConnectDraft
  phase: ConnectPhase
  result: ConnectResult | null
  onChange: (step: number) => void
  onOpenBoard: () => void
  onExit: () => void
  onConnectAnother: () => void
}): JSX.Element {
  const t = useOratorioConnectT()
  const tokenKindLabel = useGitLabTokenKindLabel()
  const github = draft.provider === 'github'
  const change = (step: number, section: string): JSX.Element | undefined => phase === 'pending' || phase === 'success' ? undefined : <IconButton icon={<Pencil size={16} />} label={t('changeSection', { section })} tooltipLabel={t('change')} onClick={() => onChange(step)} />
  const schedule = draft.schedule === 'off' ? t('manualSync') : draft.schedule === '15m' ? t('every15') : draft.schedule === '1h' ? t('everyHour') : t('everyMinutes', { n: draft.customMinutes })
  const access = github ? t('accessGitHub', { id: draft.github.appId.trim() }) : `${tokenKindLabel(draft.gitlab.tokenKind)} · ••••••••`
  const failure = phase === 'error' && result && result.kind !== 'synced' && result.kind !== 'running' ? result : null
  return (
    <div className="ora-connect__step">
      <StepHeading title={phase === 'success' ? t('connectedTitle') : t('connectTitle')} description={phase === 'success' ? t('connectedDescription') : t('connectDescription')} />
      <SettingsGroup>
        <SettingsRow label={t('summarySource')} description={<span className="ora-connect__summary"><ProviderGlyph provider={draft.provider} />{providerName(draft.provider)} · {providerInstance(draft.provider, draft.endpoint)}</span>} control={change(0, t('summarySource'))} />
        <SettingsRow label={t('summaryAccess')} description={access} control={change(0, t('summaryAccess'))} />
        <SettingsRow label={github ? t('repository') : t('project')} description={<span className="ora-connect__mono">{draft.projectKey.trim()}</span>} control={change(1, github ? t('repository') : t('project'))} />
        <SettingsRow label={t('summaryWorkspace')} description={<span className="ora-connect__mono">{draft.workspacePath}</span>} control={change(2, t('summaryWorkspace'))} />
        <SettingsRow label={t('summaryAutomation')} description={t('automationSummary', { schedule, review: draft.autoReview ? t('on') : t('offState'), writes: draft.allowWrites ? t('allowed') : t('offState') })} control={change(3, t('summaryAutomation'))} />
      </SettingsGroup>
      {phase === 'success' && result ? (
        <div className="ora-connect__result" data-tone="success" role="status">
          <span className="ora-connect__result-line"><span className="ora-state-dot" data-tone="success" /><strong>{result.kind === 'running' ? t('connectedTitle') : t('readAccessConfirmed')}</strong></span>
          <p>{result.kind === 'synced' ? t(github ? 'syncedGitHub' : 'syncedGitLab', { issues: result.issues, requests: result.requests, project: draft.projectKey.trim() }) : t('stillSyncing')}</p>
          <span className="ora-connect__result-actions">
            <Button variant="primary" onClick={onOpenBoard}>{t('openBoard')}</Button>
            <Button variant="secondary" onClick={onExit}>{t('oratorioSettings')}</Button>
            <Button variant="ghost" onClick={onConnectAnother}>{t('connectAnother')}</Button>
          </span>
        </div>
      ) : null}
      {failure ? (
        <div className="ora-connect__result" data-tone="error" role="alert">
          <span className="ora-connect__result-line"><CircleAlert size={15} aria-hidden="true" /><strong>{failure.kind === 'saveFailed' ? t('saveFailedTitle') : t('syncFailedTitle')}</strong></span>
          <p>{failure.kind === 'detection' ? t('detectionWarning') : failure.message} {failure.kind === 'saveFailed' ? t('notSavedNote') : t('savedNote')}</p>
          <span className="ora-connect__result-actions">
            <Button variant="secondary" onClick={() => onChange(failure.kind === 'detection' ? 1 : 0)}>{failure.kind === 'detection' ? t('enterInstallationId') : t('backToAccess')}</Button>
          </span>
        </div>
      ) : null}
    </div>
  )
}
