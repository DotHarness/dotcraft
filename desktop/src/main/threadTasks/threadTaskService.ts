import type {
  NewTaskPromptMode,
  ThreadOfferAnswer,
  ThreadTask,
  ThreadTaskActionResult,
  ThreadTaskStartOptions,
  ThreadTaskStartResult,
  ThreadTasksSnapshot
} from '../../shared/threadTasks'
import { readBrowserTurnNotification } from '../browserTabLifecycle'
import {
  composeBrief,
  createChildThread,
  startChildTurn,
  type ChildThreadWire,
  type ThreadTaskConnection
} from './childThreads'
import type { AddedThreadTask, NewThreadTask, ThreadTaskStore } from './threadTaskStore'
import { TurnAudienceTracker } from './turnAudience'

export const MAX_AUTO_STARTS = 10

type JsonObject = Record<string, unknown>

export type OfferDecision = 'nobody' | 'alwaysHere' | 'ask' | 'alwaysNewThread'

export type OfferOutcome =
  | { kind: 'accepted'; task: ThreadTask }
  | { kind: 'kept'; task: ThreadTask; typedPast: boolean }
  | { kind: 'unstarted'; task: ThreadTask }
  | { kind: 'abandoned'; task: ThreadTask }

export type DismissOutcome = 'dismissed' | 'alreadyStarted' | 'alreadyDismissed' | 'expired' | 'starting' | 'notFound'

export type ProposalRefusal = 'notFound' | 'notPending' | 'dismissed' | 'expired' | 'starting' | 'briefRequired'

export type ProposalStartOutcome =
  | { kind: 'started'; task: ThreadTask; thread: ChildThreadWire; turn: JsonObject | null }
  | { kind: 'failed'; message: string; thread?: ChildThreadWire }
  | { kind: 'refused'; reason: ProposalRefusal }

export interface ProposalBrief {
  prompt?: string
  background?: string
}

export type NewOffer = Omit<NewThreadTask, 'kind' | 'prompt' | 'background'>
export type NewProposal = Omit<NewThreadTask, 'kind'>

export interface ThreadTaskServiceHost {
  store: ThreadTaskStore
  getNewTaskPrompt(): NewTaskPromptMode
  notifyOffer?(task: ThreadTask): void
}

interface OfferWaiter {
  task: ThreadTask
  threadId: string
  turnId?: string
  resolve(outcome: OfferOutcome): void
}

const USER_INPUT_METHODS = new Set(['turn/start', 'turn/enqueue', 'turn/steer'])

export class DesktopThreadTasks {
  private readonly audience = new TurnAudienceTracker()
  private readonly waiters = new Map<string, OfferWaiter>()
  private readonly autoStarts = new Map<string, number>()

  constructor(private readonly host: ThreadTaskServiceHost) {}

  snapshot(threadId: string): ThreadTasksSnapshot {
    return this.host.store.snapshot(threadId)
  }

  handleNotification(method: string, params: unknown): void {
    this.audience.observe(method, params)
    const turn = readBrowserTurnNotification(method, params)
    if (turn?.terminal) this.abandonTurn(turn.threadId, turn.turnId)
  }

  observeUserRequest(method: string, params: unknown): void {
    if (!isRecord(params) || typeof params.threadId !== 'string') return
    const threadId = params.threadId
    if (USER_INPUT_METHODS.has(method)) {
      this.autoStarts.delete(threadId)
      for (const [taskId, waiter] of this.waiters) {
        if (waiter.threadId === threadId && this.host.store.get(threadId, taskId)?.state === 'pending') {
          this.finish(taskId, 'notAnswered', {}, (task) => ({ kind: 'kept', task, typedPast: true }))
        }
      }
    } else if (method === 'turn/interrupt' && typeof params.turnId === 'string') {
      this.abandonTurn(threadId, params.turnId)
    }
  }

  abandonAllOffers(): void {
    for (const taskId of [...this.waiters.keys()]) this.abandon(taskId)
  }

  canSeeCard(connection: ThreadTaskConnection, threadId: string, turnId: string | undefined): Promise<boolean> {
    return this.audience.fromComposer(connection, threadId, turnId)
  }

  async offerDecision(connection: ThreadTaskConnection, threadId: string, turnId: string | undefined): Promise<OfferDecision> {
    if (!(await this.canSeeCard(connection, threadId, turnId))) return 'nobody'
    const mode = this.host.getNewTaskPrompt()
    if (mode === 'here') return 'alwaysHere'
    if (mode === 'newThread' && (this.autoStarts.get(threadId) ?? 0) < MAX_AUTO_STARTS) return 'alwaysNewThread'
    return 'ask'
  }

