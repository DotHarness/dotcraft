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
import { sameFilePath } from '../../shared/workspaceProjectKey'
import {
  composeBrief,
  createChildThread,
  startChildTurn,
  type ChildThreadSpec,
  type ChildThread,
  type ThreadTaskConnection
} from './childThreads'
import type { AddedThreadTask, NewThreadTask, ThreadTaskStore } from './threadTaskStore'
import { TurnAudienceTracker } from './turnAudience'
import type { DesktopLinkedChats } from './linkedChats'
import { callingThread, connectThreadProject, currentThreadProject, resolveThreadProject, ThreadProjectError } from './threadProjects'

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
  | { kind: 'started'; task: ThreadTask; thread: ChildThread; turn: JsonObject | null }
  | { kind: 'failed'; message: string; thread?: ChildThread; code?: string }
  | { kind: 'refused'; reason: ProposalRefusal }

export interface ProposalBrief {
  prompt?: string
  background?: string
}

export type NewOffer = Omit<NewThreadTask, 'kind' | 'prompt' | 'background'>
export type NewProposal = Omit<NewThreadTask, 'kind'>

export interface ThreadTaskServiceHost {
  store: ThreadTaskStore
  linkedChats?: DesktopLinkedChats
  getNewTaskPrompt(): NewTaskPromptMode
  notifyOffer?(task: ThreadTask, ownerId?: number): void
  notifyWrapUpQuestion?(threadId: string, turnId: string | undefined, callId: string, ownerId?: number): void
}

interface OfferWaiter {
  ownerId?: number
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

  get linkedChats(): DesktopLinkedChats | undefined {
    return this.host.linkedChats
  }

  async createLinkedChild(source: ThreadTaskConnection, target: ThreadTaskConnection, spec: ChildThreadSpec): Promise<ChildThread> {
    const thread = await createChildThread(target, spec)
    if (spec.callerThreadId) {
      this.linkedChats?.link(callingThread(source, spec.callerThreadId), {
        ...callingThread(target, thread.id),
        projectName: currentThreadProject(target).name,
        title: thread.displayName || spec.title || thread.id
      })
    }
    return thread
  }

  snapshot(threadId: string): ThreadTasksSnapshot {
    return this.host.store.snapshot(threadId)
  }

  async reconcileQuestion(connection: ThreadTaskConnection, threadId: string): Promise<void> {
    const question = this.host.store.getQuestion(threadId)
    if (!question?.turnId) return
    try {
      const result = await connection.request<{ data: Array<{ id: string; status: string }> }>('thread/turns/list', {
        threadId,
        limit: 1,
        sortDirection: 'descending',
        itemsView: 'none'
      })
      const latest = result.data[0]
      if (!latest || this.host.store.getQuestion(threadId) !== question) return
      if (latest.id !== question.turnId || latest.status === 'failed' || latest.status === 'cancelled') {
        this.clearQuestion(threadId)
      } else if (latest.status === 'completed' && question.pending) {
        this.publishQuestion(threadId, question.turnId, question.callId, connection.ownerId)
      }
    } catch {}
  }

  clearQuestion(threadId: string): void {
    this.host.store.setQuestion(threadId, undefined)
  }

  dismissQuestion(threadId: string, callId: string): void {
    this.host.store.dismissQuestion(threadId, callId)
  }

