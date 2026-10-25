import { useEffect, useRef, useState, type JSX, type ReactNode } from 'react'
import { ChevronDown, X } from 'lucide-react'
import { doThisTaskHereMessage, type ThreadTask } from '../../../../shared/threadTasks'
import { useT } from '../../../contexts/LocaleContext'
import { useComposerPreferencesStore } from '../../../stores/composerPreferencesStore'
import { useConversationStore } from '../../../stores/conversationStore'
import { useThreadStore } from '../../../stores/threadStore'
import { addToast } from '../../../stores/toastStore'
import { useUIStore } from '../../../stores/uiStore'
import { buildComposerInputParts } from '../../../utils/composeInputParts'
import { startTurnWithOptimisticUI } from '../../../utils/startTurn'
import { Button } from '../../ui/Button'
import { ContextMenu, type ContextMenuPosition } from '../../ui/ContextMenu'
import { IconButton } from '../../ui/IconButton'
import { RunningShimmer } from '../../ui/RunningShimmer'
import { ShortcutBadge } from '../../ui/ShortcutBadge'
import { SidebarNavIcon } from '../../sidebar/SidebarNavIcon'
import { MarkdownRenderer } from '../MarkdownRenderer'
import { ThreadTaskOptions, type ThreadTaskOptionsHandle } from './ThreadTaskOptions'
import css from './ThreadTaskCard.module.css'

const OFFER_NEW_CHAT = ['Mod', 'Enter'] as const
const OFFER_HERE = ['Esc'] as const

type T = ReturnType<typeof useT>

function reportFailure(t: T, error: string | undefined): void {
  if (error) addToast(t('threadTask.startFailed', { error }), 'error')
}

function openChat(threadId: string): void {
  useThreadStore.getState().setActiveThreadId(threadId)
  useUIStore.getState().setActiveMainView('conversation')
}

function Band({ label, meta, children }: { label: string; meta?: ReactNode; children?: ReactNode }): JSX.Element {
  return (
    <div className={css.band}>
      <span className={css.glyph} aria-hidden><SidebarNavIcon name="new-chat" size={14} /></span>
      <span className={css.bandTitle}>{label}</span>
      {meta}
      <span className={css.grow} />
      {children}
    </div>
  )
}

function statusLabel(t: T, task: ThreadTask): string {
  switch (task.state) {
    case 'starting': return t('threadTask.state.starting')
    case 'accepted': return t('threadTask.state.briefing')
    case 'keptHere': return task.kind === 'offer' ? t('threadTask.state.keptHere') : t('threadTask.state.sentHere')
    case 'dismissed': return t('threadTask.state.dismissed')
    case 'failed': return t('threadTask.state.failed')
    case 'expired': return t('threadTask.state.expired')
    default: return t('threadTask.state.notAnswered')
  }
}

function StartedRow({ task, childThreadId }: { task: ThreadTask; childThreadId: string }): JSX.Element {
  const t = useT()
  const working = useThreadStore((state) => state.runningTurnThreadIds.has(childThreadId))
  return (
    <div className={`${css.row} ${css.line}`} data-state={task.state}>
      <RunningShimmer active={working} className={css.lineTitle}>{task.title}</RunningShimmer>
      <Button variant="outlineGhost" size="toolbar" className={css.lineAction} onClick={() => openChat(childThreadId)}>
        {t('threadActionCard.openChat')}
      </Button>
    </div>
  )
}

function OutcomeRow({ task }: { task: ThreadTask }): JSX.Element {
  const t = useT()
  if (task.state === 'started' && task.childThreadId) return <StartedRow task={task} childThreadId={task.childThreadId} />
  return (
    <div className={`${css.row} ${css.line}`} data-state={task.state}>
      <span className={css.lineTitle}>{task.title}</span>
      <span className={css.status}>{statusLabel(t, task)}</span>
    </div>
  )
}

function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement) return target.value.trim() !== ''
  return target.isContentEditable && (target.textContent ?? '').trim() !== ''
}

