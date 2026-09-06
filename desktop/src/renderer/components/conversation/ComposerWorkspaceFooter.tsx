import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type JSX, type ReactNode } from 'react'
import { ComposerOverlapBand, useComposerOverlapBandHeight } from './useComposerOverlapBand'
import { createPortal } from 'react-dom'
import { ArrowRightLeft, ChevronDown, Cloud, Folder, FolderPlus, GitBranch, Laptop, ListChecks, Plus, Server } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { normalizeGitPathKey, useGitStore, type GitBranchListSnapshot } from '../../stores/gitStore'
import { changelistLabel, usePerforceChangelistStore, type PerforceChangelistEntry, type PerforceChangelistSnapshot } from '../../stores/perforceChangelistStore'
import { useSourceControlStore } from '../../stores/sourceControlStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { addToast } from '../../stores/toastStore'
import type { Thread } from '../../types/thread'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { isDefaultChatWorkspacePathCandidate } from '../../../shared/defaultChatWorkspace'
import { normalizeWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import { WorktreeHandoffDialog } from './WorktreeHandoffDialog'
import { useAddProjectFlow } from '../projects/AddProject'
import { ActionTooltip } from '../ui/ActionTooltip'
import { RunOnPicker, useRunOnVisible } from './RunOnPicker'
import {
  FooterMenuButton,
  FooterMenuDivider,
  FooterMenuSearchField,
  WorkspaceFooterPill,
  WorkspaceMenuItem,
  menuStyle
} from './composerFooterPrimitives'
import {
  CreateBranchDialog,
  CreateChangelistDialog,
  branchNameError,
  normalizeBranchName
} from './ComposerWorkspaceFooterDialogs'

export type ComposerWorkspaceMode = 'local' | 'worktree'

interface ComposerWorkspaceFooterProps {
  workspacePath: string
  mode: ComposerWorkspaceMode
  variant: 'welcome' | 'thread'
  remoteWorkspace?: boolean
  thread?: Thread | null
  baseRef?: string | null
  worktreeBranchName?: string | null
  onWelcomeModeChange?: (mode: ComposerWorkspaceMode) => void
  onBaseRefChange?: (baseRef: string | null) => void
  onWorktreeBranchNameChange?: (branchName: string | null) => void
  onWelcomeWorkspaceChange?: (workspacePath: string) => Promise<void> | void
  // Perforce changelist pre-selected on the welcome screen; applied to the thread the first message creates.
  welcomeChangelist?: string | null
  onWelcomeChangelistChange?: (changelist: string) => void
  /** True while a turn runs; only the Run on chip refuses input, git controls stay live. */
  turnRunning?: boolean
}

type OpenMenu = 'project' | 'workspace' | 'branch' | 'changelist' | 'runOn' | null

const GIT_BRANCH_REFRESH_INTERVAL_MS = 5_000

const footerStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  minHeight: '28px',
  color: 'var(--composer-footer-text)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
}

function currentBranchLabel(branches: GitBranchListSnapshot | null): string | null {
  return branches?.current || branches?.detachedHead || null
}

function workspaceSlug(path: string): string {
  const trimmed = path.trim().replace(/[\\/]+$/, '')
  const leaf = trimmed.split(/[\\/]+/).filter(Boolean).pop() || 'worktree'
  const slug = leaf
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
  return slug || 'worktree'
}

function projectIdentity(project: WorkspaceProjectSummary): string {
  return project.projectId?.trim() || normalizeWorkspaceProjectKey(project.path)
}

function projectIcon(project: WorkspaceProjectSummary): ReactNode {
  if (project.kind !== 'remote') {
    return <Folder size={14} strokeWidth={1.8} aria-hidden />
  }
  return project.remote?.source === 'servers'
    ? <Server size={14} strokeWidth={1.8} aria-hidden />
    : <Cloud size={14} strokeWidth={1.8} aria-hidden />
}

function defaultWorktreeBranchName(path: string): string {
  return `dotcraft/${workspaceSlug(path)}`
}

