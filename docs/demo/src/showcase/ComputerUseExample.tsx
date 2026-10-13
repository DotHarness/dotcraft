import { useEffect, useLayoutEffect, useMemo, useRef, useSyncExternalStore, type JSX } from 'react'
import { Monitor } from 'lucide-react'
import { ApprovalDecisionComposer } from '@renderer/components/conversation/ApprovalDecisionComposer'
import { AgentResponseBlock } from '@renderer/components/conversation/AgentResponseBlock'
import { ConversationColumn } from '@renderer/components/conversation/ConversationColumn'
import { UserMessageBlock } from '@renderer/components/conversation/UserMessageBlock'
import { useT } from '@renderer/contexts/LocaleContext'
import { Button } from '@renderer/components/ui/Button'
import type { PendingApproval } from '@renderer/stores/conversationStore'
import type { ConversationItem, ConversationTurn } from '@renderer/types/conversation'
import { COMPUTER_USE_GLOW_SHADOW, COMPUTER_USE_PILL_HEIGHT, COMPUTER_USE_PILL_WIDTH, computerUsePillHtml } from '../../../../desktop/src/shared/computerUsePill'
import { AppIcon, AppWindow } from './ComputerUseApps'
import { computerUseCopy, computerUseExamples, DEFAULT_COMPUTER_USE_EXAMPLE, type ComputerUseScript } from './content/computerUse'
import { ComputerUseDirector, type Entry } from './computerUseDirector'
import { showNote, ShowcaseNote } from './notes'
import { lang, SHOWCASE_THREAD_ID, SHOWCASE_TURN_ID } from './params'
import { onPlaybackControl } from './playback'
import './computerUse.css'

const copy = computerUseCopy(lang)

type AssistantEntry = Extract<Entry, { kind: 'assistant' }>

const STOPPED_RESULT = 'Computer use was stopped by the user. (computer_use_stopped)'

function turnFor(entry: AssistantEntry): ConversationTurn {
  const items: ConversationItem[] = []
  for (const step of entry.steps) {
    const createdAt = step.startedAt
    items.push({ id: `cu-note-${step.id}`, type: 'agentMessage', phase: 'commentary', status: 'completed', text: step.text, createdAt })
    // A call whose arguments are complete but whose result hasn't arrived is the running state Desktop shows.
    const settled = step.state !== 'running'
    items.push({
      id: `cu-call-${step.id}`,
      type: 'toolCall',
      toolName: 'NodeReplJs',
      toolCallId: `cu-call-${step.id}`,
      arguments: { code: step.code },
      status: 'completed',
      success: settled ? step.state === 'done' : undefined,
      result: settled ? (step.state === 'stopped' ? STOPPED_RESULT : '') : undefined,
      duration: settled && step.completedAt ? Date.parse(step.completedAt) - Date.parse(step.startedAt) : undefined,
      createdAt,
      completedAt: step.completedAt
    })
  }
  const last = entry.steps.at(-1)?.completedAt ?? entry.startedAt
  if (entry.text) items.push({ id: `cu-reply-${entry.id}`, type: 'agentMessage', phase: 'final', status: 'completed', text: entry.text, createdAt: last })
  const running = entry.thinking || entry.steps.some((step) => step.state === 'running') || !entry.text
  return {
    id: `${SHOWCASE_TURN_ID}-${entry.id}`,
    threadId: SHOWCASE_THREAD_ID,
    status: running ? 'running' : 'completed',
    items,
    startedAt: entry.startedAt,
    completedAt: running ? undefined : last
  }
}

function ConversationEntry({ entry }: { entry: Entry }): JSX.Element {
  if (entry.kind === 'user') return <UserMessageBlock messageId={`cu-user-${entry.id}`} text={entry.text} />
  const turn = turnFor(entry)
  const running = turn.status === 'running'
  return <AgentResponseBlock turn={turn} isRunning={running} isLastTurn showIdleThinkingFallback={entry.thinking} readOnly />
}

function approvalRequest(script: ComputerUseScript, director: ComputerUseDirector): PendingApproval {
  return {
    bridgeId: 'showcase-computer-use',
    threadId: SHOWCASE_THREAD_ID,
    turnId: SHOWCASE_TURN_ID,
    requestId: `showcase-computer-use-${script.app.name}`,
    itemId: 'showcase-computer-use',
    locallySubmittedDecision: null,
    approvalType: 'computerUse',
    operation: 'use',
    target: script.app.target,
    targetLabel: script.app.name,
    reason: '',
    submit: async (value) => {
      if (value === 'acceptAlways') showNote(copy.alwaysAllowed(script.app.name))
      director.decideApproval(value !== 'decline')
    }
  }
}

