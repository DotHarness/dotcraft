import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { PendingApproval } from '../../stores/conversationStore'
import { useConversationStore } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'
import type { ApprovalDecision, ApprovalType } from '../../types/conversation'
import { ComposerShell } from './ComposerShell'
import { ConversationColumn } from './ConversationColumn'
import { ComposerChoiceRow } from './ComposerChoiceRow'

interface ApprovalDecisionComposerProps {
  request: PendingApproval
}

interface ApprovalOption {
  decision: ApprovalDecision
  label: string
  description: string
}

export function ApprovalDecisionComposer({ request }: ApprovalDecisionComposerProps): JSX.Element {
  const t = useT()
  const [selectedIndex, setSelectedIndex] = useState(0)
  const [submitting, setSubmitting] = useState(false)
  const [submitted, setSubmitted] = useState(false)
  const sendingRef = useRef(false)

  useEffect(() => {
    sendingRef.current = false
    setSelectedIndex(0)
    setSubmitting(false)
    setSubmitted(false)
  }, [request.bridgeId, request.itemId])

  const options = useMemo<ApprovalOption[]>(() => [
    {
      decision: 'accept',
      label: t('approval.option.accept.label'),
      description: t('approval.option.accept.description')
    },
    {
      decision: 'acceptForSession',
      label: t('approval.option.acceptForSession.label'),
      description: t('approval.option.acceptForSession.description')
    },
    {
      decision: 'acceptAlways',
      label: t('approval.option.acceptAlways.label'),
      description: t('approval.option.acceptAlways.description')
    },
    {
      decision: 'decline',
      label: t('approval.option.decline.label'),
      description: t('approval.option.decline.description')
    },
    {
      decision: 'cancel',
      label: t('approval.option.cancel.label'),
      description: t('approval.option.cancel.description')
    }
  ], [t])

  const selectedOption = options[Math.min(selectedIndex, options.length - 1)] ?? options[0]
  const locallySubmitted = request.locallySubmittedDecision != null
  const locked = submitting || submitted || locallySubmitted
  const canMoveUp = selectedIndex > 0
  const canMoveDown = selectedIndex + 1 < options.length
  const showFooterReject = selectedOption.decision !== 'decline'

  const sendDecision = useCallback(async (decision: ApprovalDecision): Promise<void> => {
    if (sendingRef.current || submitted || request.locallySubmittedDecision != null) return
    sendingRef.current = true
    setSubmitting(true)
    useConversationStore.getState().onApprovalSubmitStarted(decision)

    try {
      await window.api.appServer.sendServerResponse(request.bridgeId, { decision })
      useConversationStore.getState().onApprovalDecision(decision)
      setSubmitted(true)
    } catch (err) {
      useConversationStore.getState().onApprovalSubmitFailed()
      sendingRef.current = false
      setSubmitting(false)
      addToast(
        t('approval.sendFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }, [request.bridgeId, request.locallySubmittedDecision, submitted, t])

  const submitSelected = useCallback((): void => {
    void sendDecision(selectedOption.decision)
  }, [selectedOption.decision, sendDecision])

  const selectOption = (index: number): void => {
    if (locked) return
    const clamped = Math.min(Math.max(index, 0), options.length - 1)
    setSelectedIndex(clamped)
  }

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (isEditableTarget(event.target)) return

      if (event.key === 'Escape') {
        event.preventDefault()
        void sendDecision('decline')
        return
      }

      if (locked) return

      if (event.key === 'ArrowUp' || event.key === 'k') {
        event.preventDefault()
        selectOption(selectedIndex - 1)
      } else if (event.key === 'ArrowDown' || event.key === 'j') {
        event.preventDefault()
        selectOption(selectedIndex + 1)
      } else if (/^[1-9]$/.test(event.key)) {
        const index = Number(event.key) - 1
        if (index < options.length) {
          event.preventDefault()
          selectOption(index)
        }
      } else if (event.key === 'Enter') {
        event.preventDefault()
        submitSelected()
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [locked, options.length, selectedIndex, sendDecision, submitSelected])

  const approvalType = request.approvalType
  const typeLabel = t(approvalTypeLabelKey(approvalType))
  const questionText = t(approvalQuestionKey(approvalType))
  const operation = request.operation.trim()
  const target = request.target.trim()
  const reason = request.reason.trim()

  return (
    <div style={composerDockStyle}>
      <ConversationColumn>
        <ComposerShell
          dragOver={false}
          dropLabel=""
          onDragOver={(e) => e.preventDefault()}
          onDragLeave={(e) => e.preventDefault()}
          onDrop={(e) => e.preventDefault()}
          focused
          editor={(
            <div style={{ display: 'grid', gap: '8px' }}>
              <div style={questionStyle}>{questionText}</div>
              <div style={detailPanelStyle}>
                <ApprovalDetailRow label={t('approval.detail.type')} value={typeLabel} />
                {operation.length > 0 && (
                  <ApprovalDetailRow label={t('approval.detail.operation')} value={operation} mono />
                )}
                {target.length > 0 && (
                  <ApprovalDetailRow label={t('approval.detail.target')} value={target} mono />
                )}
                {reason.length > 0 && (
                  <ApprovalDetailRow label={t('approval.detail.reason')} value={reason} />
                )}
              </div>
              <div style={{ display: 'grid', gap: '6px' }}>
                {options.map((option, index) => (
                  <ComposerChoiceRow
                    key={option.decision}
                    index={index}
                    label={option.label}
                    description={option.description}
                    selected={selectedIndex === index}
                    canMoveUp={canMoveUp}
                    canMoveDown={canMoveDown}
                    disabled={locked}
                    descriptionAriaLabel={t('approval.optionDescriptionAria', { option: option.label })}
                    onSelect={() => {
                      if (selectedIndex === index) {
                        submitSelected()
                      } else {
                        selectOption(index)
                      }
                    }}
                  />
                ))}
              </div>
            </div>
          )}
          footerLeading={<div />}
          footerAction={(
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
              {showFooterReject && (
                <button
                  type="button"
                  onClick={() => {
                    void sendDecision('decline')
                  }}
                  disabled={locked}
                  aria-label={t('approval.rejectShortcutAria')}
                  style={rejectButtonStyle(locked)}
                >
                  <span>{t('approval.option.decline.label')}</span>
                  <span style={kbdChipStyle}>Esc</span>
                </button>
              )}
              <button
                type="button"
                onClick={submitSelected}
                disabled={locked}
                style={submitButtonStyle(locked)}
              >
                {selectedOption.label}
              </button>
            </div>
          )}
        />
      </ConversationColumn>
    </div>
  )
}

function ApprovalDetailRow({
  label,
  value,
  mono = false
}: {
  label: string
  value: string
  mono?: boolean
}): JSX.Element {
  return (
    <div style={detailRowStyle}>
      <span style={detailLabelStyle}>{label}</span>
      <span style={detailValueStyle(mono)}>{value}</span>
    </div>
  )
}

function approvalQuestionKey(type: ApprovalType): string {
  if (type === 'file') return 'approval.question.file'
  if (type === 'remoteResource') return 'approval.question.remoteResource'
  if (type === 'skill') return 'approval.question.skill'
  return 'approval.question.shell'
}

function approvalTypeLabelKey(type: ApprovalType): string {
  if (type === 'file') return 'approval.type.file'
  if (type === 'remoteResource') return 'approval.type.remoteResource'
  if (type === 'skill') return 'approval.kind.skill'
  return 'approval.type.shell'
}

function isEditableTarget(target: EventTarget | null): boolean {
  const element = target instanceof Element
    ? target
    : target instanceof Node
      ? target.parentElement
      : null
  if (!element) return false
  if (element.closest('[contenteditable="true"]')) return true
  const tag = element.tagName.toLowerCase()
  return tag === 'input' || tag === 'textarea' || tag === 'select'
}

const composerDockStyle: CSSProperties = {
  flexShrink: 0,
  padding: '0 clamp(20px, 4vw, 40px)'
}

const questionStyle: CSSProperties = {
  minWidth: 0,
  color: 'var(--text-primary)',
  fontSize: 'var(--text-body-size)',
  fontWeight: 600,
  lineHeight: 'var(--text-body-line-height)'
}

const detailPanelStyle: CSSProperties = {
  display: 'grid',
  gap: '4px',
  padding: '8px 10px',
  border: '1px solid var(--border-default)',
  borderRadius: '8px',
  background: 'var(--bg-primary)'
}

const detailRowStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '84px minmax(0, 1fr)',
  gap: '8px',
  alignItems: 'baseline',
  minWidth: 0
}

const detailLabelStyle: CSSProperties = {
  color: 'var(--text-dimmed)',
  fontSize: 'var(--text-body-size)',
  lineHeight: 'var(--text-body-line-height)',
  whiteSpace: 'nowrap'
}

function detailValueStyle(mono: boolean): CSSProperties {
  return {
    minWidth: 0,
    color: 'var(--text-secondary)',
    fontFamily: mono ? 'var(--font-mono)' : undefined,
    fontSize: 'var(--text-body-size)',
    lineHeight: 'var(--text-body-line-height)',
    overflowWrap: 'anywhere'
  }
}

function rejectButtonStyle(disabled: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    border: 'none',
    background: 'transparent',
    color: 'var(--text-dimmed)',
    fontSize: '12px',
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.6 : 1,
    padding: 0
  }
}

function submitButtonStyle(disabled: boolean): CSSProperties {
  return {
    height: '32px',
    borderRadius: '999px',
    border: '1px solid var(--border-default)',
    padding: '0 14px',
    background: 'var(--bg-primary)',
    color: 'var(--text-primary)',
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.68 : 1,
    fontSize: '12px',
    fontWeight: 600
  }
}

const kbdChipStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  minWidth: '28px',
  height: '20px',
  padding: '0 6px',
  borderRadius: '4px',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-secondary)',
  color: 'var(--text-secondary)',
  fontSize: '11px',
  fontFamily: 'var(--font-mono, ui-monospace)'
}
