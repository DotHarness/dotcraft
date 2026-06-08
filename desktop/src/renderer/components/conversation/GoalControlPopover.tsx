import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { CSSProperties } from 'react'
import { CheckCircle2, Pause, Play, RotateCcw, Target, Trash2, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ThreadGoal } from '../../types/thread'
import { formatGoalUsage } from '../../utils/threadGoal'

interface GoalControlPopoverProps {
  visible: boolean
  goal: ThreadGoal | null
  busy?: boolean
  onSetObjective: (objective: string) => Promise<boolean>
  onPause: () => Promise<boolean>
  onResume: () => Promise<boolean>
  onClear: () => Promise<boolean>
  onDismiss: () => void
}

export function GoalControlPopover({
  visible,
  goal,
  busy = false,
  onSetObjective,
  onPause,
  onResume,
  onClear,
  onDismiss
}: GoalControlPopoverProps): JSX.Element | null {
  const t = useT()
  const [objective, setObjective] = useState('')
  const [editing, setEditing] = useState(false)
  const inputRef = useRef<HTMLTextAreaElement | null>(null)

  useEffect(() => {
    if (!visible) return
    setEditing(!goal)
    setObjective(goal?.objective ?? '')
    window.setTimeout(() => inputRef.current?.focus(), 0)
  }, [goal, visible])

  useEffect(() => {
    if (!visible) return
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') onDismiss()
    }
    window.addEventListener('keydown', onKey)
    return () => {
      window.removeEventListener('keydown', onKey)
    }
  }, [onDismiss, visible])

  if (!visible) return null

  const usage = goal ? formatGoalUsage(goal) : ''
  const canSubmit = objective.trim().length > 0 && !busy
  const setLabel = goal ? t('goal.action.replace') : t('goal.action.set')

  const submitObjective = async (): Promise<void> => {
    if (!canSubmit) return
    const ok = await onSetObjective(objective.trim())
    if (ok) {
      setEditing(false)
      onDismiss()
    }
  }

  return createPortal(
    <div
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--overlay-scrim)'
      }}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onDismiss()
      }}
    >
    <div
      role="dialog"
      aria-modal="true"
      aria-label={t('goal.panel.title')}
      onMouseDown={(event) => event.stopPropagation()}
      style={{
        width: 'min(520px, calc(100vw - 48px))',
        padding: 22,
        borderRadius: 10,
        background: 'var(--bg-secondary)',
        boxShadow: 'var(--shadow-level-3)',
        color: 'var(--text-primary)'
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12 }}>
        <Target size={16} aria-hidden />
        <div style={{ fontSize: 15, fontWeight: 600, flex: 1 }}>
          {t('goal.panel.title')}
        </div>
        <button type="button" onClick={onDismiss} aria-label={t('goal.action.dismiss')} style={iconButtonStyle}>
          <X size={15} aria-hidden />
        </button>
      </div>

      {goal && !editing ? (
        <>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 8 }}>
            <span style={statusDotStyle(goal.status)} aria-hidden />
            <span style={{ fontSize: 12, color: 'var(--text-secondary)', fontWeight: 500 }}>
              {t(`goal.status.${goal.status}`)}
            </span>
            {usage && (
              <span style={{ fontSize: 12, color: 'var(--text-dimmed)', marginLeft: 'auto' }}>
                {usage}
              </span>
            )}
          </div>
          <ActionTooltip label={goal.objective} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}>
          <div
            style={{
              fontSize: 13,
              lineHeight: 1.45,
              color: 'var(--text-primary)',
              marginBottom: 14,
              maxHeight: 72,
              overflow: 'auto',
              overflowWrap: 'anywhere',
              display: 'block'
            }}
          >
            {goal.objective}
          </div>
          </ActionTooltip>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
            {goal.status === 'active' && (
              <GoalButton icon={<Pause size={13} aria-hidden />} label={t('goal.action.pause')} disabled={busy} onClick={onPause} />
            )}
            {goal.status === 'paused' && (
              <GoalButton icon={<Play size={13} aria-hidden />} label={t('goal.action.resume')} disabled={busy} onClick={onResume} />
            )}
            {goal.status === 'complete' && (
              <GoalButton icon={<CheckCircle2 size={13} aria-hidden />} label={t('goal.action.new')} disabled={busy} onClick={() => {
                setEditing(true)
                setObjective('')
              }} />
            )}
            {goal.status !== 'complete' && (
              <GoalButton icon={<RotateCcw size={13} aria-hidden />} label={t('goal.action.replace')} disabled={busy} onClick={() => {
                setEditing(true)
                setObjective(goal.objective)
              }} />
            )}
            <GoalButton icon={<Trash2 size={13} aria-hidden />} label={t('goal.action.clear')} disabled={busy} onClick={onClear} danger />
          </div>
        </>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <textarea
            ref={inputRef}
            value={objective}
            onChange={(e) => setObjective(e.target.value)}
            placeholder={t('goal.objective.placeholder')}
            rows={3}
            style={{
              resize: 'vertical',
              minHeight: 74,
              maxHeight: 160,
              borderRadius: 8,
              border: '1px solid var(--border-default)',
              background: 'var(--bg-tertiary)',
              color: 'var(--text-primary)',
              padding: '9px 10px',
              font: 'inherit',
              fontSize: 13,
              lineHeight: 1.45,
              outline: 'none'
            }}
          />
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            {goal && (
              <button type="button" onClick={() => setEditing(false)} disabled={busy} style={secondaryButtonStyle}>
                {t('goal.action.cancel')}
              </button>
            )}
            <button
              type="button"
              onClick={() => { void submitObjective() }}
              disabled={!canSubmit}
              style={{
                ...primaryButtonStyle,
                opacity: canSubmit ? 1 : 0.55,
                cursor: canSubmit ? 'pointer' : 'default'
              }}
            >
              {setLabel}
            </button>
          </div>
        </div>
      )}
    </div>
    </div>,
    document.body
  )
}

