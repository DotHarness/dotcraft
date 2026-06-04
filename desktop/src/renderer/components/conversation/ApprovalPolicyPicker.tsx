import { useCallback, useEffect, useId, useMemo, useRef, useState, type CSSProperties } from 'react'
import { Hand, OctagonAlert } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import type { ApprovalPolicyWire, ThreadConfigurationWire } from '../../types/thread'
import { ActionTooltip } from '../ui/ActionTooltip'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import {
  composerFooterControlActiveBackground,
  composerFooterControlBoxStyle,
  composerFooterControlHoverBackground,
  composerModelPillStyle
} from './ComposerShell'

export type VisibleApprovalPolicy = 'default' | 'autoApprove'

interface ApprovalPolicyPickerProps {
  threadId?: string
  value?: VisibleApprovalPolicy
  onChange?: (next: VisibleApprovalPolicy) => void
  disabled?: boolean
  workspaceDefault?: VisibleApprovalPolicy
}

interface WorkspaceCoreConfigWithApproval {
  workspace?: {
    defaultApprovalPolicy?: VisibleApprovalPolicy | null
  }
  userDefaults?: {
    defaultApprovalPolicy?: VisibleApprovalPolicy | null
  }
}

function normalizeVisiblePolicy(value: unknown): VisibleApprovalPolicy {
  return value === 'autoApprove' ? 'autoApprove' : 'default'
}

function setCaseInsensitiveField(target: Record<string, unknown>, key: string, value: unknown): void {
  const lower = key.toLowerCase()
  const existingKey = Object.keys(target).find((candidate) => candidate.toLowerCase() === lower)
  target[existingKey ?? key] = value
}

const OPTIONS: VisibleApprovalPolicy[] = ['default', 'autoApprove']

