import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type FocusEvent, type JSX, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ArrowRightLeft, Check, ChevronDown, FolderPlus, GitBranch, Laptop, Plus, Search } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { normalizeGitPathKey, useGitStore, type GitBranchListSnapshot } from '../../stores/gitStore'
import { addToast } from '../../stores/toastStore'
import type { Thread } from '../../types/thread'
import { WorktreeHandoffDialog } from './WorktreeHandoffDialog'

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
}

type OpenMenu = 'workspace' | 'branch' | null

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

const pillStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  height: '28px',
  maxWidth: '240px',
  padding: '0 8px',
  border: 'none',
  borderRadius: '999px',
  background: 'transparent',
  color: 'var(--composer-footer-text)',
  font: 'inherit',
  cursor: 'pointer',
  transition: 'background 120ms ease, color 120ms ease, box-shadow 120ms ease, transform 120ms ease'
}

const menuStyle: CSSProperties = {
  position: 'absolute',
  left: 0,
  bottom: 'calc(100% + 6px)',
  zIndex: 100,
  width: '280px',
  padding: '8px',
  borderRadius: '10px',
  background: 'var(--glass-surface-strong)',
  border: 'none',
  boxShadow: 'var(--glass-shadow-soft)',
  backdropFilter: 'var(--glass-blur)',
  WebkitBackdropFilter: 'var(--glass-blur)',
  color: 'var(--text-primary)'
}

const menuButtonStyle: CSSProperties = {
  width: '100%',
  minHeight: '32px',
  border: 'none',
  borderRadius: '6px',
  background: 'transparent',
  color: 'inherit',
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  padding: '0 8px',
  font: 'inherit',
  cursor: 'pointer',
  textAlign: 'left',
  transition: 'background 120ms ease, color 120ms ease, box-shadow 120ms ease, transform 120ms ease'
}

function currentBranchLabel(branches: GitBranchListSnapshot | null): string | null {
  return branches?.current || branches?.detachedHead || null
}

function normalizeBranchName(value: string): string {
  return value.trim().replace(/^\/+/, '')
}

function branchNameError(value: string, t: ReturnType<typeof useT>): string | null {
  const branch = normalizeBranchName(value)
  if (!branch) return t('workspaceFooter.branchRequired')
  if (branch.endsWith('/')) return t('workspaceFooter.branchCannotEndSlash')
  return null
}

interface InteractiveState {
  hovered: boolean
  pressed: boolean
  focusVisible: boolean
}

function useInteractiveState(disabled = false): {
  state: InteractiveState
  eventHandlers: {
    onPointerEnter: () => void
    onPointerLeave: () => void
    onPointerDown: () => void
    onPointerUp: () => void
    onPointerCancel: () => void
    onFocus: (event: FocusEvent<HTMLButtonElement>) => void
    onBlur: () => void
  }
} {
  const [state, setState] = useState<InteractiveState>({
    hovered: false,
    pressed: false,
    focusVisible: false
  })

  return {
    state,
    eventHandlers: {
      onPointerEnter: () => {
        if (!disabled) setState((current) => ({ ...current, hovered: true }))
      },
      onPointerLeave: () => {
        setState((current) => ({ ...current, hovered: false, pressed: false }))
      },
      onPointerDown: () => {
        if (!disabled) setState((current) => ({ ...current, pressed: true }))
      },
      onPointerUp: () => {
        setState((current) => ({ ...current, pressed: false }))
      },
      onPointerCancel: () => {
        setState((current) => ({ ...current, pressed: false }))
      },
      onFocus: (event) => {
        if (!disabled && event.currentTarget.matches(':focus-visible')) {
          setState((current) => ({ ...current, focusVisible: true }))
        }
      },
      onBlur: () => {
        setState((current) => ({ ...current, focusVisible: false, pressed: false }))
      }
    }
  }
}