export function ThreadTaskOfferCard({ task }: { task: ThreadTask }): JSX.Element | null {
  const t = useT()
  const options = useRef<ThreadTaskOptionsHandle | null>(null)
  const [busy, setBusy] = useState(false)
  const [menu, setMenu] = useState<ContextMenuPosition | null>(null)
  const [optionsOpen, setOptionsOpen] = useState(false)
  const saveNewTaskPrompt = useComposerPreferencesStore((state) => state.saveNewTaskPrompt)
  const waiting = task.state === 'pending'

  async function answer(choice: 'newThread' | 'here'): Promise<void> {
    if (busy) return
    setBusy(true)
    try {
      const result = choice === 'here'
        ? await window.api.threadTasks.answerOffer({ threadId: task.threadId, taskId: task.id, answer: { choice } })
        : await window.api.threadTasks.answerOffer({
          threadId: task.threadId,
          taskId: task.id,
          answer: { choice, options: await options.current?.read() }
        })
      if (!result.ok) reportFailure(t, result.error)
    } finally {
      setBusy(false)
    }
  }

  async function always(choice: 'newThread' | 'here'): Promise<void> {
    setMenu(null)
    await saveNewTaskPrompt(choice).catch(() => undefined)
    await answer(choice)
  }

  useEffect(() => {
    if (!waiting || optionsOpen || menu) return
    const onKey = (event: KeyboardEvent): void => {
      if (event.defaultPrevented || isTypingTarget(event.target)) return
      const newChat = event.key === 'Enter' && (event.metaKey || event.ctrlKey)
      if (event.key !== 'Escape' && !newChat) return
      event.preventDefault()
      event.stopPropagation()
      void answer(newChat ? 'newThread' : 'here')
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  })

  if (task.state === 'started') return null

  return (
    <div className={css.card} role="group" aria-label={t('threadTask.offerQuestion')}>
      <Band label={t('threadTask.offerQuestion')}>
        {waiting ? (
          <Button
            variant="ghost"
            size="sm"
            className={css.bandAction}
            aria-haspopup="menu"
            aria-expanded={menu !== null}
            onClick={(event) => {
              const rect = event.currentTarget.getBoundingClientRect()
              setMenu({ x: rect.right - 220, y: rect.bottom + 4 })
            }}
          >
            {t('threadTask.dontAskAgain')}
            <ChevronDown size={14} aria-hidden />
          </Button>
        ) : null}
      </Band>
      {waiting ? (
        <div className={css.row}>
          <span className={css.heading}>{task.title}</span>
          <div className={css.controls}>
            <ThreadTaskOptions workspacePath={task.workspacePath} optionsRef={options} onOpenChange={setOptionsOpen} />
            <span className={css.buttons}>
              <Button variant="ghost" size="sm" disabled={busy} onClick={() => { void answer('here') }}>
                {t('threadTask.doItHere')}
                <ShortcutBadge shortcut={OFFER_HERE} />
              </Button>
              <Button variant="primary" size="sm" disabled={busy} onClick={() => { void answer('newThread') }}>
                {t('threadActionCard.untitled')}
                <ShortcutBadge shortcut={OFFER_NEW_CHAT} tone="onAccent" />
              </Button>
            </span>
          </div>
        </div>
      ) : (
        <OutcomeRow task={task} />
      )}
      {menu ? (
        <ContextMenu
          position={menu}
          width={220}
          onClose={() => setMenu(null)}
          items={[
            { label: t('threadTask.alwaysNewChat'), onClick: () => { void always('newThread') } },
            { label: t('threadTask.alwaysHere'), onClick: () => { void always('here') } },
            { type: 'separator' },
            {
              label: t('settings.title'),
              onClick: () => {
                setMenu(null)
                useUIStore.setState({ activeMainView: 'settings' })
                useUIStore.getState().setActiveSettingsTab('general')
              }
            }
          ]}
        />
      ) : null}
    </div>
  )
}

async function sendHere(task: ThreadTask, fallbackThreadName: string): Promise<void> {
  const text = doThisTaskHereMessage(task.title)
  if (useConversationStore.getState().turnStatus === 'idle') {
    await startTurnWithOptimisticUI({
      threadId: task.threadId,
      workspacePath: task.workspacePath,
      text,
      segments: [],
      images: [],
      files: [],
      fallbackThreadName,
      throwOnStartError: true
    })
  } else {
    const { inputParts } = buildComposerInputParts({ text })
    await window.api.appServer.sendRequest('turn/enqueue', {
      clientUserMessageId: crypto.randomUUID(),
      threadId: task.threadId,
      input: inputParts
    })
  }
}

function ProposalRow({ task }: { task: ThreadTask }): JSX.Element {
  const t = useT()
  const options = useRef<ThreadTaskOptionsHandle | null>(null)
  const rowRef = useRef<HTMLDivElement>(null)
  const [brief, setBrief] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!brief) return
    const close = (event: Event): void => {
      if (event instanceof KeyboardEvent && event.key !== 'Escape') return
      if (event instanceof PointerEvent && rowRef.current?.contains(event.target as Node)) return
      setBrief(false)
    }
    window.addEventListener('pointerdown', close)
    window.addEventListener('keydown', close)
    return () => {
      window.removeEventListener('pointerdown', close)
      window.removeEventListener('keydown', close)
    }
  }, [brief])

  async function run(action: () => Promise<void>): Promise<void> {
    if (busy) return
    setBusy(true)
    try {
      await action()
    } catch (error) {
      reportFailure(t, error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
    }
  }

  const start = (): Promise<void> => run(async () => {
    const [result] = await window.api.threadTasks.start({
      threadId: task.threadId,
      taskIds: [task.id],
      options: await options.current?.read()
    })
    if (result && !result.ok) reportFailure(t, result.error)
  })

  const doItHere = (): Promise<void> => run(async () => {
    const result = await window.api.threadTasks.markKeptHere({ threadId: task.threadId, taskId: task.id })
    if (!result.ok) return reportFailure(t, result.error)
    await sendHere(task, t('threadActionCard.untitled'))
  })

  const dismiss = (): Promise<void> => run(async () => {
    const result = await window.api.threadTasks.dismiss({ threadId: task.threadId, taskId: task.id })
    if (!result.ok) reportFailure(t, result.error)
  })

  return (
    <div ref={rowRef} className={css.row}>
      <div className={css.top}>
        <button type="button" className={css.title} aria-expanded={brief} onClick={() => setBrief(!brief)}>
          {task.title}
        </button>
        <IconButton
          icon={<X size={14} aria-hidden />}
          label={t('threadTask.dismiss', { title: task.title })}
          size={24}
          disabled={busy}
          onClick={() => { void dismiss() }}
        />
      </div>
      <div className={css.controls}>
        <ThreadTaskOptions workspacePath={task.workspacePath} optionsRef={options} />
        <span className={css.buttons}>
          <Button variant="ghost" size="sm" disabled={busy} onClick={() => { void doItHere() }}>{t('threadTask.doItHere')}</Button>
          <Button variant="primary" size="sm" disabled={busy} onClick={() => { void start() }}>{t('threadTask.start')}</Button>
        </span>
      </div>
      {brief ? (
        <div className={`${css.popover} ${css.briefPopover}`} role="dialog" aria-label={task.title}>
          <span className={css.muted}>{t('threadTask.label')}</span>
          <strong>{task.title}</strong>
          <div className={css.brief}><MarkdownRenderer content={task.prompt ?? ''} /></div>
        </div>
      ) : null}
    </div>
  )
}