export function ApprovalPolicyPicker({
  threadId,
  value: controlledValue,
  onChange,
  disabled = false,
  workspaceDefault: workspaceDefaultOverride
}: ApprovalPolicyPickerProps): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const activeThread = useThreadStore((s) => s.activeThread)
  const setActiveThread = useThreadStore((s) => s.setActiveThread)
  const [open, setOpen] = useState(false)
  const [highlight, setHighlight] = useState(0)
  const [saving, setSaving] = useState(false)
  const [triggerActive, setTriggerActive] = useState(false)
  const [workspaceDefault, setWorkspaceDefault] = useState<VisibleApprovalPolicy>('default')
  const wrapRef = useRef<HTMLDivElement>(null)
  const listId = useId()

  const value = useMemo(
    () => controlledValue ?? normalizeVisiblePolicy(activeThread?.configuration?.approvalPolicy),
    [activeThread?.configuration?.approvalPolicy, controlledValue]
  )
  const selectedIndex = Math.max(0, OPTIONS.findIndex((option) => option === value))
  const interactive = !disabled && !saving

  useEffect(() => {
    setHighlight(selectedIndex)
  }, [selectedIndex])

  useEffect(() => {
    if (workspaceDefaultOverride != null) {
      setWorkspaceDefault(workspaceDefaultOverride)
      return
    }

    let disposed = false
    const loadWorkspaceDefault = async (): Promise<void> => {
      try {
        const result = await window.api.workspaceConfig.getCore() as WorkspaceCoreConfigWithApproval
        if (disposed) return
        const workspacePolicy = result.workspace?.defaultApprovalPolicy
        const userPolicy = result.userDefaults?.defaultApprovalPolicy
        setWorkspaceDefault(normalizeVisiblePolicy(workspacePolicy ?? userPolicy))
      } catch {
        if (!disposed) setWorkspaceDefault('default')
      }
    }
    void loadWorkspaceDefault()
    return () => {
      disposed = true
    }
  }, [workspaceDefaultOverride])

  useEffect(() => {
    if (!open) return
    const handlePointerDown = (event: MouseEvent): void => {
      if (!wrapRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        setOpen(false)
        return
      }
      if (!interactive) return
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        setHighlight((current) => Math.min(OPTIONS.length - 1, current + 1))
        return
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault()
        setHighlight((current) => Math.max(0, current - 1))
        return
      }
      if (event.key === 'Enter') {
        event.preventDefault()
        const next = OPTIONS[highlight]
        if (next) {
          void applyPolicy(next)
        }
      }
    }
    window.addEventListener('mousedown', handlePointerDown, true)
    window.addEventListener('keydown', handleKeyDown, true)
    return () => {
      window.removeEventListener('mousedown', handlePointerDown, true)
      window.removeEventListener('keydown', handleKeyDown, true)
    }
  })

  const applyPolicy = useCallback(
    async (nextPolicy: VisibleApprovalPolicy): Promise<void> => {
      if (nextPolicy === value || saving || disabled) return

      if (nextPolicy === 'autoApprove') {
        const confirmed = await confirm({
          title: t('settings.permissions.fullAccess.warningTitle'),
          message: t('settings.permissions.fullAccess.warningBody'),
          confirmLabel: t('settings.permissions.fullAccess.warningConfirm'),
          cancelLabel: t('common.cancel'),
          danger: true
        })
        if (!confirmed) return
      }

      if (onChange) {
        onChange(nextPolicy)
        setOpen(false)
        return
      }

      if (!threadId || !activeThread || activeThread.id !== threadId) return

      setSaving(true)
      const previous = useThreadStore.getState().activeThread
      try {
        const readRes = (await window.api.appServer.sendRequest('thread/read', {
          threadId,
          includeTurns: false
        })) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        const existingConfig =
          readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
            ? { ...(readRes.thread.configuration as Record<string, unknown>) }
            : {}
        setCaseInsensitiveField(existingConfig, 'approvalPolicy', nextPolicy)

        const active = useThreadStore.getState().activeThread
        if (active && active.id === threadId) {
          setActiveThread({
            ...active,
            configuration: {
              ...(active.configuration ?? {}),
              approvalPolicy: nextPolicy as ApprovalPolicyWire
            }
          })
        }

        await window.api.appServer.sendRequest('thread/config/update', {
          threadId,
          config: existingConfig
        })
        setOpen(false)
      } catch (error) {
        if (previous && previous.id === threadId) {
          setActiveThread(previous)
        }
        const message = error instanceof Error ? error.message : String(error)
        addToast(t('composer.approval.updateFailed', { error: message }), 'error')
      } finally {
        setSaving(false)
      }
    },
    [activeThread, confirm, disabled, onChange, saving, setActiveThread, t, threadId, value]
  )

  const workspaceDefaultHint =
    value === 'default'
      ? t('composer.approval.workspaceDefaultHint', {
        policy: workspaceDefault === 'autoApprove'
          ? t('composer.approval.fullAccess.label')
          : t('composer.approval.default.label')
      })
      : undefined
  const label = getPolicyLabel(t, value)
  const tooltipLabel = t('composer.approval.selectTitle')

  return (
    <div
      ref={wrapRef}
      style={{
        ...composerFooterControlBoxStyle,
        position: 'relative',
        minWidth: 0
      }}
      title={workspaceDefaultHint}
    >
      <ActionTooltip
        label={tooltipLabel}
        disabledReason={workspaceDefaultHint}
        placement="top"
        wrapperStyle={{ minWidth: 0 }}
      >
        <button
          type="button"
          data-testid="approval-policy-trigger"
          aria-label={tooltipLabel}
          aria-haspopup={interactive ? 'listbox' : undefined}
          aria-expanded={interactive ? open : undefined}
          aria-controls={interactive && open ? listId : undefined}
          disabled={!interactive}
          onMouseEnter={() => setTriggerActive(true)}
          onMouseLeave={() => setTriggerActive(false)}
          onFocus={(event) => {
            if (event.currentTarget.matches(':focus-visible')) setTriggerActive(true)
          }}
          onBlur={() => setTriggerActive(false)}
          onClick={() => {
            if (!interactive) return
            setOpen((current) => !current)
          }}
          style={{
            ...composerModelPillStyle(
              value === 'autoApprove' ? 'var(--warning)' : 'var(--composer-footer-text)',
              !interactive
            ),
            backgroundColor: interactive
              ? open
                ? composerFooterControlActiveBackground
                : triggerActive
                  ? composerFooterControlHoverBackground
                  : 'transparent'
              : 'transparent',
            cursor: interactive ? 'pointer' : 'default'
          }}
        >
          <PolicyIcon policy={value} />
          <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {label}
          </span>
          {interactive && <ChevronDown rotated={open} />}
        </button>
      </ActionTooltip>

      {interactive && open && (
        <div
          id={listId}
          role="listbox"
          aria-label={t('composer.approval.label')}
          style={popupStyle()}
        >
          {OPTIONS.map((option, index) => {
            const selected = option === value
            const highlighted = index === highlight
            return (
              <button
                key={option}
                type="button"
                role="option"
                data-testid={`approval-policy-option-${option}`}
                aria-selected={selected}
                onMouseEnter={() => {
                  setHighlight(index)
                }}
                onClick={() => {
                  void applyPolicy(option)
                }}
                style={optionStyle(highlighted, selected)}
              >
                <PolicyIcon policy={option} />
                <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {getPolicyLabel(t, option)}
                </span>
                {selected && <SelectedDot />}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}

function getPolicyLabel(t: ReturnType<typeof useT>, policy: VisibleApprovalPolicy): string {
  return policy === 'autoApprove'
    ? t('composer.approval.fullAccess.label')
    : t('composer.approval.default.label')
}

function PolicyIcon({ policy }: { policy: VisibleApprovalPolicy }): JSX.Element {
  const Icon = policy === 'autoApprove' ? OctagonAlert : Hand
  return (
    <span
      aria-hidden
      data-testid={`approval-policy-icon-${policy}`}
      style={{ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}
    >
      <Icon size={14} strokeWidth={1.9} />
    </span>
  )
}

function ChevronDown({ rotated }: { rotated: boolean }): JSX.Element {
  return (
    <span
      aria-hidden
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: '14px',
        height: '14px',
        flexShrink: 0,
        transform: rotated ? 'rotate(180deg)' : 'none',
        transition: 'transform 120ms ease'
      }}
    >
      <svg width="10" height="10" viewBox="0 0 12 12" fill="none">
        <path
          d="M3 4.5L6 7.5L9 4.5"
          stroke="currentColor"
          strokeWidth="1.7"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
    </span>
  )
}

function SelectedDot(): JSX.Element {
  return (
    <span
      aria-hidden
      style={{
        width: '7px',
        height: '7px',
        borderRadius: '999px',
        background: 'var(--accent)',
        flexShrink: 0,
        marginLeft: 'auto'
      }}
    />
  )
}

function popupStyle(): CSSProperties {
  return {
    position: 'absolute',
    left: 0,
    bottom: 'calc(100% + 8px)',
    minWidth: '220px',
    maxWidth: '280px',
    zIndex: 70,
    border: 'none',
    borderRadius: '12px',
    background: 'var(--glass-surface-strong)',
    boxShadow: 'var(--glass-shadow-soft)',
    backdropFilter: 'var(--glass-blur)',
    WebkitBackdropFilter: 'var(--glass-blur)',
    padding: '6px'
  }
}

function optionStyle(highlighted: boolean, selected: boolean): CSSProperties {
  return {
    width: '100%',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '10px',
    border: 'none',
    borderRadius: '10px',
    padding: '8px 10px',
    background: highlighted ? 'var(--bg-tertiary)' : 'transparent',
    color: selected ? 'var(--text-primary)' : 'var(--text-secondary)',
    cursor: 'pointer',
    textAlign: 'left',
    fontSize: 'var(--type-secondary-size)',
    lineHeight: 'var(--type-secondary-line-height)'
  }
}