  handleNotification(method: string, params: unknown, ownerId?: number): void {
    this.audience.observe(method, params)
    const turn = readBrowserTurnNotification(method, params)
    if (turn) this.settleQuestion(method, turn.threadId, turn.turnId, ownerId)
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

  abandonOffersForOwner(ownerId: number): void {
    for (const [taskId, waiter] of this.waiters) {
      if (waiter.ownerId === ownerId) this.abandon(taskId)
    }
  }

  canSeeCard(connection: ThreadTaskConnection, threadId: string, turnId: string | undefined): Promise<boolean> {
    return this.audience.fromComposer(connection, threadId, turnId)
  }

  fromPhone(connection: ThreadTaskConnection, threadId: string, turnId: string | undefined): Promise<boolean> {
    return this.audience.fromPhone(connection, threadId, turnId)
  }

  stageQuestion(threadId: string, turnId: string, callId: string): void {
    this.host.store.setQuestion(threadId, { turnId, callId, pending: true })
  }

  private publishQuestion(threadId: string, turnId: string, callId: string, ownerId?: number): void {
    this.host.store.setQuestion(threadId, { turnId, callId })
    this.host.notifyWrapUpQuestion?.(threadId, turnId, callId, ownerId)
  }

  private settleQuestion(method: string, threadId: string, turnId: string, ownerId?: number): void {
    const question = this.host.store.getQuestion(threadId)
    if (!question) return
    if (question.turnId !== turnId) {
      if (method === 'turn/started') this.clearQuestion(threadId)
    } else if (method === 'turn/completed' && question.pending) {
      this.publishQuestion(threadId, turnId, question.callId, ownerId)
    } else if (method === 'turn/failed' || method === 'turn/cancelled') {
      this.clearQuestion(threadId)
    }
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
      this.waiters.set(task.id, { ownerId: connection.ownerId, task, threadId: task.threadId, turnId: task.turnId, resolve })
    })
    if (auto) {
      this.autoStarts.set(task.threadId, (this.autoStarts.get(task.threadId) ?? 0) + 1)
      void this.accept(connection, task.threadId, task.id, undefined)
    } else {
      this.host.notifyOffer?.(task, connection.ownerId)
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
    if (!task || !sameFilePath(task.workspacePath, connection.workspacePath) || task.kind !== 'offer' || task.state !== 'pending' || !this.waiters.has(taskId)) {
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
    if (!task || !sameFilePath(task.workspacePath, connection.workspacePath)) return { kind: 'refused', reason: 'notFound' }
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
    let thread: ChildThread
    try {
      const target = await this.taskTarget(connection, task)
      thread = await this.createLinkedChild(connection, target, {
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
    const childThreadId = thread.id
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
    const thread: ChildThread = {
      id: childThreadId, displayName: task.title, workspacePath: task.targetWorkspacePath ?? connection.workspacePath,
      projectId: task.targetProjectId ?? currentThreadProject(connection).id
    }
    this.host.store.update(task.threadId, task.id, {
      state: 'starting',
      prompt: brief.prompt,
      background: brief.background
    })
    try {
      const target = await this.taskTarget(connection, task)
      const turn = await startChildTurn(target, childThreadId, composeBrief(brief.prompt as string, brief.background))
      const started = this.host.store.update(task.threadId, task.id, { state: 'started' }) ?? task
      return { kind: 'started', task: started, thread, turn }
    } catch (error) {
      const message = errorMessage(error)
      this.host.store.update(task.threadId, task.id, { state: 'failed', error: message })
      return { kind: 'failed', message: `Created chat "${task.title}" (${childThreadId}) but failed to start it: ${message}`, thread, code: projectErrorCode(error) }
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
    let thread: ChildThread
    let target: ThreadTaskConnection
    try {
      target = await this.taskTarget(connection, task)
      thread = await this.createLinkedChild(connection, target, {
        title: task.title,
        callerThreadId: task.threadId,
        defaults: task.defaults,
        options,
        folderSeed: prompt
      })
    } catch (error) {
      const message = errorMessage(error)
      this.host.store.update(task.threadId, task.id, { state: 'pending', error: message })
      return { kind: 'failed', message, code: projectErrorCode(error) }
    }
    const childThreadId = thread.id
    this.host.store.update(task.threadId, task.id, { childThreadId, prompt, background })
    try {
      const turn = await startChildTurn(target, childThreadId, composeBrief(prompt, background))
      const started = this.host.store.update(task.threadId, task.id, { state: 'started' }) ?? task
      return { kind: 'started', task: started, thread, turn }
    } catch (error) {
      const message = errorMessage(error)
      this.host.store.update(task.threadId, task.id, { state: 'failed', error: message })
      return { kind: 'failed', message: `Created chat "${task.title}" (${childThreadId}) but failed to start it: ${message}`, thread, code: projectErrorCode(error) }
    }
  }

  private async taskTarget(connection: ThreadTaskConnection, task: ThreadTask): Promise<ThreadTaskConnection> {
    return connectThreadProject(connection, await resolveThreadProject(connection, task.targetProjectId))
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

function projectErrorCode(error: unknown): string | undefined {
  return error instanceof ThreadProjectError ? error.code : undefined
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

function isRecord(value: unknown): value is JsonObject {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}
