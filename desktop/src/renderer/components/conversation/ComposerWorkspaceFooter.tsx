import { useEffect, useMemo, useRef, useState, type CSSProperties, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { Check, ChevronDown, GitBranch, Laptop, Plus, Search, Shuffle } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import type { Thread } from '../../types/thread'

export type ComposerWorkspaceMode = 'local' | 'worktree'

interface BranchListResult {
  current: string | null
  detachedHead: string | null
  branches: Array<{ name: string; current: boolean }>
}

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
  outline: 'none'
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
  border: '1px solid var(--glass-border)',
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
  textAlign: 'left'
}

function currentBranchLabel(branches: BranchListResult | null): string | null {
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
  const [branches, setBranches] = useState<BranchListResult | null>(null)
  const [branchQuery, setBranchQuery] = useState('')
  const [busy, setBusy] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  const [branchDraft, setBranchDraft] = useState('dotcraft/')
  const footerRef = useRef<HTMLDivElement>(null)
  const canUseWorktrees = capabilities?.gitWorktrees === true && !remoteWorkspace
  const isThread = variant === 'thread'
  const threadBusy = thread?.runtime?.busy === true
    || thread?.runtime?.running === true
    || thread?.runtime?.waitingOnApproval === true
    || thread?.runtime?.waitingOnInput === true
    || Boolean(thread?.runtime?.maintenanceKind)
  const branchActionPath = workspacePath.trim()
  const selectedBaseRef = baseRef || currentBranchLabel(branches)
  const branchLabel = mode === 'worktree' && variant === 'welcome'
    ? (worktreeBranchName || selectedBaseRef || t('workspaceFooter.branchUnknown'))
    : (currentBranchLabel(branches) || t('workspaceFooter.branchUnknown'))
  const locationLabel = variant === 'welcome'
    ? (mode === 'worktree' ? t('workspaceFooter.newWorktree') : t('workspaceFooter.workLocally'))
    : (mode === 'worktree' ? t('workspaceFooter.worktree') : t('workspaceFooter.local'))

  useEffect(() => {
    function closeOnOutsideClick(event: MouseEvent): void {
      if (!footerRef.current?.contains(event.target as Node)) setOpenMenu(null)
    }
    document.addEventListener('mousedown', closeOnOutsideClick)
    return () => document.removeEventListener('mousedown', closeOnOutsideClick)
  }, [])

  const loadBranches = useMemo(() => async () => {
    if (remoteWorkspace || !branchActionPath) {
      setBranches(null)
      return
    }
    try {
      const next = await window.api.git.listBranches(branchActionPath)
      setBranches(next)
      if (variant === 'welcome' && mode === 'worktree' && !baseRef) {
        onBaseRefChange?.(currentBranchLabel(next))
      }
    } catch {
      setBranches(null)
    }
  }, [baseRef, branchActionPath, mode, onBaseRefChange, remoteWorkspace, variant])

  useEffect(() => {
    void loadBranches()
  }, [loadBranches])

  const filteredBranches = useMemo(() => {
    const query = branchQuery.trim().toLowerCase()
    const values = branches?.branches ?? []
    if (!query) return values
    return values.filter((branch) => branch.name.toLowerCase().includes(query))
  }, [branchQuery, branches])

  async function handoff(nextMode: ComposerWorkspaceMode): Promise<void> {
    if (!thread || busy || threadBusy || !canUseWorktrees) return
    setBusy(true)
    setOpenMenu(null)
    try {
      const result = await window.api.appServer.sendRequest(
        'thread/worktree/handoff',
        { threadId: thread.id, mode: nextMode },
        180_000
      ) as { thread?: Thread }
      if (result.thread) {
        useThreadStore.getState().upsertThreads([result.thread])
        if (useThreadStore.getState().activeThreadId === result.thread.id) {
          useThreadStore.getState().setActiveThread(result.thread)
        }
      }
      addToast(
        nextMode === 'worktree'
          ? t('workspaceFooter.handoffToWorktreeSuccess')
          : t('workspaceFooter.handoffToLocalSuccess'),
        'success'
      )
      await loadBranches()
    } catch (err) {
      addToast(t('workspaceFooter.handoffFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    } finally {
      setBusy(false)
    }
  }

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
      await loadBranches()
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
      await loadBranches()
      setCreateOpen(false)
      setOpenMenu(null)
      addToast(t('workspaceFooter.branchCreated', { branch }), 'success')
    } catch (err) {
      addToast(t('workspaceFooter.branchFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    } finally {
      setBusy(false)
    }
  }

  if (remoteWorkspace) return null

  return (
    <div ref={footerRef} style={footerStyle}>
      <div style={{ position: 'relative' }}>
        <button
          type="button"
          style={pillStyle}
          disabled={busy || (isThread && threadBusy)}
          onClick={() => setOpenMenu(openMenu === 'workspace' ? null : 'workspace')}
          onMouseEnter={(e) => { e.currentTarget.style.background = 'var(--bg-tertiary)' }}
          onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent' }}
        >
          <Laptop size={15} strokeWidth={1.8} aria-hidden />
          <span>{locationLabel}</span>
          <ChevronDown size={14} strokeWidth={1.8} aria-hidden />
        </button>
        {openMenu === 'workspace' && (
          <div style={menuStyle}>
            <WorkspaceMenuItem
              label={variant === 'thread' && mode === 'worktree' ? t('workspaceFooter.backToLocal') : t('workspaceFooter.workLocally')}
              checked={mode === 'local'}
              disabled={busy || (isThread && threadBusy)}
              onClick={() => {
                if (variant === 'welcome') onWelcomeModeChange?.('local')
                else void handoff('local')
                setOpenMenu(null)
              }}
            />
            <WorkspaceMenuItem
              label={variant === 'welcome' ? t('workspaceFooter.newWorktree') : t('workspaceFooter.handoffToWorktree')}
              checked={mode === 'worktree'}
              disabled={!canUseWorktrees || busy || (isThread && threadBusy)}
              onClick={() => {
                if (variant === 'welcome') onWelcomeModeChange?.('worktree')
                else void handoff('worktree')
                setOpenMenu(null)
              }}
            />
          </div>
        )}
      </div>

      <div style={{ position: 'relative' }}>
        <button
          type="button"
          style={pillStyle}
          disabled={busy || !branchActionPath}
          onClick={() => setOpenMenu(openMenu === 'branch' ? null : 'branch')}
          onMouseEnter={(e) => { e.currentTarget.style.background = 'var(--bg-tertiary)' }}
          onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent' }}
        >
          <GitBranch size={15} strokeWidth={1.8} aria-hidden />
          <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{branchLabel}</span>
          <ChevronDown size={14} strokeWidth={1.8} aria-hidden />
        </button>
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
                  <button
                    key={branch.name}
                    type="button"
                    style={menuButtonStyle}
                    onClick={() => { void selectBranch(branch.name) }}
                  >
                    <GitBranch size={14} strokeWidth={1.8} aria-hidden />
                    <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{branch.name}</span>
                    {checked && <Check size={15} strokeWidth={1.8} aria-hidden />}
                  </button>
                )
              })}
            </div>
            <div style={{ height: '1px', background: 'var(--border-subtle)', margin: '4px 0' }} />
            <button type="button" style={menuButtonStyle} onClick={() => setCreateOpen(true)}>
              <Plus size={15} strokeWidth={1.8} aria-hidden />
              <span>{variant === 'welcome' && mode === 'worktree' ? t('workspaceFooter.createWorktreeBranch') : t('workspaceFooter.createCheckoutBranch')}</span>
            </button>
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
  )
}

function WorkspaceMenuItem({
  label,
  checked,
  disabled,
  onClick
}: {
  label: string
  checked: boolean
  disabled?: boolean
  onClick: () => void
}): JSX.Element {
  return (
    <button
      type="button"
      style={{
        ...menuButtonStyle,
        opacity: disabled ? 0.45 : 1,
        cursor: disabled ? 'default' : 'pointer'
      }}
      disabled={disabled}
      onClick={onClick}
    >
      <Shuffle size={14} strokeWidth={1.8} aria-hidden />
      <span style={{ flex: 1 }}>{label}</span>
      {checked && <Check size={15} strokeWidth={1.8} aria-hidden />}
    </button>
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
              border: '1px solid var(--border-default)',
              background: 'var(--bg-primary)',
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
