import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import type { DesktopPluginComposerSurfaceContext } from '@dotcraft/plugin'
import { useT } from '../../contexts/LocaleContext'
import type { ApprovalDetailRowSpec, ApprovalOptionSpec, PendingApproval } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'
import type { ApprovalType } from '../../types/conversation'
import { approvalOptions, approvalRoles } from '../../utils/approvalOptions'
import { approvalQuestionKey, approvalRequestKey } from '../../utils/approvalRequest'
import { submitApprovalDecision } from '../../utils/submitApprovalDecision'
import { ComposerShell, DECISION_MASCOT } from './ComposerShell'
import { ConversationColumn } from './ConversationColumn'
import { useComposerProvider } from './useComposerProvider'
import {
  ComposerStatusContent,
  ComposerToolbarLeadingSlots,
  ComposerToolbarTrailingSlots
} from './ComposerSurfaceSlots'
import { DesktopPluginSurface } from '../desktopPlugins/DesktopPluginSurface'
import { SplitButton, type SplitButtonItem } from '../ui/SplitButton'
import { inSameSideChat } from '../sideChat/sideChatScope'
import { DecisionDismissButton, DecisionSubmitButton } from './DecisionComposerChrome'
import {
  DEFAULT_COMPOSER_MASCOT_EFFECT_STATE,
  type ComposerMascotEffectState
} from './composerMascotEffectState'

interface ApprovalDecisionComposerProps {
  request: PendingApproval
  onResponseAccepted?: () => void
  mascotEffectState?: ComposerMascotEffectState
  desktopPluginSurfaceContext?: DesktopPluginComposerSurfaceContext
  showMascot?: boolean
}

/** Default tool-approval detail rows (when the request carries no custom `detailRows`). */
function buildToolDetailRows(request: PendingApproval, t: ReturnType<typeof useT>): ApprovalDetailRowSpec[] {
  const rows: ApprovalDetailRowSpec[] = [
    { label: t('approval.detail.type'), value: t(approvalTypeLabelKey(request.approvalType)) }
  ]
  if (request.approvalType === 'computerUse') {
    return [...rows, { label: t('approval.detail.target'), value: request.target.trim(), mono: true }]
  }
  const operation = request.operation.trim()
  const target = request.target.trim()
  const reason = (request.shell ? request.shell.reasons.join(' ') : request.reason).trim()
  if (operation.length > 0) rows.push({ label: t('approval.detail.operation'), value: operation, mono: true })
  if (target.length > 0) rows.push({ label: t('approval.detail.target'), value: target, mono: true })
  if (reason.length > 0) rows.push({ label: t('approval.detail.reason'), value: reason })
  return rows
}

