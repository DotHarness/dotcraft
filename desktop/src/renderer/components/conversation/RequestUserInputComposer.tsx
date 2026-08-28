import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent as ReactKeyboardEvent,
  type RefObject
} from 'react'
import type { DesktopPluginComposerSurfaceContext } from '@dotcraft/plugin'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { Input } from '../ui/Input'
import type { PendingUserInputRequest } from '../../stores/conversationStore'
import { useConversationStore } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'
import { ComposerShell, DECISION_MASCOT } from './ComposerShell'
import { ConversationColumn } from './ConversationColumn'
import { DesktopPluginSurface } from '../desktopPlugins/DesktopPluginSurface'
import {
  ComposerStatusContent,
  ComposerToolbarLeadingSlots,
  ComposerToolbarTrailingSlots
} from './ComposerSurfaceSlots'
import {
  DecisionDismissButton,
  DecisionSubmitButton,
  decisionComposerBodyStyle,
  decisionComposerChoiceListStyle,
  decisionComposerFooterActionsStyle,
  decisionComposerTitleRowStyle,
  decisionComposerTitleStyle
} from './DecisionComposerChrome'
import {
  ComposerChoiceArrowHints,
  ComposerChoiceRow,
  composerChoiceNumberStyle,
  composerChoiceRowStyle
} from './ComposerChoiceRow'
import {
  DEFAULT_COMPOSER_MASCOT_EFFECT_STATE,
  type ComposerMascotEffectState
} from './composerMascotEffectState'

interface RequestUserInputResponse {
  answers: Record<string, { answers: string[] }>
}

interface RequestUserInputComposerProps {
  request: PendingUserInputRequest
  onResponseAccepted?: () => void
  mascotEffectState?: ComposerMascotEffectState
  desktopPluginSurfaceContext?: DesktopPluginComposerSurfaceContext
}