function GoalButton({
  icon,
  label,
  disabled,
  danger = false,
  onClick
}: {
  icon: JSX.Element
  label: string
  disabled?: boolean
  danger?: boolean
  onClick: () => Promise<boolean> | void
}): JSX.Element {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={() => { void onClick() }}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        border: '1px solid var(--border-default)',
        borderRadius: 8,
        background: 'var(--bg-tertiary)',
        color: danger ? 'var(--error)' : 'var(--text-secondary)',
        cursor: disabled ? 'default' : 'pointer',
        opacity: disabled ? 0.55 : 1,
        fontSize: 12,
        padding: '5px 8px'
      }}
    >
      {icon}
      {label}
    </button>
  )
}

function statusDotStyle(status: ThreadGoal['status']): CSSProperties {
  const color = status === 'active'
    ? 'var(--success)'
    : status === 'paused'
      ? 'var(--warning)'
      : status === 'budgetLimited'
        ? 'var(--error)'
        : 'var(--info)'
  return {
    width: 8,
    height: 8,
    borderRadius: '999px',
    background: color,
    flexShrink: 0
  }
}

const iconButtonStyle: CSSProperties = {
  border: 'none',
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  width: 24,
  height: 24,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  borderRadius: 6
}

const secondaryButtonStyle: CSSProperties = {
  border: 'none',
  borderRadius: 8,
  background: 'var(--bg-tertiary)',
  color: 'var(--text-primary)',
  cursor: 'pointer',
  fontSize: 13,
  padding: '7px 12px'
}

const primaryButtonStyle: CSSProperties = {
  border: '1px solid var(--text-primary)',
  borderRadius: 8,
  background: 'var(--text-primary)',
  color: 'var(--bg-primary)',
  cursor: 'pointer',
  fontSize: 13,
  fontWeight: 600,
  padding: '7px 12px'
}
