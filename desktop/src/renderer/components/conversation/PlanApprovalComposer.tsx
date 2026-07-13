import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent as ReactKeyboardEvent,
  type RefObject
} from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useUIStore } from '../../stores/uiStore'
import { acceptPlanSentinelFor } from '../../utils/planAcceptSentinel'
import { startTurnWithOptimisticUI } from '../../utils/startTurn'
import { ComposerShell, DECISION_MASCOT } from './ComposerShell'
import { ConversationColumn } from './ConversationColumn'
import {
  ComposerChoiceArrowHints,
  ComposerChoiceRow,
  composerChoiceNumberStyle,
  composerChoiceRowStyle
} from './ComposerChoiceRow'
import {
  DecisionDismissButton,
  DecisionSubmitButton,
  decisionComposerBodyStyle,
  decisionComposerChoiceListStyle,
  decisionComposerFooterActionsStyle,
  decisionComposerTitleStyle
} from './DecisionComposerChrome'
import { RichInputArea, type RichInputAreaHandle } from './RichInputArea'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import {
  DEFAULT_COMPOSER_MASCOT_EFFECT_STATE,
  type ComposerMascotEffectState
} from './composerMascotEffectState'

interface PlanApprovalComposerProps {
  threadId: string
  workspacePath: string
  turnId: string
  mascotEffectState?: ComposerMascotEffectState
}

export function PlanApprovalComposer({
  threadId,
  workspacePath,
  turnId,
  mascotEffectState = DEFAULT_COMPOSER_MASCOT_EFFECT_STATE
}: PlanApprovalComposerProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [editorFocused, setEditorFocused] = useState(false)
  const [, setContentRevision] = useState(0)
  const [selectedIndex, setSelectedIndex] = useState(0)
  const richRef = useRef<RichInputAreaHandle>(null)
  const sendInFlightRef = useRef(false)
  const setThreadMode = useConversationStore((s) => s.setThreadMode)
  const dismissPlanApproval = useUIStore((s) => s.dismissPlanApproval)

  const text = richRef.current?.getText() ?? ''
  const trimmed = text.trim()
  const submitAsNo = trimmed.length > 0

  const handleAcceptPlan = useCallback(async (): Promise<void> => {
    if (sendInFlightRef.current) return
    sendInFlightRef.current = true
    dismissPlanApproval(turnId)
    setThreadMode('agent')
    try {
      await window.api.appServer.sendRequest('thread/mode/set', { threadId, mode: 'agent' })
    } catch (err) {
      console.error('thread/mode/set failed:', err)
    }
    await startTurnWithOptimisticUI({
      threadId,
      workspacePath,
      text: acceptPlanSentinelFor(locale),
      fallbackThreadName: t('toast.imageMessage'),
      fileFallbackThreadName: t('toast.fileReferenceMessage'),
      attachmentFallbackThreadName: t('toast.attachmentMessage'),
      includeUserPreview: false,
      renameThreadFromText: false
    })
    sendInFlightRef.current = false
  }, [dismissPlanApproval, locale, setThreadMode, t, threadId, turnId, workspacePath])

  const handleSubmit = useCallback(async (): Promise<void> => {
    if (sendInFlightRef.current) return
    if (!submitAsNo) {
      await handleAcceptPlan()
      return
    }
    sendInFlightRef.current = true
    const started = await startTurnWithOptimisticUI({
      threadId,
      workspacePath,
      text: trimmed,
      fallbackThreadName: t('toast.imageMessage'),
      fileFallbackThreadName: t('toast.fileReferenceMessage'),
      attachmentFallbackThreadName: t('toast.attachmentMessage')
    })
    if (started) {
      dismissPlanApproval(turnId)
      richRef.current?.clear()
    }
    sendInFlightRef.current = false
  }, [dismissPlanApproval, handleAcceptPlan, submitAsNo, t, threadId, trimmed, turnId, workspacePath])

  const selectAdjustmentRow = useCallback((): void => {
    setSelectedIndex(1)
    window.setTimeout(() => {
      richRef.current?.focus()
    }, 0)
  }, [])

  const handleEditorFocusChange = useCallback((focused: boolean): void => {
    setEditorFocused(focused)
    if (focused) setSelectedIndex(1)
  }, [])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        dismissPlanApproval(turnId)
        return
      }

      if (isEditableTarget(event.target) || event.metaKey || event.ctrlKey || event.altKey) return

      if (event.key === 'ArrowUp' || event.key === 'k') {
        event.preventDefault()
        setSelectedIndex(0)
      } else if (event.key === 'ArrowDown' || event.key === 'j') {
        event.preventDefault()
        selectAdjustmentRow()
      } else if (event.key === '1') {
        event.preventDefault()
        void handleAcceptPlan()
      } else if (event.key === '2') {
        event.preventDefault()
        selectAdjustmentRow()
      } else if (event.key === 'Enter') {
        event.preventDefault()
        if (selectedIndex === 0) {
          void handleAcceptPlan()
        } else {
          void handleSubmit()
        }
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [dismissPlanApproval, handleAcceptPlan, handleSubmit, selectAdjustmentRow, selectedIndex, turnId])

  const acceptSelected = selectedIndex === 0
  const adjustmentSelected = selectedIndex === 1

  return (
    <div style={composerDockStyle}>
      <ConversationColumn>
        <ComposerShell
          dragOver={false}
          dropLabel=""
          onDragOver={(e) => e.preventDefault()}
          onDragLeave={(e) => e.preventDefault()}
          onDrop={(e) => e.preventDefault()}
          focused={editorFocused}
          showMascot
          mascotInteraction={DECISION_MASCOT}
          mascotReasoningEffort={mascotEffectState.reasoningEffort}
          mascotSpeed={mascotEffectState.speed}
          mascotContextMax={mascotEffectState.contextMax}
          mascotHandoff
          editor={(
            <div style={decisionComposerBodyStyle}>
              <div style={decisionComposerTitleStyle}>{t('planApproval.title')}</div>
              <div style={decisionComposerChoiceListStyle}>
                <ComposerChoiceRow
                  index={0}
                  label={t('planApproval.yes')}
                  selected={acceptSelected}
                  canMoveUp={false}
                  canMoveDown
                  density="decision"
                  onSelect={() => {
                    void handleAcceptPlan()
                  }}
                />
                <PlanAdjustmentRow
                  selected={adjustmentSelected}
                  richRef={richRef}
                  placeholder={t('planApproval.noPlaceholder')}
                  onSelect={selectAdjustmentRow}
                  onSubmit={() => {
                    void handleSubmit()
                  }}
                  onContentChange={() => {
                    setContentRevision((n) => n + 1)
                  }}
                  onFocusChange={handleEditorFocusChange}
                />
              </div>
            </div>
          )}
          footerLeading={<div />}
          footerAction={(
            <div style={decisionComposerFooterActionsStyle}>
              <DecisionDismissButton
                label={t('planApproval.dismissHint')}
                onClick={() => dismissPlanApproval(turnId)}
                ariaLabel={t('planApproval.dismissHint')}
                tooltipLabel={t('planApproval.escKey')}
                shortcut={ACTION_SHORTCUTS.cancel}
              />
              <DecisionSubmitButton
                label={t('planApproval.submit')}
                onClick={() => {
                  void handleSubmit()
                }}
                disabled={sendInFlightRef.current}
              />
            </div>
          )}
        />
      </ConversationColumn>
    </div>
  )
}