export function RequestUserInputComposer({
  request,
  onResponseAccepted,
  mascotEffectState = DEFAULT_COMPOSER_MASCOT_EFFECT_STATE,
  desktopPluginSurfaceContext
}: RequestUserInputComposerProps): JSX.Element | null {
  const t = useT()
  const [currentQuestion, setCurrentQuestion] = useState(0)
  const [selected, setSelected] = useState<number[]>([])
  const [otherText, setOtherText] = useState<string[]>([])
  const otherInputRef = useRef<HTMLInputElement | null>(null)
  const sentEmptyRef = useRef(false)

  useEffect(() => {
    sentEmptyRef.current = false
    setCurrentQuestion(0)
    setSelected(request.questions.map(() => 0))
    setOtherText(request.questions.map(() => ''))
  }, [request.requestId, request.questions])

  const respond = useCallback((response: RequestUserInputResponse): void => {
    useConversationStore.getState().onUserInputResolved()
    window.api.appServer
      .sendServerResponse(request.bridgeId, response)
      .then(() => {
        onResponseAccepted?.()
      })
      .catch((err: unknown) => {
        addToast(
          t('userInput.sendFailed', {
            error: err instanceof Error ? err.message : String(err)
          }),
          'error'
        )
      })
  }, [onResponseAccepted, request.bridgeId, t])

  useEffect(() => {
    if (request.questions.length === 0 && !sentEmptyRef.current) {
      sentEmptyRef.current = true
      respond({ answers: {} })
    }
  }, [request.questions.length, respond])

  const questionCount = request.questions.length
  if (questionCount === 0) return null

  const safeQuestionIndex = Math.min(currentQuestion, questionCount - 1)
  const question = request.questions[safeQuestionIndex]
  const hasOther = question.isOther !== false
  const otherIndex = question.options.length
  const optionCount = question.options.length + (hasOther ? 1 : 0)
  const selectedIndex = Math.min(selected[safeQuestionIndex] ?? 0, Math.max(optionCount - 1, 0))
  const isOtherSelected = hasOther && selectedIndex === otherIndex
  const canMoveUp = selectedIndex > 0
  const canMoveDown = selectedIndex + 1 < optionCount
  const canGoPreviousQuestion = safeQuestionIndex > 0
  const canGoNextQuestion = safeQuestionIndex + 1 < questionCount

  const updateSelected = (index: number): void => {
    const clamped = Math.min(Math.max(index, 0), Math.max(optionCount - 1, 0))
    setSelected((prev) => {
      const next = [...prev]
      next[safeQuestionIndex] = clamped
      return next
    })
  }

  const updateOther = (value: string): void => {
    setOtherText((prev) => {
      const next = [...prev]
      next[safeQuestionIndex] = value
      return next
    })
  }

  const buildResponse = (): RequestUserInputResponse => {
    const answers: Record<string, { answers: string[] }> = {}
    request.questions.forEach((q, index) => {
      const choice = selected[index] ?? 0
      if (choice < q.options.length) {
        answers[q.id] = { answers: [q.options[choice]?.label ?? ''] }
        return
      }
      const text = (otherText[index] ?? '').trim()
      answers[q.id] = { answers: [text ? `user_note: ${text}` : t('userInput.other')] }
    })
    return { answers }
  }

  const submit = useCallback((): void => {
    if (safeQuestionIndex + 1 < questionCount) {
      setCurrentQuestion((index) => index + 1)
      return
    }
    respond(buildResponse())
  }, [buildResponse, questionCount, respond, safeQuestionIndex])

  const dismiss = useCallback((): void => {
    respond({ answers: {} })
  }, [respond])

  const goPreviousQuestion = useCallback((): void => {
    setCurrentQuestion((index) => Math.max(index - 1, 0))
  }, [])

  const goNextQuestion = useCallback((): void => {
    setCurrentQuestion((index) => Math.min(index + 1, questionCount - 1))
  }, [questionCount])

  useEffect(() => {
    if (!isOtherSelected) return
    window.setTimeout(() => {
      otherInputRef.current?.focus()
    }, 0)
  }, [isOtherSelected, safeQuestionIndex])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      const editingText = isEditableTarget(event.target)

      if (event.key === 'Escape') {
        event.preventDefault()
        dismiss()
        return
      }

      if (editingText) return

      if (event.key === 'ArrowUp' || event.key === 'k') {
        event.preventDefault()
        updateSelected(selectedIndex - 1)
      } else if (event.key === 'ArrowDown' || event.key === 'j') {
        event.preventDefault()
        updateSelected(selectedIndex + 1)
      } else if (/^[1-9]$/.test(event.key)) {
        const index = Number(event.key) - 1
        if (index < optionCount) {
          event.preventDefault()
          updateSelected(index)
        }
      } else if (event.key === 'ArrowLeft') {
        if (questionCount > 1) {
          event.preventDefault()
          goPreviousQuestion()
        }
      } else if (event.key === 'ArrowRight') {
        if (questionCount > 1) {
          event.preventDefault()
          goNextQuestion()
        }
      } else if (event.key === 'Enter') {
        event.preventDefault()
        submit()
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [dismiss, goNextQuestion, goPreviousQuestion, optionCount, questionCount, selectedIndex, submit])

  const questionText = question.question.trim() || question.header.trim() || question.id
  const primaryLabel = safeQuestionIndex + 1 < questionCount
    ? t('userInput.continue')
    : t('userInput.submit')
  const progressLabel = t('userInput.progress', {
    current: String(safeQuestionIndex + 1),
    total: String(questionCount)
  })
  const mascotSurfaceContext = desktopPluginSurfaceContext ?? {
    workspacePath: null,
    threadId: null,
    mode: 'agent',
    busy: false,
    awaitingApproval: true,
    variant: 'default',
    minimalChrome: false
  } as const

  return (
    <div style={composerDockStyle}>
      <ConversationColumn>
        <DesktopPluginSurface name="composer.before" context={mascotSurfaceContext} />
        <ComposerShell
          desktopPluginSurfaceContext={mascotSurfaceContext}
          dragOver={false}
          dropLabel=""
          onDragOver={(e) => e.preventDefault()}
          onDragLeave={(e) => e.preventDefault()}
          onDrop={(e) => e.preventDefault()}
          focused
          showMascot
          mascotInteraction={DECISION_MASCOT}
          mascotReasoningEffort={mascotEffectState.reasoningEffort}
          mascotSpeed={mascotEffectState.speed}
          mascotContextMax={mascotEffectState.contextMax}
          mascotHandoff
          editor={(
            <div style={decisionComposerBodyStyle}>
              <div style={decisionComposerTitleRowStyle}>
                <div style={questionStyle}>{questionText}</div>
                {questionCount > 1 && (
                  <div style={questionNavStyle} aria-label={progressLabel}>
                    <button
                      type="button"
                      onClick={goPreviousQuestion}
                      disabled={!canGoPreviousQuestion}
                      aria-label={t('userInput.previousQuestion')}
                      style={questionNavButtonStyle(!canGoPreviousQuestion)}
                    >
                      <ChevronLeft size={15} strokeWidth={1.9} aria-hidden="true" />
                    </button>
                    <span style={questionProgressStyle}>{progressLabel}</span>
                    <button
                      type="button"
                      onClick={goNextQuestion}
                      disabled={!canGoNextQuestion}
                      aria-label={t('userInput.nextQuestion')}
                      style={questionNavButtonStyle(!canGoNextQuestion)}
                    >
                      <ChevronRight size={15} strokeWidth={1.9} aria-hidden="true" />
                    </button>
                  </div>
                )}
              </div>
              <div style={decisionComposerChoiceListStyle}>
                {question.options.map((option, index) => (
                  <ComposerChoiceRow
                    key={`${question.id}:${option.label}:${index}`}
                    index={index}
                    label={option.label}
                    description={option.description}
                    selected={selectedIndex === index}
                    canMoveUp={canMoveUp}
                    canMoveDown={canMoveDown}
                    density="decision"
                    descriptionAriaLabel={t('userInput.optionDescriptionAria', { option: option.label || question.header })}
                    onSelect={() => {
                      if (selectedIndex === index) {
                        submit()
                      } else {
                        updateSelected(index)
                      }
                    }}
                  />
                ))}
                {hasOther && (
                  <OtherRow
                    index={otherIndex}
                    selected={isOtherSelected}
                    value={otherText[safeQuestionIndex] ?? ''}
                    secret={question.isSecret === true}
                    canMoveUp={canMoveUp}
                    canMoveDown={canMoveDown}
                    inputRef={otherInputRef}
                    onSelect={() => updateSelected(otherIndex)}
                    onChange={updateOther}
                    onSubmit={submit}
                  />
                )}
              </div>
            </div>
          )}
          footerLeading={(
            <ComposerToolbarLeadingSlots context={mascotSurfaceContext} />
          )}
          footerAction={(
            <ComposerToolbarTrailingSlots
              context={mascotSurfaceContext}
              style={decisionComposerFooterActionsStyle}
              submit={(
                <>
                  <DecisionDismissButton
                    label={t('userInput.dismiss')}
                    onClick={dismiss}
                    ariaLabel={t('userInput.dismiss')}
                  />
                  <DecisionSubmitButton label={primaryLabel} onClick={submit} />
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

const composerDockStyle: CSSProperties = {
  flexShrink: 0,
  padding: '0 clamp(20px, 4vw, 40px)'
}

function OtherRow({
  index,
  selected,
  value,
  secret,
  canMoveUp,
  canMoveDown,
  inputRef,
  onSelect,
  onChange,
  onSubmit
}: {
  index: number
  selected: boolean
  value: string
  secret: boolean
  canMoveUp: boolean
  canMoveDown: boolean
  inputRef: RefObject<HTMLInputElement | null>
  onSelect: () => void
  onChange: (value: string) => void
  onSubmit: () => void
}): JSX.Element {
  const t = useT()
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const highlighted = hovered || focused

  return (
    <div
      role="button"
      tabIndex={selected ? -1 : 0}
      aria-pressed={selected}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(event) => {
        const nextTarget = event.relatedTarget
        if (!(nextTarget instanceof Node) || !event.currentTarget.contains(nextTarget)) {
          setFocused(false)
        }
      }}
      onClick={(event) => {
        onSelect()
        if (!(event.target instanceof HTMLInputElement)) {
          inputRef.current?.focus()
        }
      }}
      onKeyDown={(event: ReactKeyboardEvent<HTMLDivElement>) => {
        if (!selected && (event.key === 'Enter' || event.key === ' ')) {
          event.preventDefault()
          onSelect()
        }
      }}
      style={composerChoiceRowStyle(selected, false, highlighted, 'decision')}
      aria-label={`${index + 1}. ${t('userInput.other')}`}
    >
      <span style={composerChoiceNumberStyle('decision')}>{index + 1}.</span>
      <div style={{ flex: '1 1 auto', minWidth: 0 }}>
        <Input
          ref={inputRef}
          bare
          type={secret ? 'password' : 'text'}
          value={value}
          onFocus={onSelect}
          onChange={(event) => onChange(event.currentTarget.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault()
              onSubmit()
            }
          }}
          placeholder={t('userInput.otherPlaceholder')}
          style={otherInputStyle(selected || focused || value.trim().length > 0)}
        />
      </div>
      {selected && <ComposerChoiceArrowHints canMoveUp={canMoveUp} canMoveDown={canMoveDown} />}
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

const questionNavStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '2px',
  minHeight: '20px',
  color: 'var(--text-dimmed)',
  flexShrink: 0
}

function questionNavButtonStyle(disabled: boolean): CSSProperties {
  return {
    width: '20px',
    height: '20px',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: '5px',
    border: '1px solid transparent',
    background: 'transparent',
    color: disabled ? 'var(--text-dimmed)' : 'var(--text-secondary)',
    opacity: disabled ? 0.4 : 1,
    cursor: disabled ? 'default' : 'pointer',
    padding: 0
  }
}

const questionStyle: CSSProperties = {
  ...decisionComposerTitleStyle
}

const questionProgressStyle: CSSProperties = {
  color: 'var(--text-dimmed)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  fontWeight: 500,
  whiteSpace: 'nowrap'
}

function otherInputStyle(active: boolean): CSSProperties {
  return {
    width: '100%',
    border: 'none',
    outline: 'none',
    background: 'transparent',
    color: active ? 'var(--text-primary)' : 'var(--text-dimmed)',
    padding: 0,
    fontSize: 'var(--text-body-size)',
    lineHeight: 'var(--text-body-line-height)',
    fontWeight: 'var(--conversation-font-weight)'
  }
}