  propose(input: NewProposal): AddedThreadTask {
    return this.host.store.add({ ...input, kind: 'proposal' })
  }

  runOffer(connection: ThreadTaskConnection, input: NewOffer, decision: 'ask' | 'alwaysNewThread'): Promise<OfferOutcome> {
    const auto = decision === 'alwaysNewThread'
    const { task } = this.host.store.add({ ...input, kind: 'offer' })
    const outcome = new Promise<OfferOutcome>((resolve) => {
      this.waiters.set(task.id, { task, threadId: task.threadId, turnId: task.turnId, resolve })
    })
    if (auto) {
      this.autoStarts.set(task.threadId, (this.autoStarts.get(task.threadId) ?? 0) + 1)
      void this.accept(connection, task.threadId, task.id, undefined)
    } else {
      this.host.notifyOffer?.(task)
    }
    return outcome
  }

  async answerOffer(
    connection: ThreadTaskConnection,
    threadId: string,
    taskId: string,
    answer: ThreadOfferAnswer
  ): Promise<ThreadTaskActionResult> {
    const task = this.host.store.get(threadId, taskId)
    if (!task || task.kind !== 'offer' || task.state !== 'pending' || !this.waiters.has(taskId)) {
      return { ok: false, error: 'This offer is no longer waiting for an answer.' }
    }
    if (answer.choice === 'here') {
      this.finish(taskId, 'keptHere', {}, (resolved) => ({ kind: 'kept', task: resolved, typedPast: false }))
      return { ok: true }
    }
    return this.accept(connection, threadId, taskId, answer.options)
  }

  async startProposal(
    connection: ThreadTaskConnection,
    threadId: string,
    taskId: string,
    brief: ProposalBrief = {},
    options?: ThreadTaskStartOptions
  ): Promise<ProposalStartOutcome> {
    const task = this.host.store.get(threadId, taskId)
    if (!task) return { kind: 'refused', reason: 'notFound' }
    if (task.state === 'starting') return { kind: 'refused', reason: 'starting' }
    if (task.kind === 'offer' && task.state === 'accepted' && task.childThreadId) {
      return brief.prompt ? this.sendAcceptedBrief(connection, task, brief) : { kind: 'refused', reason: 'briefRequired' }
    }
    if (task.kind !== 'proposal' || task.state !== 'pending') {
      return {
        kind: 'refused',
        reason: task.state === 'dismissed' ? 'dismissed' : task.state === 'expired' ? 'expired' : 'notPending'
      }
    }
    return this.startPendingProposal(connection, task, brief, options)
  }

  async startProposals(
    connection: ThreadTaskConnection,
    threadId: string,
    taskIds: string[],
    options?: ThreadTaskStartOptions
  ): Promise<ThreadTaskStartResult[]> {
    const results: ThreadTaskStartResult[] = []
    for (const taskId of taskIds) {
      const task = this.host.store.get(threadId, taskId)
      if (task?.kind !== 'proposal') {
        results.push({ taskId, ok: false, error: 'This proposal was not found.' })
        continue
      }
      const outcome = await this.startProposal(connection, threadId, taskId, {}, options)
      results.push(outcome.kind === 'started'
        ? { taskId, ok: true, childThreadId: outcome.thread.id }
        : { taskId, ok: false, error: outcome.kind === 'failed' ? outcome.message : `This proposal is ${outcome.reason}.` })
    }
    return results
  }

  dismiss(threadId: string, taskId: string): DismissOutcome {
    const task = this.host.store.get(threadId, taskId)
    if (!task) return 'notFound'
    switch (task.state) {
      case 'pending':
        if (this.waiters.has(taskId)) {
          this.finish(taskId, 'dismissed', {}, (resolved) => ({ kind: 'abandoned', task: resolved }))
        } else {
          this.host.store.update(threadId, taskId, { state: 'dismissed' })
        }
        return 'dismissed'
      case 'starting':
        return 'starting'
      case 'expired':
        return 'expired'
      case 'accepted':
      case 'started':
      case 'keptHere':
        return 'alreadyStarted'
      default:
        return 'alreadyDismissed'
    }
  }

  markKeptHere(threadId: string, taskId: string): ThreadTaskActionResult {
    const task = this.host.store.get(threadId, taskId)
    if (task?.kind !== 'proposal' || task.state !== 'pending') {
      return { ok: false, error: 'This proposal is no longer waiting.' }
    }
    this.host.store.update(threadId, taskId, { state: 'keptHere' })
    return { ok: true }
  }