export function ThreadTaskProposalCard({ tasks }: { tasks: ThreadTask[] }): JSX.Element {
  const t = useT()
  const [startingAll, setStartingAll] = useState(false)
  const pending = tasks.filter((task) => task.state === 'pending')
  const started = tasks.filter((task) => task.state === 'started').length

  async function startAll(): Promise<void> {
    setStartingAll(true)
    try {
      const results = await window.api.threadTasks.start({
        threadId: pending[0].threadId,
        taskIds: pending.map((task) => task.id)
      })
      for (const result of results) if (!result.ok) reportFailure(t, result.error)
    } finally {
      setStartingAll(false)
    }
  }

  return (
    <div className={css.card} role="group" aria-label={tasks.length > 1 ? t('threadTask.count', { count: tasks.length }) : t('threadTask.label')}>
      <Band
        label={tasks.length > 1 ? t('threadTask.count', { count: tasks.length }) : t('threadTask.label')}
        meta={started > 0 ? <span className={css.muted}>{t('threadTask.startedCount', { count: started })}</span> : null}
      >
        {pending.length >= 2 ? (
          <Button variant="ghost" size="sm" className={css.bandAction} disabled={startingAll} onClick={() => { void startAll() }}>
            {t('threadTask.startAll')}
          </Button>
        ) : null}
      </Band>
      {tasks.map((task) => task.state === 'pending'
        ? <ProposalRow key={task.id} task={task} />
        : <OutcomeRow key={task.id} task={task} />)}
    </div>
  )
}