export function ComposerWorkspaceFooter({
  workspacePath,
  mode,
  variant,
  remoteWorkspace = false,
  thread = null,
  baseRef = null,
  worktreeBranchName = null,
  onWelcomeModeChange,
  onBaseRefChange,
  onWorktreeBranchNameChange,
  onWelcomeWorkspaceChange,
  welcomeChangelist = null,
  onWelcomeChangelistChange,
  turnRunning = false
}: ComposerWorkspaceFooterProps): JSX.Element | null {
  const t = useT()
  const runOnVisible = useRunOnVisible()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const projects = useWorkspaceProjectsStore((s) => s.projects)
  const chat = useWorkspaceProjectsStore((s) => s.chat)
  const foregroundProjectId = useWorkspaceProjectsStore((s) => s.foregroundProjectId)
  const [openMenu, setOpenMenu] = useState<OpenMenu>(null)
  const [branchQuery, setBranchQuery] = useState('')
  const [changelistQuery, setChangelistQuery] = useState('')
  const [projectQuery, setProjectQuery] = useState('')
  const addProject = useAddProjectFlow()
  const [busy, setBusy] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  const [createChangelistOpen, setCreateChangelistOpen] = useState(false)
  const [handoffMode, setHandoffMode] = useState<ComposerWorkspaceMode | null>(null)
  const [branchDraft, setBranchDraft] = useState('dotcraft/')
  const [changelistDraft, setChangelistDraft] = useState('')
  const footerRef = useRef<HTMLDivElement>(null)
  // Each footer dropdown opens upward over the same-tone composer card; the band stops
  // exactly at the card's top edge (the hook finds the card via the composer root).
  const projectMenuRef = useRef<HTMLDivElement>(null)
  const workspaceMenuRef = useRef<HTMLDivElement>(null)
  const branchMenuRef = useRef<HTMLDivElement>(null)
  const changelistMenuRef = useRef<HTMLDivElement>(null)
  const projectBandHeight = useComposerOverlapBandHeight(projectMenuRef, openMenu === 'project')
  const workspaceBandHeight = useComposerOverlapBandHeight(workspaceMenuRef, openMenu === 'workspace')
  const branchBandHeight = useComposerOverlapBandHeight(branchMenuRef, openMenu === 'branch')
  const changelistBandHeight = useComposerOverlapBandHeight(changelistMenuRef, openMenu === 'changelist')
  const isThread = variant === 'thread'
  const threadBusy = thread?.runtime?.busy === true
    || thread?.runtime?.running === true
    || thread?.runtime?.waitingOnApproval === true
    || thread?.runtime?.waitingOnInput === true
    || Boolean(thread?.runtime?.maintenanceKind)
  const handoffDisabledReason = isThread && threadBusy
    ? t('workspaceFooter.handoffUnavailableDuringConversation')
    : null
  const selectedProjectId = foregroundProjectId || normalizeWorkspaceProjectKey(workspacePath)
  const foregroundIsChat =
    isDefaultChatWorkspacePathCandidate(workspacePath) ||
    (chat != null && projectIdentity(chat) === selectedProjectId)
  const branchActionPath = workspacePath.trim()
  const sourceControlEnabled = capabilities?.sourceControlManagement === true
  const ensureSourceControl = useSourceControlStore((s) => s.ensure)
  const sourceControlWorkspacePath = useSourceControlStore((s) => s.workspacePath)
  const sourceControlProvider = useSourceControlStore((s) =>
    s.workspacePath === branchActionPath ? s.effectiveProvider : null
  )
  const perforceChangelistAvailable = useSourceControlStore((s) =>
    s.workspacePath === branchActionPath ? s.perforceChangelist === true : false
  )
  const isPerforceProvider = sourceControlProvider === 'perforce'
  const sourceControlProviderLoading =
    sourceControlEnabled && Boolean(branchActionPath) && (
      sourceControlWorkspacePath !== branchActionPath ||
      sourceControlProvider == null
    )
  const hideGitForSourceControl = sourceControlEnabled && sourceControlProvider != null && sourceControlProvider !== 'git'
  const isPerforceWorkspace = variant === 'thread' && isPerforceProvider && perforceChangelistAvailable && Boolean(thread?.id)
  // Welcome (pre-thread) Perforce: the pick is stashed in welcome state and applied to
  // the thread the first message creates.
  const isPerforceWelcome = variant === 'welcome' && isPerforceProvider && perforceChangelistAvailable && Boolean(branchActionPath)
  const [welcomeChangelists, setWelcomeChangelists] = useState<PerforceChangelistEntry[]>([])
  const [welcomeChangelistStatus, setWelcomeChangelistStatus] = useState<'idle' | 'loading' | 'available' | 'error'>('idle')
  const changelistState = usePerforceChangelistStore((s) =>
    thread?.id ? s.byThreadId[thread.id] : undefined
  )
  const changelistSnapshot = changelistState?.snapshot ?? null
  const defaultChangelistEntry: PerforceChangelistEntry = {
    id: 'default', isDefault: true, description: t('workspaceFooter.changelistDefault'), user: '', client: '', status: 'pending'
  }
  const changelistEntries = isPerforceWelcome
    ? (welcomeChangelists.length > 0 ? welcomeChangelists : [defaultChangelistEntry])
    : (changelistSnapshot?.changelists ?? [defaultChangelistEntry])
  const selectedChangelist = isPerforceWelcome
    ? (welcomeChangelist ?? 'default')
    : (changelistSnapshot?.target?.changelist ?? 'default')
  const changelistControlsReady = isPerforceWelcome
    ? welcomeChangelistStatus === 'available'
    : changelistState?.status === 'available'
  const branchActionPathKey = normalizeGitPathKey(branchActionPath)
  const gitPathState = useGitStore((s) =>
    branchActionPathKey ? s.branchesByPath[branchActionPathKey] : undefined
  )
  const gitAvailability = remoteWorkspace || !branchActionPath || foregroundIsChat || hideGitForSourceControl
    ? 'unavailable'
    : gitPathState?.status ?? 'checking'
  const branches = gitPathState?.snapshot ?? null
  const branchControlsReady = gitAvailability === 'available' && branches != null
  const canUseWorktrees = capabilities?.gitWorktrees === true && !remoteWorkspace && branchControlsReady
  const localWorkspacePath = thread?.worktree?.workspacePath || thread?.workspacePath || branchActionPath
  const selectedBaseRef = baseRef || currentBranchLabel(branches)
  const threadWorktreeBranchName = thread?.worktree?.branchName?.trim() || null
  const branchLabel = mode === 'worktree' && variant === 'welcome'
    ? (worktreeBranchName || selectedBaseRef || t('workspaceFooter.branchUnknown'))
    : (
        currentBranchLabel(branches) ||
        (variant === 'thread' && mode === 'worktree' ? threadWorktreeBranchName : null) ||
        t('workspaceFooter.branchUnknown')
      )
  const locationLabel = variant === 'welcome'
    ? (mode === 'worktree' ? t('workspaceFooter.newWorktree') : t('workspaceFooter.workLocally'))
    : (mode === 'worktree' ? t('workspaceFooter.worktree') : t('workspaceFooter.local'))
  const showBranchHandoffOnly = variant === 'thread' && mode === 'worktree'
  const projectOptions = useMemo(() => {
    if (variant !== 'welcome') return []
    if (projects.some((project) => projectIdentity(project) === selectedProjectId)) {
      return projects
    }
    return [
      {
        projectId: selectedProjectId,
        kind: 'local' as const,
        path: workspacePath,
        identityWorkspacePath: workspacePath,
        name: workspaceSlug(workspacePath),
        state: 'foreground' as const,
        running: true,
        loaded: true,
        threadCount: 0,
        threads: [],
        pinned: false
      },
      ...projects
    ].filter((project) => project.path.trim().length > 0)
  }, [projects, selectedProjectId, variant, workspacePath])
  const selectedProject = projectOptions.find((project) =>
    projectIdentity(project) === selectedProjectId
  )
  // The default Chat workspace is not a project: when it is foreground, suppress the
  // project picker rather than surfacing the Chat workspace path as a project label.
  const showProjectSelector = variant === 'welcome' && projectOptions.length > 0 && !foregroundIsChat
  const filteredProjects = useMemo(() => {
    const query = projectQuery.trim().toLowerCase()
    if (!query) return projectOptions
    return projectOptions.filter((project) =>
      (project.name || workspaceSlug(project.path)).toLowerCase().includes(query) ||
      project.path.toLowerCase().includes(query)
    )
  }, [projectOptions, projectQuery])

  useEffect(() => {
    function closeOnOutsideClick(event: MouseEvent): void {
      if (!footerRef.current?.contains(event.target as Node)) setOpenMenu(null)
    }
    document.addEventListener('mousedown', closeOnOutsideClick)
    return () => document.removeEventListener('mousedown', closeOnOutsideClick)
  }, [])

  useEffect(() => {
    ensureSourceControl(branchActionPath, sourceControlEnabled)
  }, [branchActionPath, ensureSourceControl, sourceControlEnabled])

  useEffect(() => {
    if (!isPerforceWorkspace || !thread?.id) return
    void usePerforceChangelistStore.getState().ensure(thread.id)
  }, [isPerforceWorkspace, thread?.id])

  useEffect(() => {
    if (!isPerforceWelcome) {
      setWelcomeChangelists([])
      setWelcomeChangelistStatus('idle')
      return
    }
    let cancelled = false
    setWelcomeChangelistStatus('loading')
    // No threadId → the AppServer lists the foreground workspace's pending changelists.
    void window.api.appServer.sendRequest('sourceControl/changelist/list', {}, 30_000)
      .then((snap) => {
        if (cancelled) return
        setWelcomeChangelists((snap as PerforceChangelistSnapshot)?.changelists ?? [])
        setWelcomeChangelistStatus('available')
      })
      .catch(() => {
        if (!cancelled) setWelcomeChangelistStatus('error')
      })
    return () => { cancelled = true }
  }, [isPerforceWelcome, branchActionPath])

  useEffect(() => {
    if (openMenu !== 'changelist') {
      setChangelistQuery('')
    }
  }, [openMenu])

  useEffect(() => {
    if (openMenu !== 'project') {
      setProjectQuery('')
    }
  }, [openMenu])

  const hideForUnavailableGit = useCallback(() => {
    setOpenMenu(null)
    if (variant === 'welcome') {
      if (mode !== 'local') onWelcomeModeChange?.('local')
      if (baseRef !== null) onBaseRefChange?.(null)
      if (worktreeBranchName !== null) onWorktreeBranchNameChange?.(null)
    }
  }, [
    baseRef,
    mode,
    onBaseRefChange,
    onWelcomeModeChange,
    onWorktreeBranchNameChange,
    variant,
    worktreeBranchName
  ])

  const loadBranches = useCallback(async (options: { force?: boolean } = {}) => {
    if (sourceControlProviderLoading) {
      return
    }
    if (remoteWorkspace || !branchActionPath || foregroundIsChat || hideGitForSourceControl) {
      hideForUnavailableGit()
      return
    }
    await useGitStore.getState().ensureBranches(branchActionPath, { force: options.force })
  }, [branchActionPath, foregroundIsChat, hideForUnavailableGit, hideGitForSourceControl, remoteWorkspace, sourceControlProviderLoading])

  useEffect(() => {
    if (sourceControlProviderLoading) {
      return
    }
    if (remoteWorkspace || !branchActionPath || foregroundIsChat || hideGitForSourceControl) {
      hideForUnavailableGit()
      return
    }
    void loadBranches()
  }, [branchActionPath, foregroundIsChat, hideForUnavailableGit, hideGitForSourceControl, loadBranches, remoteWorkspace, sourceControlProviderLoading])

  useEffect(() => {
    if (sourceControlProviderLoading) return
    if (gitAvailability !== 'unavailable') return
    hideForUnavailableGit()
  }, [gitAvailability, hideForUnavailableGit, sourceControlProviderLoading])

  useEffect(() => {
    if (variant !== 'welcome' || mode !== 'worktree' || baseRef || !branches) return
    onBaseRefChange?.(currentBranchLabel(branches))
  }, [baseRef, branches, mode, onBaseRefChange, variant])

  useEffect(() => {
    if (remoteWorkspace || !branchActionPath || foregroundIsChat || gitAvailability !== 'available') return
    const timer = window.setInterval(() => {
      void loadBranches({ force: true })
    }, GIT_BRANCH_REFRESH_INTERVAL_MS)
    return () => window.clearInterval(timer)
  }, [branchActionPath, foregroundIsChat, gitAvailability, loadBranches, remoteWorkspace])

  const filteredBranches = useMemo(() => {
    const query = branchQuery.trim().toLowerCase()
    const values = branches?.branches ?? []
    if (!query) return values
    return values.filter((branch) => branch.name.toLowerCase().includes(query))
  }, [branchQuery, branches])

  const filteredChangelists = useMemo(() => {
    const query = changelistQuery.trim().toLowerCase()
    if (!query) return changelistEntries
    return changelistEntries.filter((entry) =>
      changelistLabel(entry.id).toLowerCase().includes(query) ||
      entry.description.toLowerCase().includes(query)
    )
  }, [changelistQuery, changelistEntries])

  async function selectBranch(branchName: string): Promise<void> {
    setOpenMenu(null)
    if (variant === 'welcome' && mode === 'worktree') {
      onBaseRefChange?.(branchName)
      onWorktreeBranchNameChange?.(null)
      return
    }

    setBusy(true)
    try {
      await window.api.git.checkoutBranch(branchActionPath, branchName)
      await loadBranches({ force: true })
      addToast(t('workspaceFooter.branchCheckedOut', { branch: branchName }), 'success')
    } catch (err) {
      addToast(t('workspaceFooter.branchFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    } finally {
      setBusy(false)
    }
  }

  async function selectWelcomeProject(project: WorkspaceProjectSummary): Promise<void> {
    setOpenMenu(null)
    if (projectIdentity(project) === selectedProjectId) return
    if (project.kind === 'remote') return
    await onWelcomeWorkspaceChange?.(project.path)
  }

  async function createBranch(): Promise<void> {
    const error = branchNameError(branchDraft, t)
    if (error) return
    const branch = normalizeBranchName(branchDraft)
    setBusy(true)
    try {
      if (variant === 'welcome' && mode === 'worktree') {
        onWorktreeBranchNameChange?.(branch)
        setCreateOpen(false)
        setOpenMenu(null)
        addToast(t('workspaceFooter.worktreeBranchSelected', { branch }), 'success')
        return
      }

      await window.api.git.createAndCheckoutBranch(branchActionPath, branch)
      await loadBranches({ force: true })
      setCreateOpen(false)
      setOpenMenu(null)
      addToast(t('workspaceFooter.branchCreated', { branch }), 'success')
    } catch (err) {
      addToast(t('workspaceFooter.branchFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    } finally {
      setBusy(false)
    }
  }

  async function selectChangelist(changelist: string): Promise<void> {
    if (isPerforceWelcome) {
      setOpenMenu(null)
      onWelcomeChangelistChange?.(changelist)
      return
    }
    if (!thread?.id) return
    setOpenMenu(null)
    setBusy(true)
    try {
      await usePerforceChangelistStore.getState().setTarget(thread.id, changelist)
      addToast(t('workspaceFooter.changelistSelected', { changelist: changelistLabel(changelist) }), 'success')
    } catch (err) {
      addToast(t('workspaceFooter.changelistFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    } finally {
      setBusy(false)
    }
  }

  async function createChangelist(): Promise<void> {
    if (isPerforceWelcome) {
      setBusy(true)
      try {
        // No threadId → the AppServer creates the changelist on the foreground workspace; we
        // carry the id as the welcome pre-selection until the first thread is created.
        const result = await window.api.appServer.sendRequest(
          'sourceControl/changelist/create',
          { description: changelistDraft, setAsTarget: true },
          30_000
        ) as { changelist: PerforceChangelistEntry }
        setWelcomeChangelists((prev) => [...prev.filter((entry) => entry.id !== result.changelist.id), result.changelist])
        onWelcomeChangelistChange?.(result.changelist.id)
        setCreateChangelistOpen(false)
        setOpenMenu(null)
        setChangelistDraft('')
        addToast(t('workspaceFooter.changelistCreated', { changelist: changelistLabel(result.changelist.id) }), 'success')
      } catch (err) {
        addToast(t('workspaceFooter.changelistFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
      } finally {
        setBusy(false)
      }
      return
    }
    if (!thread?.id) return
    setBusy(true)
    try {
      const created = await usePerforceChangelistStore.getState().createChangelist(thread.id, changelistDraft)
      await usePerforceChangelistStore.getState().ensure(thread.id, { force: true })
      setCreateChangelistOpen(false)
      setOpenMenu(null)
      setChangelistDraft('')
      addToast(t('workspaceFooter.changelistCreated', { changelist: changelistLabel(created.id) }), 'success')
    } catch (err) {
      addToast(t('workspaceFooter.changelistFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    } finally {
      setBusy(false)
    }
  }

  const handoffDialog = variant === 'thread' && handoffMode && thread ? (
    <WorktreeHandoffDialog
      key="handoff-dialog"
      mode={handoffMode}
      thread={thread}
      baseRef={currentBranchLabel(branches)}
      defaultBranchName={defaultWorktreeBranchName(localWorkspacePath)}
      localWorkspacePath={localWorkspacePath}
      disabledReason={handoffDisabledReason}
      onBusyChange={setBusy}
      onClose={() => setHandoffMode(null)}
      onComplete={() => { void loadBranches({ force: true }) }}
    />
  ) : null
  const showGitFooterControls = !foregroundIsChat && !remoteWorkspace && Boolean(branchActionPath) && gitAvailability !== 'unavailable'
  const showPerforceFooterControls = (isPerforceWorkspace && Boolean(thread?.id)) || isPerforceWelcome
  // A remote AppServer's Hub is not this Desktop's, so its satellites are not reachable.
  const showRunOn = runOnVisible && !remoteWorkspace && !foregroundIsChat
  const workLocationBranch = mode === 'local' ? currentBranchLabel(branches) : null
  // Empty until the branch is known, so the pill is not remounted when it arrives.
  const workLocationTooltip = workLocationBranch
    ? t('workspaceFooter.workLocally.tooltip', { branch: workLocationBranch })
    : ''

  return (
    <>
    {(showRunOn || showProjectSelector || showGitFooterControls || showPerforceFooterControls) && (
      <div ref={footerRef} style={footerStyle}>
      {showProjectSelector && (
        <div style={{ position: 'relative' }}>
          <WorkspaceFooterPill
            disabled={busy}
            open={openMenu === 'project'}
            onClick={() => setOpenMenu(openMenu === 'project' ? null : 'project')}
          >
            {selectedProject ? projectIcon(selectedProject) : <Folder size={15} strokeWidth={1.8} aria-hidden />}
            <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {selectedProject?.name || workspaceSlug(workspacePath)}
            </span>
            <ChevronDown size={14} strokeWidth={1.8} aria-hidden />
          </WorkspaceFooterPill>
          {openMenu === 'project' && (
            <div ref={projectMenuRef} style={menuStyle}>
              <ComposerOverlapBand height={projectBandHeight} radius={10} />
              <FooterMenuSearchField
                value={projectQuery}
                placeholder={t('workspaceFooter.searchProjects')}
                onChange={setProjectQuery}
              />
              <div style={{ maxHeight: '220px', overflowY: 'auto', padding: '4px 0' }}>
                {filteredProjects.length === 0 ? (
                  <div style={{ padding: '8px', color: 'var(--text-dimmed)' }}>{t('workspaceFooter.noProjects')}</div>
                ) : filteredProjects.map((project) => {
                  const checked = projectIdentity(project) === selectedProjectId
                  return (
                    <FooterMenuButton
                      key={projectIdentity(project)}
                      icon={projectIcon(project)}
                      checked={checked}
                      disabled={busy || (project.kind === 'remote' && !checked)}
                      onClick={() => { void selectWelcomeProject(project) }}
                    >
                      <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {project.name || workspaceSlug(project.path)}
                      </span>
                    </FooterMenuButton>
                  )
                })}
              </div>
              <FooterMenuDivider />
              <FooterMenuButton
                icon={<FolderPlus size={15} strokeWidth={1.8} aria-hidden />}
                disabled={addProject.busy}
                onClick={() => { setOpenMenu(null); addProject.beginCreate() }}
              >
                <span style={{ flex: 1 }}>{t('addProject.addNew')}</span>
              </FooterMenuButton>
            </div>
          )}
          {addProject.dialog}
        </div>
      )}
      {showRunOn && (
        <RunOnPicker
          threadId={thread?.id}
          workspacePath={workspacePath}
          disabled={turnRunning}
          onOpenChange={(open) => setOpenMenu(open ? 'runOn' : null)}
        />
      )}
      {showGitFooterControls && (
        <>
      <div style={{ position: 'relative' }}>
        <ActionTooltip label={workLocationTooltip} placement="top">
          <WorkspaceFooterPill
            disabled={busy || !branchControlsReady}
            open={openMenu === 'workspace'}
            onClick={() => setOpenMenu(openMenu === 'workspace' ? null : 'workspace')}
          >
            <Laptop size={15} strokeWidth={1.8} aria-hidden />
            <span>{locationLabel}</span>
            <ChevronDown size={14} strokeWidth={1.8} aria-hidden />
          </WorkspaceFooterPill>
        </ActionTooltip>
        {openMenu === 'workspace' && (
          <div ref={workspaceMenuRef} style={menuStyle}>
            <ComposerOverlapBand height={workspaceBandHeight} radius={10} />
            {showBranchHandoffOnly ? (
              <WorkspaceMenuItem
                label={t('workspaceFooter.handoffToBranch')}
                icon={<ArrowRightLeft size={14} strokeWidth={1.8} aria-hidden />}
                checked={false}
                disabled={busy || !branchControlsReady}
                onClick={() => {
                  setHandoffMode('local')
                  setOpenMenu(null)
                }}
              />
            ) : (
              <>
                <WorkspaceMenuItem
                  label={t('workspaceFooter.workLocally')}
                  icon={<Laptop size={14} strokeWidth={1.8} aria-hidden />}
                  checked={mode === 'local'}
                  disabled={busy || !branchControlsReady}
                  onClick={() => {
                    if (variant === 'welcome') onWelcomeModeChange?.('local')
                    setOpenMenu(null)
                  }}
                />
                <WorkspaceMenuItem
                  label={variant === 'welcome' ? t('workspaceFooter.newWorktree') : t('workspaceFooter.handoffToWorktree')}
                  icon={variant === 'welcome'
                    ? <FolderPlus size={14} strokeWidth={1.8} aria-hidden />
                    : <ArrowRightLeft size={14} strokeWidth={1.8} aria-hidden />}
                  checked={mode === 'worktree'}
                  disabled={!canUseWorktrees || busy}
                  onClick={() => {
                    if (variant === 'welcome') onWelcomeModeChange?.('worktree')
                    else if (mode !== 'worktree') setHandoffMode('worktree')
                    setOpenMenu(null)
                  }}
                />
              </>
            )}
          </div>
        )}
      </div>

      <div style={{ position: 'relative' }}>
        <WorkspaceFooterPill
          disabled={busy || !branchActionPath || !branchControlsReady}
          open={openMenu === 'branch'}
          onClick={() => setOpenMenu(openMenu === 'branch' ? null : 'branch')}
        >
          <GitBranch size={15} strokeWidth={1.8} aria-hidden />
          <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{branchLabel}</span>
          <ChevronDown size={14} strokeWidth={1.8} aria-hidden />
        </WorkspaceFooterPill>
        {openMenu === 'branch' && (
          <div ref={branchMenuRef} style={{ ...menuStyle, width: '320px' }}>
            <ComposerOverlapBand height={branchBandHeight} radius={10} />
            <FooterMenuSearchField
              value={branchQuery}
              placeholder={t('workspaceFooter.searchBranches')}
              onChange={setBranchQuery}
            />
            <div style={{ maxHeight: '220px', overflowY: 'auto', padding: '4px 0' }}>
              {filteredBranches.length === 0 ? (
                <div style={{ padding: '8px', color: 'var(--text-dimmed)' }}>{t('workspaceFooter.noBranches')}</div>
              ) : filteredBranches.map((branch) => {
                const checked = variant === 'welcome' && mode === 'worktree'
                  ? selectedBaseRef === branch.name && !worktreeBranchName
                  : branch.current
                return (
                  <FooterMenuButton
                    key={branch.name}
                    icon={<GitBranch size={14} strokeWidth={1.8} aria-hidden />}
                    checked={checked}
                    onClick={() => { void selectBranch(branch.name) }}
                  >
                    <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{branch.name}</span>
                  </FooterMenuButton>
                )
              })}
            </div>
            <FooterMenuDivider />
            <FooterMenuButton
              icon={<Plus size={15} strokeWidth={1.8} aria-hidden />}
              onClick={() => setCreateOpen(true)}
            >
              <span>{variant === 'welcome' && mode === 'worktree' ? t('workspaceFooter.createWorktreeBranch') : t('workspaceFooter.createCheckoutBranch')}</span>
            </FooterMenuButton>
          </div>
        )}
      </div>

      {createOpen && createPortal(
        <CreateBranchDialog
          value={branchDraft}
          busy={busy}
          title={variant === 'welcome' && mode === 'worktree'
            ? t('workspaceFooter.createWorktreeBranchTitle')
            : t('workspaceFooter.createCheckoutBranchTitle')}
          confirmLabel={variant === 'welcome' && mode === 'worktree'
            ? t('workspaceFooter.create')
            : t('workspaceFooter.createAndCheckout')}
          onChange={setBranchDraft}
          onCancel={() => setCreateOpen(false)}
          onConfirm={() => { void createBranch() }}
        />,
        document.body
      )}
        </>
      )}
      {showPerforceFooterControls && (
        <div style={{ position: 'relative' }}>
          <WorkspaceFooterPill
            disabled={busy}
            open={openMenu === 'changelist'}
            onClick={() => setOpenMenu(openMenu === 'changelist' ? null : 'changelist')}
          >
            <ListChecks size={15} strokeWidth={1.8} aria-hidden />
            <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {changelistLabel(selectedChangelist)}
            </span>
            <ChevronDown size={14} strokeWidth={1.8} aria-hidden />
          </WorkspaceFooterPill>
          {openMenu === 'changelist' && (
            <div ref={changelistMenuRef} style={{ ...menuStyle, width: '320px' }}>
              <ComposerOverlapBand height={changelistBandHeight} radius={10} />
              <FooterMenuSearchField
                value={changelistQuery}
                placeholder={t('workspaceFooter.searchChangelists')}
                onChange={setChangelistQuery}
              />
              <div style={{ maxHeight: '220px', overflowY: 'auto', padding: '4px 0' }}>
                {filteredChangelists.length === 0 ? (
                  <div style={{ padding: '8px', color: 'var(--text-dimmed)' }}>
                    {changelistControlsReady ? t('workspaceFooter.noChangelists') : t('workspaceFooter.loadingChangelists')}
                  </div>
                ) : filteredChangelists.map((entry) => (
                  <FooterMenuButton
                    key={entry.id}
                    icon={<ListChecks size={14} strokeWidth={1.8} aria-hidden />}
                    checked={selectedChangelist === entry.id}
                    disabled={busy}
                    onClick={() => { void selectChangelist(entry.id) }}
                  >
                    <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {changelistLabel(entry.id)}
                      {entry.description ? (
                        <span style={{ color: 'var(--text-dimmed)' }}> · {entry.description}</span>
                      ) : null}
                    </span>
                  </FooterMenuButton>
                ))}
              </div>
              <FooterMenuDivider />
              <FooterMenuButton
                icon={<Plus size={15} strokeWidth={1.8} aria-hidden />}
                onClick={() => setCreateChangelistOpen(true)}
              >
                <span>{t('workspaceFooter.createChangelist')}</span>
              </FooterMenuButton>
            </div>
          )}
        </div>
      )}
      {createChangelistOpen && createPortal(
        <CreateChangelistDialog
          value={changelistDraft}
          busy={busy}
          onChange={setChangelistDraft}
          onCancel={() => setCreateChangelistOpen(false)}
          onConfirm={() => { void createChangelist() }}
        />,
        document.body
      )}
      </div>
    )}
    {handoffDialog}
    </>
  )
}