function PlanAdjustmentRow({
  selected,
  richRef,
  placeholder,
  onSelect,
  onSubmit,
  onContentChange,
  onFocusChange
}: {
  selected: boolean
  richRef: RefObject<RichInputAreaHandle | null>
  placeholder: string
  onSelect: () => void
  onSubmit: () => void
  onContentChange: () => void
  onFocusChange: (focused: boolean) => void
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const highlighted = hovered || focused

  return (
    <div
      role="button"
      tabIndex={selected ? -1 : 0}
      aria-pressed={selected}
      aria-label={`2. ${placeholder}`}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(event) => {
        const nextTarget = event.relatedTarget
        if (!(nextTarget instanceof Node) || !event.currentTarget.contains(nextTarget)) {
          setFocused(false)
        }
      }}
      onClick={onSelect}
      onKeyDown={(event: ReactKeyboardEvent<HTMLDivElement>) => {
        if (!selected && (event.key === 'Enter' || event.key === ' ')) {
          event.preventDefault()
          onSelect()
        }
      }}
      style={composerChoiceRowStyle(selected, false, highlighted, 'decision')}
    >
      <span style={composerChoiceNumberStyle('decision')}>2.</span>
      <div style={{ flex: '1 1 auto', minWidth: 0 }}>
        <RichInputArea
          ref={richRef}
          chrome="inline"
          placeholder={placeholder}
          onSubmit={onSubmit}
          onAtQuery={() => {}}
          onSlashQuery={() => {}}
          onContentChange={onContentChange}
          onFocusChange={(focus) => {
            onFocusChange(focus)
          }}
        />
      </div>
      {selected && <ComposerChoiceArrowHints canMoveUp canMoveDown={false} />}
    </div>
  )
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