  private async accept(
    connection: ThreadTaskConnection,
    threadId: string,
    taskId: string,
    options: ThreadTaskStartOptions | undefined
  ): Promise<ThreadTaskActionResult> {
    const task = this.host.store.update(threadId, taskId, { state: 'starting', error: undefined })
    if (!task) return { ok: false, error: 'This offer was not found.' }
    let thread: ChildThreadWire
    try {
      thread = await createChildThread(connection, {
        title: task.title,
        callerThreadId: threadId,
        defaults: task.defaults,
        options,
        folderSeed: task.title
      })
    } catch (error) {
      const message = errorMessage(error)
      this.finish(taskId, 'failed', { error: message }, (resolved) => ({ kind: 'unstarted', task: resolved }))
      if (!this.waiters.has(taskId)) this.host.store.update(threadId, taskId, { state: 'failed', error: message })
      return { ok: false, error: message }
    }
    const childThreadId = thread.id as string
    if (this.waiters.has(taskId)) {
      this.finish(taskId, 'accepted', { childThreadId }, (resolved) => ({ kind: 'accepted', task: resolved }))
    } else {
      this.host.store.update(threadId, taskId, { childThreadId })
    }
    return { ok: true }
  }

  private async sendAcceptedBrief(
    connection: ThreadTaskConnection,
    task: ThreadTask,
    brief: ProposalBrief
  ): Promise<ProposalStartOutcome> {
    const childThreadId = task.childThreadId as string
    const thread: ChildThreadWire = { id: childThreadId, displayName: task.title }
    this.host.store.update(task.threadId, task.id, {
      state: 'starting',
      prompt: brief.prompt,
      background: brief.background
    })
    try {
      const turn = await startChildTurn(connection, childThreadId, composeBrief(brief.prompt as string, brief.background))
      const started = this.host.store.update(task.threadId, task.id, { state: 'started' }) ?? task
      return { kind: 'started', task: started, thread, turn }
    } catch (error) {
      const message = errorMessage(error)
      this.host.store.update(task.threadId, task.id, { state: 'failed', error: message })
      return { kind: 'failed', message: `Created chat "${task.title}" (${childThreadId}) but failed to start it: ${message}`, thread }
    }
  }

  private async startPendingProposal(
    connection: ThreadTaskConnection,
    task: ThreadTask,
    brief: ProposalBrief,
    options: ThreadTaskStartOptions | undefined
  ): Promise<ProposalStartOutcome> {
    const prompt = brief.prompt ?? task.prompt ?? task.title
    const background = brief.prompt ? brief.background : task.background
    this.host.store.update(task.threadId, task.id, { state: 'starting', error: undefined })
    let thread: ChildThreadWire
    try {
      thread = await createChildThread(connection, {
        title: task.title,
        callerThreadId: task.threadId,
        defaults: task.defaults,
        options,
        folderSeed: prompt
      })
    } catch (error) {
      const message = errorMessage(error)
      this.host.store.update(task.threadId, task.id, { state: 'pending', error: message })
      return { kind: 'failed', message }
    }
    const childThreadId = thread.id as string
    this.host.store.update(task.threadId, task.id, { childThreadId, prompt, background })
    try {
      const turn = await startChildTurn(connection, childThreadId, composeBrief(prompt, background))
      const started = this.host.store.update(task.threadId, task.id, { state: 'started' }) ?? task
      return { kind: 'started', task: started, thread, turn }
    } catch (error) {
      const message = errorMessage(error)
      this.host.store.update(task.threadId, task.id, { state: 'failed', error: message })
      return { kind: 'failed', message: `Created chat "${task.title}" (${childThreadId}) but failed to start it: ${message}`, thread }
    }
  }

  private abandonTurn(threadId: string, turnId: string): void {
    for (const [taskId, waiter] of this.waiters) {
      if (waiter.threadId === threadId && waiter.turnId === turnId) this.abandon(taskId)
    }
  }

  private abandon(taskId: string): void {
    this.finish(taskId, 'notAnswered', {}, (task) => ({ kind: 'abandoned', task }))
  }

  private finish(
    taskId: string,
    state: ThreadTask['state'],
    patch: Partial<ThreadTask>,
    outcome: (task: ThreadTask) => OfferOutcome
  ): void {
    const waiter = this.waiters.get(taskId)
    if (!waiter) return
    this.waiters.delete(taskId)
    const task = this.host.store.update(waiter.threadId, taskId, { ...patch, state })
    waiter.resolve(outcome(task ?? { ...waiter.task, ...patch, state }))
  }
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

function isRecord(value: unknown): value is JsonObject {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}
