import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { ApprovalDetailRowSpec, ApprovalOptionSpec, PendingApproval } from '../../stores/conversationStore'
import { useConversationStore } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'
import type { ApprovalDecision, ApprovalType } from '../../types/conversation'
import { ComposerShell, DECISION_MASCOT } from './ComposerShell'
import { ConversationColumn } from './ConversationColumn'
import { ComposerChoiceRow } from './ComposerChoiceRow'

interface ApprovalDecisionComposerProps {
  request: PendingApproval
}

/** Default tool-approval detail rows (when the request carries no custom `detailRows`). */
function buildToolDetailRows(request: PendingApproval, t: ReturnType<typeof useT>): ApprovalDetailRowSpec[] {
  const rows: ApprovalDetailRowSpec[] = [
    { label: t('approval.detail.type'), value: t(approvalTypeLabelKey(request.approvalType)) }
  ]
  const operation = request.operation.trim()
  const target = request.target.trim()
  const reason = request.reason.trim()
  if (operation.length > 0) rows.push({ label: t('approval.detail.operation'), value: operation, mono: true })
  if (target.length > 0) rows.push({ label: t('approval.detail.target'), value: target, mono: true })
  if (reason.length > 0) rows.push({ label: t('approval.detail.reason'), value: reason })
  return rows
}

function approvalRequestKey(request: PendingApproval): string {
  return `${request.source ?? 'tool'}:${request.requestId || request.itemId || request.bridgeId}`
}

function approvalRequestTarget(request: PendingApproval): {
  bridgeId: string
  threadId: string | null
  turnId: string | null
  requestId: string
  itemId: string
} {
  return {
    bridgeId: request.bridgeId,
    threadId: request.threadId,
    turnId: request.turnId,
    requestId: request.requestId,
    itemId: request.itemId
  }
}