export function ApprovalDecisionComposer({
  request,
  onResponseAccepted,
  mascotEffectState = DEFAULT_COMPOSER_MASCOT_EFFECT_STATE,
  desktopPluginSurfaceContext,
  showMascot = true
}: ApprovalDecisionComposerProps): JSX.Element {
  const t = useT()
  const rootRef = useRef<HTMLDivElement>(null)
  const requestKey = useMemo(() => approvalRequestKey(request), [request])
  const [submittingRequestKey, setSubmittingRequestKey] = useState<string | null>(null)
  const [submittedRequestKey, setSubmittedRequestKey] = useState<string | null>(null)
  const sendingRef = useRef<string | null>(null)

  useEffect(() => {
    sendingRef.current = null
    setSubmittingRequestKey(null)
    setSubmittedRequestKey(null)
  }, [requestKey])

  const declineValue = request.declineValue ?? 'decline'
  const roles = useMemo(
    () => approvalRoles(approvalOptions(request, t), declineValue),
    [declineValue, request, t]
  )
  const locallySubmitted = request.locallySubmittedDecision != null
  const submitting = submittingRequestKey === requestKey
  const submitted = submittedRequestKey === requestKey
  const locked = submitting || submitted || locallySubmitted

  const sendDecision = useCallback(async (value: string): Promise<void> => {
    if (sendingRef.current === requestKey || submitted || request.locallySubmittedDecision != null) return
    sendingRef.current = requestKey
    setSubmittingRequestKey(requestKey)
    try {
      await submitApprovalDecision(request, value)
      setSubmittedRequestKey(requestKey)
      if (!request.submit) onResponseAccepted?.()
    } catch (err) {
      if (sendingRef.current === requestKey) sendingRef.current = null
      setSubmittingRequestKey((current) => current === requestKey ? null : current)
      addToast(t('approval.sendFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }, [onResponseAccepted, request, requestKey, submitted, t])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.defaultPrevented || isEditableTarget(event.target) || !inSameSideChat(rootRef.current, event.target)) return

      if (event.key === 'Escape') {
        event.preventDefault()
        void sendDecision(declineValue)
      } else if (event.key === 'Enter' && !locked && !isActivatableTarget(event.target)) {
        event.preventDefault()
        void sendDecision(roles.primary.value)
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [declineValue, locked, roles.primary.value, sendDecision])

  const questionText = request.question
    ?? t(approvalQuestionKey(request.approvalType), { app: request.targetLabel ?? request.target })
  const detailRows = request.detailRows ?? buildToolDetailRows(request, t)
  const denyLabel = roles.deny?.label ?? t('approval.option.decline.label')
  const sendReject = (): void => {
    void sendDecision(declineValue)
  }
  const sendPrimary = (): void => {
    void sendDecision(roles.primary.value)
  }
  const menuItems = (options: ApprovalOptionSpec[]): SplitButtonItem[] => options.map((option) => ({
    key: option.value,
    label: option.label,
    description: option.description || undefined,
    onClick: () => {
      void sendDecision(option.value)
    }
  }))
  const provider = useComposerProvider()
  const mascotSurfaceContext = desktopPluginSurfaceContext ?? {
    workspacePath: null,
    threadId: request.threadId,
    mode: 'agent',
    busy: false,
    awaitingApproval: true,
    variant: 'default',
    minimalChrome: false,
    provider
  } as const

  return (
    <div ref={rootRef} style={composerDockStyle}>
      <ConversationColumn>
        <DesktopPluginSurface name="composer.before" context={mascotSurfaceContext} />
        <ComposerShell
          desktopPluginSurfaceContext={mascotSurfaceContext}
          dragOver={false}
          dropLabel=""
          onDragOver={(e) => e.preventDefault()}
          onDragLeave={(e) => e.preventDefault()}
          onDrop={(e) => e.preventDefault()}
          showMascot={showMascot}
          petSurface="approval"
          mascotInteraction={DECISION_MASCOT}
          mascotReasoningEffort={mascotEffectState.reasoningEffort}
          mascotSpeed={mascotEffectState.speed}
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
            </div>
          )}
          footerLeading={(
            <ComposerToolbarLeadingSlots context={mascotSurfaceContext} />
          )}
          footerAction={(
            <ComposerToolbarTrailingSlots
              context={mascotSurfaceContext}
              submit={(
                <>
                  {roles.denyScopes.length > 0
                    ? (
                      <SplitButton
                        variant="ghost"
                        label={denyLabel}
                        ariaLabel={t('approval.rejectShortcutAria')}
                        onClick={sendReject}
                        menuLabel={t('approval.moreRejectOptions')}
                        items={menuItems(roles.denyScopes)}
                        disabled={locked}
                      />
                    )
                    : (
                      <DecisionDismissButton
                        label={denyLabel}
                        ariaLabel={t('approval.rejectShortcutAria')}
                        onClick={sendReject}
                        disabled={locked}
                      />
                    )}
                  {roles.wider.length > 0
                    ? (
                      <SplitButton
                        label={roles.primary.label}
                        onClick={sendPrimary}
                        menuLabel={t('approval.moreAllowOptions')}
                        items={menuItems(roles.wider)}
                        disabled={locked}
                      />
                    )
                    : <DecisionSubmitButton label={roles.primary.label} onClick={sendPrimary} disabled={locked} />}
                </>
              )}
            />
          )}
          belowFooter={(
            <ComposerStatusContent context={mascotSurfaceContext} />
          )}
        />
        <DesktopPluginSurface name="composer.after" context={mascotSurfaceContext} />
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

function approvalTypeLabelKey(type: ApprovalType): string {
  if (type === 'file') return 'approval.type.file'
  if (type === 'remoteResource') return 'approval.type.remoteResource'
  if (type === 'computerUse') return 'approval.type.computerUse'
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

function isActivatableTarget(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('button, a[href]') != null
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