function StatusPill({ active, onStop }: { active: boolean; onStop: () => void }): JSX.Element {
  const t = useT()
  const strings = { usingComputer: t('computerUse.pill.usingComputer'), escToCancel: t('computerUse.pill.escToCancel') }
  const html = useMemo(() => computerUsePillHtml(strings), [strings.usingComputer, strings.escToCancel])
  return (
    <button
      type="button"
      className="cu-pill"
      data-active={active || undefined}
      tabIndex={active ? 0 : -1}
      aria-hidden={!active}
      aria-label={`${strings.usingComputer} · ${strings.escToCancel}`}
      title={copy.stopHint}
      style={{ width: COMPUTER_USE_PILL_WIDTH, height: COMPUTER_USE_PILL_HEIGHT }}
      onClick={onStop}
    >
      <iframe className="cu-pill__frame" srcDoc={html} tabIndex={-1} title="" aria-hidden />
    </button>
  )
}

function useDirector(example: string, chapter: string | undefined): { director: ComputerUseDirector; script: ComputerUseScript } {
  const script = (computerUseExamples[example] ?? computerUseExamples[DEFAULT_COMPUTER_USE_EXAMPLE])[lang]
  // A new director per chapter, so reopening the same example at another chapter starts clean.
  const director = useMemo(() => new ComputerUseDirector(script, lang), [script, chapter])
  useEffect(() => {
    if (chapter) director.openAt(chapter)
    else director.start(window.parent === window)
    const unsubscribe = onPlaybackControl(() => director.current())
    return () => {
      unsubscribe()
      director.dispose()
    }
  }, [director])
  return { director, script }
}

export function ComputerUseExample({ example, chapter }: { example: string; chapter?: string }): JSX.Element {
  const { director, script } = useDirector(example, chapter)
  const scene = useSyncExternalStore(director.subscribe, director.getScene)
  const scrollRef = useRef<HTMLDivElement>(null)
  const request = useMemo(() => approvalRequest(script, director), [script, director])

  useLayoutEffect(() => {
    const scroller = scrollRef.current
    if (scroller) scroller.scrollTop = scroller.scrollHeight
  }, [scene.entries, scene.gate])

  useEffect(() => {
    if (!scene.active || scene.gate) return
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      director.stop()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [director, scene.active, scene.gate])

  return (
    <div className="cu-screen" data-active={scene.active || undefined}>
      <div className="cu-screen__windows">
        <section className="cu-window cu-chat" aria-label="DotCraft">
          <header className="cu-window__titlebar">
            <Monitor size={14} aria-hidden className="cu-app-icon" />
            <span className="cu-window__title">{script.title}</span>
          </header>
          <div ref={scrollRef} className="cu-chat__scroll">
            <ConversationColumn className="dc-conversation-column-stack">
              {scene.entries.map((entry) => <ConversationEntry key={entry.id} entry={entry} />)}
            </ConversationColumn>
          </div>
          {scene.gate === 'approval' && (
            <div className="cu-chat__dock">
              <ApprovalDecisionComposer request={request} />
            </div>
          )}
          {scene.gate === 'confirm' && script.confirm && (
            <div className="cu-chat__dock cu-reply" role="group" aria-label={copy.replyLabel}>
              <span className="cu-reply__label">{copy.replyLabel}</span>
              <div className="cu-reply__actions">
                <Button variant="primary" onClick={() => director.answerConfirm(true)}>{script.confirm.yes.label}</Button>
                <Button onClick={() => director.answerConfirm(false)}>{script.confirm.no.label}</Button>
              </div>
            </div>
          )}
          <div className="cu-chat__note"><ShowcaseNote /></div>
        </section>
        <AppWindow script={script} values={scene.values} focus={scene.focus} cursor={scene.cursor} clicks={scene.clicks} />
      </div>
      <div className="cu-taskbar" aria-hidden>
        <span className="cu-taskbar__app"><Monitor size={14} /></span>
        <span className="cu-taskbar__app"><AppIcon kind={script.app.kind} /></span>
      </div>
      <div className="cu-glow" aria-hidden data-active={scene.active || undefined} style={{ boxShadow: COMPUTER_USE_GLOW_SHADOW }} />
      <StatusPill active={scene.active} onStop={() => director.stop()} />
    </div>
  )
}