export function ApprovalDecisionComposer({ request }: ApprovalDecisionComposerProps): JSX.Element {
  const t = useT()
  const requestKey = useMemo(() => approvalRequestKey(request), [request])
  const requestTarget = useMemo(() => approvalRequestTarget(request), [request])
  const [selectedIndex, setSelectedIndex] = useState(0)
  const [submittingRequestKey, setSubmittingRequestKey] = useState<string | null>(null)
  const [submittedRequestKey, setSubmittedRequestKey] = useState<string | null>(null)
  const sendingRef = useRef<string | null>(null)

  useEffect(() => {
    sendingRef.current = null
    setSelectedIndex(0)
    setSubmittingRequestKey(null)
    setSubmittedRequestKey(null)
  }, [requestKey])

  const options = useMemo<ApprovalOptionSpec[]>(() => request.options ?? [
    {
      value: 'accept',
      label: t('approval.option.accept.label'),
      description: t('approval.option.accept.description')
    },
    {
      value: 'acceptForSession',
      label: t('approval.option.acceptForSession.label'),
      description: t('approval.option.acceptForSession.description')
    },
    {
      value: 'acceptAlways',
      label: t('approval.option.acceptAlways.label'),
      description: t('approval.option.acceptAlways.description')
    },
    {
      value: 'decline',
      label: t('approval.option.decline.label'),
      description: t('approval.option.decline.description')
    },
    {
      value: 'cancel',
      label: t('approval.option.cancel.label'),
      description: t('approval.option.cancel.description')
    }
  ], [t, request.options])

  const selectedOption = options[Math.min(selectedIndex, options.length - 1)] ?? options[0]
  const declineValue = request.declineValue ?? 'decline'
  const declineOption = options.find((option) => option.value === declineValue)
  const locallySubmitted = request.locallySubmittedDecision != null
  const submitting = submittingRequestKey === requestKey
  const submitted = submittedRequestKey === requestKey
  const locked = submitting || submitted || locallySubmitted
  const canMoveUp = selectedIndex > 0
  const canMoveDown = selectedIndex + 1 < options.length
  const showFooterReject = selectedOption.value !== declineValue

  const sendDecision = useCallback(async (value: string): Promise<void> => {
    if (sendingRef.current === requestKey || submitted || request.locallySubmittedDecision != null) return
    sendingRef.current = requestKey
    setSubmittingRequestKey(requestKey)

    const failed = (err: unknown): void => {
      if (sendingRef.current === requestKey) sendingRef.current = null
      setSubmittingRequestKey((current) => current === requestKey ? null : current)
      addToast(t('approval.sendFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }

    // Non-tool sources (e.g. browser-use) route through a custom handler; tool approvals respond
    // to AppServer via the bridge.
    if (request.submit) {
      try {
        await request.submit(value)
        setSubmittedRequestKey(requestKey)
      } catch (err) {
        failed(err)
      }
      return
    }

    const decision = value as ApprovalDecision
    useConversationStore.getState().onApprovalSubmitStarted(decision, requestTarget)
    try {
      await window.api.appServer.sendServerResponse(request.bridgeId, { decision })
      useConversationStore.getState().onApprovalDecision(decision, requestTarget)
      setSubmittedRequestKey(requestKey)
    } catch (err) {
      useConversationStore.getState().onApprovalSubmitFailed(requestTarget)
      failed(err)
    }
  }, [request.bridgeId, request.locallySubmittedDecision, request.submit, requestKey, requestTarget, submitted, t])

  const submitSelected = useCallback((): void => {
    void sendDecision(selectedOption.value)
  }, [selectedOption.value, sendDecision])

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
        void sendDecision(declineValue)
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
  }, [declineValue, locked, options.length, selectedIndex, sendDecision, submitSelected])

  const questionText = request.question ?? t(approvalQuestionKey(request.approvalType))
  const detailRows = request.detailRows ?? buildToolDetailRows(request, t)

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
          showMascot
          mascotInteraction={DECISION_MASCOT}
          mascotHandoff
          editor={(
            <div style={{ display: 'grid', gap: '8px' }}>
              <div style={questionStyle}>{questionText}</div>
              <div data-testid="approval-detail-panel" style={detailPanelStyle}>
                {detailRows.map((row, index) => (
                  <ApprovalDetailRow
                    key={`${row.label}-${index}`}
                    label={row.label}
                    value={row.value}
                    mono={row.mono}
                    valueTestId={`approval-detail-value-${index}`}
                  />
                ))}
              </div>
              <div style={{ display: 'grid', gap: '6px' }}>
                {options.map((option, index) => (
                  <ComposerChoiceRow
                    key={option.value}
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
                    void sendDecision(declineValue)
                  }}
                  disabled={locked}
                  aria-label={t('approval.rejectShortcutAria')}
                  style={rejectButtonStyle(locked)}
                >
                  <span>{declineOption?.label ?? t('approval.option.decline.label')}</span>
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
  mono = false,
  valueTestId
}: {
  label: string
  value: string
  mono?: boolean
  valueTestId?: string
}): JSX.Element {
  return (
    <div style={detailRowStyle}>
      <span style={detailLabelStyle}>{label}</span>
      <span data-testid={valueTestId} style={detailValueStyle(mono)}>{value}</span>
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

const DETAIL_PANEL_MAX_HEIGHT = 'min(34vh, 300px)'
const DETAIL_VALUE_MAX_HEIGHT = '120px'

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
  background: 'var(--bg-primary)',
  maxHeight: DETAIL_PANEL_MAX_HEIGHT,
  overflowY: 'auto',
  overscrollBehavior: 'contain'
}

const detailRowStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '84px minmax(0, 1fr)',
  gap: '8px',
  alignItems: 'start',
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
    overflowWrap: 'anywhere',
    maxHeight: DETAIL_VALUE_MAX_HEIGHT,
    overflowY: 'auto',
    // Long approval details (operation, target, and reason) must not push the decision
    // options off-screen; each value gets a small local scroll area when needed.
    whiteSpace: mono ? 'pre-wrap' : 'normal'
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