function interactiveStyle(
  state: InteractiveState,
  options: {
    active?: boolean
    disabled?: boolean
  } = {}
): CSSProperties {
  if (options.disabled) {
    return {
      opacity: 0.45,
      cursor: 'default',
      transform: 'none',
      boxShadow: 'none'
    }
  }

  const highlighted = options.active === true || state.hovered || state.focusVisible
  const background = state.pressed
    ? 'var(--bg-active)'
    : highlighted
      ? 'var(--bg-tertiary)'
      : 'transparent'

  return {
    background,
    color: highlighted ? 'var(--text-primary)' : undefined,
    boxShadow: state.focusVisible
      ? '0 0 0 2px color-mix(in srgb, var(--accent) 55%, transparent)'
      : 'none',
    transform: state.pressed ? 'translateY(1px)' : 'none'
  }
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
  onWorktreeBranchNameChange
}: ComposerWorkspaceFooterProps): JSX.Element | null {
  const t = useT()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const [openMenu, setOpenMenu] = useState<OpenMenu>(null)
  const [branchQuery, setBranchQuery] = useState('')
  const [busy, setBusy] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  const [handoffMode, setHandoffMode] = useState<ComposerWorkspaceMode | null>(null)
  const [branchDraft, setBranchDraft] = useState('dotcraft/')
  const footerRef = useRef<HTMLDivElement>(null)
  const isThread = variant === 'thread'
  const threadBusy = thread?.runtime?.busy === true
    || thread?.runtime?.running === true
    || thread?.runtime?.waitingOnApproval === true
    || thread?.runtime?.waitingOnInput === true
    || Boolean(thread?.runtime?.maintenanceKind)
  const branchActionPath = workspacePath.trim()
  const branchActionPathKey = normalizeGitPathKey(branchActionPath)
  const gitPathState = useGitStore((s) =>
    branchActionPathKey ? s.branchesByPath[branchActionPathKey] : undefined
  )
  const gitAvailability = remoteWorkspace || !branchActionPath
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

  useEffect(() => {
    function closeOnOutsideClick(event: MouseEvent): void {
      if (!footerRef.current?.contains(event.target as Node)) setOpenMenu(null)
    }
    document.addEventListener('mousedown', closeOnOutsideClick)
    return () => document.removeEventListener('mousedown', closeOnOutsideClick)
  }, [])

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
    if (remoteWorkspace || !branchActionPath) {
      hideForUnavailableGit()
      return
    }
    await useGitStore.getState().ensureBranches(branchActionPath, { force: options.force })
  }, [branchActionPath, hideForUnavailableGit, remoteWorkspace])

  useEffect(() => {
    if (remoteWorkspace || !branchActionPath) {
      hideForUnavailableGit()
      return
    }
    void loadBranches()
  }, [branchActionPath, hideForUnavailableGit, loadBranches, remoteWorkspace])

  useEffect(() => {
    if (gitAvailability !== 'unavailable') return
    hideForUnavailableGit()
  }, [gitAvailability, hideForUnavailableGit])

  useEffect(() => {
    if (variant !== 'welcome' || mode !== 'worktree' || baseRef || !branches) return
    onBaseRefChange?.(currentBranchLabel(branches))
  }, [baseRef, branches, mode, onBaseRefChange, variant])

  useEffect(() => {
    if (remoteWorkspace || !branchActionPath || gitAvailability !== 'available') return
    const timer = window.setInterval(() => {
      void loadBranches({ force: true })
    }, GIT_BRANCH_REFRESH_INTERVAL_MS)
    return () => window.clearInterval(timer)
  }, [branchActionPath, gitAvailability, loadBranches, remoteWorkspace])

  const filteredBranches = useMemo(() => {
    const query = branchQuery.trim().toLowerCase()
    const values = branches?.branches ?? []
    if (!query) return values
    return values.filter((branch) => branch.name.toLowerCase().includes(query))
  }, [branchQuery, branches])

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

  const handoffDialog = variant === 'thread' && handoffMode && thread ? (
    <WorktreeHandoffDialog
      key="handoff-dialog"
      mode={handoffMode}
      thread={thread}
      baseRef={currentBranchLabel(branches)}
      defaultBranchName={defaultWorktreeBranchName(localWorkspacePath)}
      localWorkspacePath={localWorkspacePath}
      onBusyChange={setBusy}
      onClose={() => setHandoffMode(null)}
      onComplete={() => { void loadBranches({ force: true }) }}
    />
  ) : null
  const showFooterControls = !remoteWorkspace && Boolean(branchActionPath) && gitAvailability !== 'unavailable'

  return (
    <>
    {showFooterControls && (
      <div ref={footerRef} style={footerStyle}>
      <div style={{ position: 'relative' }}>
        <WorkspaceFooterPill
          disabled={busy || !branchControlsReady || (isThread && threadBusy)}
          open={openMenu === 'workspace'}
          onClick={() => setOpenMenu(openMenu === 'workspace' ? null : 'workspace')}
        >
          <Laptop size={15} strokeWidth={1.8} aria-hidden />
          <span>{locationLabel}</span>
          <ChevronDown size={14} strokeWidth={1.8} aria-hidden />
        </WorkspaceFooterPill>
        {openMenu === 'workspace' && (
          <div style={menuStyle}>
            {showBranchHandoffOnly ? (
              <WorkspaceMenuItem
                label={t('workspaceFooter.handoffToBranch')}
                icon={<ArrowRightLeft size={14} strokeWidth={1.8} aria-hidden />}
                checked={false}
                disabled={busy || !branchControlsReady || (isThread && threadBusy)}
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
                  disabled={busy || !branchControlsReady || (isThread && threadBusy)}
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
                  disabled={!canUseWorktrees || busy || (isThread && threadBusy)}
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
          <div style={{ ...menuStyle, width: '320px' }}>
            <div style={{
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
              height: '32px',
              padding: '0 8px',
              color: 'var(--text-dimmed)'
            }}>
              <Search size={14} strokeWidth={1.8} aria-hidden />
              <input
                value={branchQuery}
                onChange={(e) => setBranchQuery(e.target.value)}
                placeholder={t('workspaceFooter.searchBranches')}
                style={{
                  flex: 1,
                  minWidth: 0,
                  border: 'none',
                  outline: 'none',
                  background: 'transparent',
                  color: 'var(--text-primary)',
                  font: 'inherit'
                }}
              />
            </div>
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
            <div
              style={{
                height: '1px',
                background: 'color-mix(in srgb, var(--text-primary) 9%, transparent)',
                margin: '6px 8px'
              }}
            />
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
      </div>
    )}
    {handoffDialog}
    </>
  )
}

function WorkspaceFooterPill({
  children,
  disabled,
  open,
  onClick
}: {
  children: ReactNode
  disabled?: boolean
  open?: boolean
  onClick: () => void
}): JSX.Element {
  const { state, eventHandlers } = useInteractiveState(disabled)
  return (
    <button
      type="button"
      style={{
        ...pillStyle,
        ...interactiveStyle(state, {
          active: open,
          disabled
        })
      }}
      disabled={disabled}
      onClick={onClick}
      {...eventHandlers}
    >
      {children}
    </button>
  )
}

function FooterMenuButton({
  children,
  icon,
  checked,
  disabled,
  onClick
}: {
  children: ReactNode
  icon: JSX.Element
  checked?: boolean
  disabled?: boolean
  onClick: () => void
}): JSX.Element {
  const { state, eventHandlers } = useInteractiveState(disabled)
  return (
    <button
      type="button"
      style={{
        ...menuButtonStyle,
        ...interactiveStyle(state, { disabled })
      }}
      disabled={disabled}
      onClick={onClick}
      {...eventHandlers}
    >
      {icon}
      {children}
      {checked && <Check size={15} strokeWidth={1.8} aria-hidden />}
    </button>
  )
}

function WorkspaceMenuItem({
  label,
  icon,
  checked,
  disabled,
  onClick
}: {
  label: string
  icon: JSX.Element
  checked: boolean
  disabled?: boolean
  onClick: () => void
}): JSX.Element {
  return (
    <FooterMenuButton
      icon={icon}
      checked={checked}
      disabled={disabled}
      onClick={onClick}
    >
      <span style={{ flex: 1 }}>{label}</span>
    </FooterMenuButton>
  )
}

function CreateBranchDialog({
  value,
  busy,
  title,
  confirmLabel,
  onChange,
  onCancel,
  onConfirm
}: {
  value: string
  busy: boolean
  title: string
  confirmLabel: string
  onChange: (value: string) => void
  onCancel: () => void
  onConfirm: () => void
}): JSX.Element {
  const t = useT()
  const error = branchNameError(value, t)

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onCancel()
      if (event.key === 'Enter' && !error && !busy) onConfirm()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [busy, error, onCancel, onConfirm])

  return (
    <div
      role="dialog"
      aria-modal="true"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--overlay-scrim)'
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onCancel()
      }}
    >
      <div
        style={{
          width: '420px',
          maxWidth: 'calc(100vw - 48px)',
          padding: '22px',
          borderRadius: '10px',
          background: 'var(--bg-secondary)',
          boxShadow: 'var(--shadow-level-3)'
        }}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h2 style={{ margin: '0 0 16px', fontSize: '18px', color: 'var(--text-primary)' }}>{title}</h2>
        <label style={{ display: 'grid', gap: '8px', color: 'var(--text-primary)', fontSize: '13px', fontWeight: 600 }}>
          {t('workspaceFooter.branchName')}
          <input
            value={value}
            autoFocus
            onChange={(e) => onChange(e.target.value)}
            style={{
              height: '42px',
              borderRadius: '8px',
              border: 'none',
              background: 'var(--bg-tertiary)',
              color: 'var(--text-primary)',
              padding: '0 12px',
              font: 'inherit',
              outline: 'none'
            }}
          />
        </label>
        {error && <div style={{ marginTop: '8px', color: 'var(--error)', fontSize: '12px' }}>{error}</div>}
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '20px' }}>
          <button type="button" onClick={onCancel} style={{ ...dialogButtonStyle, background: 'var(--bg-tertiary)', color: 'var(--text-primary)' }}>
            {t('workspaceFooter.close')}
          </button>
          <button
            type="button"
            disabled={Boolean(error) || busy}
            onClick={onConfirm}
            style={{
              ...dialogButtonStyle,
              background: 'var(--text-primary)',
              color: 'var(--bg-primary)',
              opacity: Boolean(error) || busy ? 0.55 : 1
            }}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}

const dialogButtonStyle: CSSProperties = {
  minWidth: '88px',
  height: '40px',
  border: 'none',
  borderRadius: '8px',
  padding: '0 16px',
  font: 'inherit',
  cursor: 'pointer'
}
