import { setTimeout as delay } from 'node:timers/promises'
import { readAppServerErrorFields } from '../../shared/appServerError'
import {
  linkedChatRefKey,
  LinkedChatStore,
  viewLinkedChat,
  type ChatReport,
  type LinkedChat,
  type LinkedChatChild,
  type LinkedChatOutcome,
  type LinkedChatRef,
  type LinkedChatStatus,
  type LinkedChatWait,
  type StoredLinkedChat
} from './linkedChatStore'

export type { LinkedChat, LinkedChatChild, LinkedChatRef, LinkedChatStatus } from './linkedChatStore'

export interface LinkedChatConnection {
  projectId: string
  workspacePath: string
  request<T = unknown>(method: string, params?: unknown): Promise<T>
}

interface RuntimeSnapshot {
  activeTurnId?: string | null
  running?: boolean
  waitingOnApproval?: boolean
  waitingOnInput?: boolean
  waitingOnPlanConfirmation?: boolean
  hasBackgroundWork?: boolean
}

export interface LinkedTurn {
  id: string
  startedAt?: string
  status: string
  items?: Array<{ type?: string; payload?: { text?: string; phase?: string; deliveryMode?: string } }>
}

export interface ThreadHeaderResult {
  thread?: { status?: string; displayName?: string | null; runtime?: RuntimeSnapshot }
}

interface ProjectConnection {
  connection: LinkedChatConnection
  parentChecks: Map<string, Promise<boolean | 'retry'>>
  lifetime: AbortController
}

interface Observation {
  revision: number
  runtime?: RuntimeSnapshot
  refreshHeader: boolean
  inspectLatest: boolean
  endedTurns: Set<string>
  pendingWaits: Map<string, { turnId: string; kind: LinkedChatWait; timer: ReturnType<typeof setTimeout> }>
  observing: boolean
  observeAgain: boolean
  delivering: boolean
  deliverAgain: boolean
}

export const DEFAULT_WAIT_SETTLE_MS = 10_000
export const DEFAULT_COMPLETION_BATCH_MS = 1_500

export interface DesktopLinkedChatsOptions {
  waitSettleMs?: number
  completionBatchMs?: number
  isParentPaused?: (parent: LinkedChatRef) => boolean
}

const WAIT_ACTION: Record<LinkedChatWait, string> = {
  approval: 'an approval',
  input: 'your input',
  planConfirmation: 'plan confirmation'
}

export class DesktopLinkedChats {
  private readonly store: LinkedChatStore
  private readonly projects = new Map<string, Map<LinkedChatConnection, ProjectConnection>>()
  private readonly observations = new WeakMap<StoredLinkedChat, Observation>()
  private readonly completionBatches = new Map<string, ReturnType<typeof setTimeout>>()
  private readonly waitSettleMs: number
  private readonly completionBatchMs: number
  private readonly isParentPaused?: (parent: LinkedChatRef) => boolean

  constructor(
    filePath: string | null,
    private readonly onConnectionNeedsChanged?: () => void,
    onLinksChanged?: () => void,
    options?: DesktopLinkedChatsOptions
  ) {
    this.store = new LinkedChatStore(filePath, onLinksChanged)
    this.waitSettleMs = options?.waitSettleMs ?? DEFAULT_WAIT_SETTLE_MS
    this.completionBatchMs = options?.completionBatchMs ?? DEFAULT_COMPLETION_BATCH_MS
    this.isParentPaused = options?.isParentPaused
  }

  link(parent: LinkedChatRef, child: LinkedChatChild): void {
    this.track(this.store.add(parent, child))
  }

  follow(parent: LinkedChatRef, child: LinkedChatChild, latest?: LinkedTurn): 'followed' | 'alreadyLinked' | 'cycle' {
    if (this.store.get({ parent, child })) return 'alreadyLinked'
    if (this.store.wouldCycle(parent, child)) return 'cycle'
    const outcome = latest && terminalStatus(latest)
    const link = this.store.add(parent, child, {
      followed: true,
      ...(outcome && {
        status: outcome,
        lastOutcome: outcome,
        lastReportedTurnId: latest.id,
        lastReportedTurnStartedAt: latest.startedAt
      })
    })
    this.track(link)
    return 'followed'
  }

  unfollow(link: LinkedChat): boolean {
    const stored = this.store.get(link)
    if (!this.store.removeFollow(link)) return false
    if (stored) this.clearPendingWaits(stored)
    this.onConnectionNeedsChanged?.()
    return true
  }

  removeForParent(parentThreadId: string): boolean {
    for (const link of this.store.all()) {
      if (link.parent.threadId !== parentThreadId) continue
      this.clearPendingWaits(link)
      this.clearCompletionBatch(link.parent)
    }
    const changed = this.store.removeForParent(parentThreadId)
    if (changed) this.onConnectionNeedsChanged?.()
    return changed
  }

  flushParent(parentThreadId: string): void {
    const parents = new Map<string, LinkedChatRef>()
    for (const link of this.store.all()) {
      if (link.parent.threadId === parentThreadId && link.pending.length > 0) {
        parents.set(linkedChatRefKey(link.parent), link.parent)
      }
    }
    for (const parent of parents.values()) {
      this.clearCompletionBatch(parent)
      this.flushCompletionBatch(parent)
      for (const link of this.store.all()) {
        if (sameChat(link.parent, parent) && link.pending.length > 0) this.deliver(link)
      }
    }
  }

  private track(link: StoredLinkedChat): void {
    if (this.primary(link.child.projectId)) this.reconcile(link)
    this.onConnectionNeedsChanged?.()
  }

  list(parent: LinkedChatRef): LinkedChat[] {
    return this.store.all().filter(link => sameChat(link.parent, parent)).reverse().map(viewLinkedChat)
  }

  listForThread(threadId: string): LinkedChat[] {
    return this.store.all().filter(link => link.parent.threadId === threadId).reverse().map(viewLinkedChat)
  }

  find(parent: LinkedChatRef, threadId: string, projectId?: string): LinkedChat | undefined {
    const link = this.store.all().findLast(({ parent: source, child }) => sameChat(source, parent) &&
      child.threadId === threadId && (projectId === undefined || child.projectId === projectId))
    return link && viewLinkedChat(link)
  }

  linkedProjectBetween(source: LinkedChatRef, targetThreadId: string): string | undefined {
    for (const { parent, child, followed } of this.store.all().reverse()) {
      if (targetThreadId === child.threadId && sameChat(source, parent)) return child.projectId
      if (!followed && targetThreadId === parent.threadId && sameChat(source, child)) return parent.projectId
    }
  }

  markStopped(view: LinkedChat, turnId: string): void {
    const link = this.store.get(view)
    if (!link) return
    link.stoppedTurnId = turnId
    this.store.save()
  }

  clearStopped(view: LinkedChat, turnId: string): void {
    const link = this.store.get(view)
    if (link?.stoppedTurnId !== turnId) return
    delete link.stoppedTurnId
    this.store.save()
  }

  needsProjectConnection(projectId: string): boolean {
    return this.store.all().some(link => (link.child.projectId === projectId && holdsForStatus(link.status)) ||
      (link.parent.projectId === projectId && link.pending.length > 0))
  }

  holdsConnection(connection: LinkedChatConnection): boolean {
    if (this.primary(connection.projectId)?.connection !== connection) return false
    return this.store.all().some(link => {
      const state = this.observations.get(link)
      return (link.child.projectId === connection.projectId &&
        (holdsForStatus(link.status) || state?.observing)) ||
        (link.parent.projectId === connection.projectId &&
          (state?.delivering || this.completionBatches.has(linkedChatRefKey(link.parent))))
    })
  }

  connected(connection: LinkedChatConnection): void {
    const projectId = connection.projectId
    const project = this.projects.get(projectId) ?? new Map<LinkedChatConnection, ProjectConnection>()
    if (project.has(connection)) return
    project.set(connection, { connection, parentChecks: new Map(), lifetime: new AbortController() })
    this.projects.set(projectId, project)
    if (project.size === 1) this.reconcileProject(projectId)
  }

  disconnected(connection: LinkedChatConnection): void {
    const projectId = connection.projectId
    const project = this.projects.get(projectId)
    const disconnected = project?.get(connection)
    if (!project || !disconnected) return
    const wasPrimary = this.primary(projectId) === disconnected
    project.delete(connection)
    disconnected.lifetime.abort()
    if (project.size === 0) this.projects.delete(projectId)
    if (wasPrimary) this.reconcileProject(projectId)
  }

  handleNotification(connection: LinkedChatConnection, method: string, params: unknown): void {
    if (this.primary(connection.projectId)?.connection !== connection) return
    if (method !== 'thread/status/changed' && method !== 'thread/deleted' &&
      method !== 'thread/archived' && method !== 'thread/renamed') return

    const event = params as { threadId?: string; displayName?: string | null; runtime?: RuntimeSnapshot } | null
    if (!event?.threadId) return
    const ref = { threadId: event.threadId, projectId: connection.projectId, workspacePath: connection.workspacePath }
    if (method === 'thread/deleted' || method === 'thread/archived') {
      this.remove(ref)
      return
    }
    for (const link of this.store.all()) {
      if (!sameChat(link.child, ref)) continue
      if (method === 'thread/renamed') {
        this.rename(link, event.displayName)
      } else if (event.runtime) {
        const state = this.observation(link)
        this.acceptRuntime(link, state, event.runtime)
        this.observe(link, state)
      }
    }
  }

  private reconcileProject(projectId: string): void {
    const primary = this.primary(projectId)
    if (primary) {
      for (const link of this.store.all()) {
        if (link.child.projectId === projectId) this.reconcile(link)
        if (link.parent.projectId === projectId) {
          void this.checkParent(primary, link.parent)
          this.deliver(link)
        }
      }
    }
    this.onConnectionNeedsChanged?.()
  }

  private reconcile(link: StoredLinkedChat): void {
    const state = this.observation(link)
    state.refreshHeader = true
    state.inspectLatest = true
    this.observe(link, state)
  }

  private observation(link: StoredLinkedChat): Observation {
    let state = this.observations.get(link)
    if (!state) {
      state = {
        revision: 0, refreshHeader: false, inspectLatest: false, endedTurns: new Set(),
        pendingWaits: new Map(),
        observing: false, observeAgain: false, delivering: false, deliverAgain: false
      }
      this.observations.set(link, state)
    }
    return state
  }

  private clearPendingWaits(link: StoredLinkedChat): void {
    const state = this.observations.get(link)
    if (!state) return
    for (const pending of state.pendingWaits.values()) clearTimeout(pending.timer)
    state.pendingWaits.clear()
  }

  private clearCompletionBatch(parent: LinkedChatRef): void {
    const key = linkedChatRefKey(parent)
    const timer = this.completionBatches.get(key)
    if (!timer) return
    clearTimeout(timer)
    this.completionBatches.delete(key)
  }

  private primary(projectId: string): ProjectConnection | undefined {
    return this.projects.get(projectId)?.values().next().value
  }

  private current(project: ProjectConnection, link: StoredLinkedChat): boolean {
    return this.primary(project.connection.projectId) === project && this.store.get(link) === link
  }

  private observe(link: StoredLinkedChat, state: Observation): void {
    if (!state.refreshHeader && !state.inspectLatest && (!state.endedTurns.size || state.runtime?.hasBackgroundWork)) return
    if (state.observing) {
      state.observeAgain = true
      return
    }
    state.observing = true
    void (async () => {
      do {
        state.observeAgain = false
        const project = this.primary(link.child.projectId)
        if (!project || !this.current(project, link)) return
        try {
          if (state.refreshHeader) {
            const revision = state.revision
            const title = link.child.title
            state.refreshHeader = false
            const { thread } = await project.connection.request<ThreadHeaderResult>('thread/read', { threadId: link.child.threadId })
            if (!this.current(project, link)) return
            if (thread?.status === 'archived') {
              this.remove(link.child)
              return
            }
            if (title === link.child.title) this.rename(link, thread?.displayName)
            if (revision === state.revision) {
              state.runtime = undefined
              if (thread?.runtime) this.acceptRuntime(link, state, thread.runtime)
            }
          }
          await this.inspectTurns(project, link, state)
        } catch (error) {
          if (this.current(project, link)) {
            if (isThreadNotFound(error)) this.remove(link.child)
            else state.inspectLatest = true
          }
        }
      } while (state.observeAgain)
    })().finally(() => {
      state.observing = false
      if (state.observeAgain) this.observe(link, state)
      else this.onConnectionNeedsChanged?.()
    })
  }

  private acceptRuntime(link: StoredLinkedChat, state: Observation, runtime: RuntimeSnapshot): void {
    const previous = state.runtime
    state.revision++
    state.runtime = runtime
    this.pruneResolvedPendingWaits(state, runtime)
    const ended = previous?.activeTurnId
    if (ended && ended !== runtime.activeTurnId && ended !== link.lastReportedTurnId) state.endedTurns.add(ended)
    if ((!runtime.activeTurnId && !runtime.hasBackgroundWork) ||
      (previous?.hasBackgroundWork && !runtime.hasBackgroundWork) ||
      (runtime.waitingOnPlanConfirmation && !previous?.waitingOnPlanConfirmation)) state.inspectLatest = true

    if (runtime.activeTurnId) {
      if (runtime.waitingOnApproval) this.stageWait(link, runtime.activeTurnId, 'approval')
      if (runtime.waitingOnInput) this.stageWait(link, runtime.activeTurnId, 'input')
    }
    this.updateStatus(link, state)
    if (link.pending.length && !this.completionBatches.has(linkedChatRefKey(link.parent))) this.deliver(link)
  }

  private pruneResolvedPendingWaits(state: Observation, runtime: RuntimeSnapshot): void {
    for (const [key, entry] of state.pendingWaits.entries()) {
      if (!isWaitStillActive(runtime, entry.turnId, entry.kind)) {
        clearTimeout(entry.timer)
        state.pendingWaits.delete(key)
      }
    }
  }

  private async inspectTurns(project: ProjectConnection, link: StoredLinkedChat, state: Observation): Promise<void> {
    const background = state.runtime?.hasBackgroundWork === true
    if (!state.inspectLatest && (!state.endedTurns.size || background)) return

    const revision = state.revision
    state.inspectLatest = false
    const known = background ? new Set<string>() : new Set(state.endedTurns)
    let cursor: string | undefined
    let latest: LinkedTurn | undefined
    const ended: LinkedTurn[] = []
    do {
      const page = await project.connection.request<{ data: LinkedTurn[]; nextCursor?: string | null }>('thread/turns/list', {
        threadId: link.child.threadId,
        itemsView: 'summary',
        sortDirection: 'descending',
        limit: known.size ? 20 : 1,
        ...(cursor ? { cursor } : {})
      })
      if (!this.current(project, link)) return
      latest ??= page.data[0]
      for (const turn of page.data) {
        if (known.delete(turn.id) && terminalStatus(turn)) ended.push(turn)
      }
      cursor = page.nextCursor ?? undefined
    } while (known.size && cursor)

    if (!latest) return
    const latestTerminal = terminalStatus(latest)
    const earlier = isEarlier(latest.startedAt, link.lastReportedTurnStartedAt)
    if (latestTerminal && !earlier && !ended.some(turn => turn.id === latest?.id)) ended.unshift(latest)

    if (revision === state.revision && !state.runtime && !latestTerminal) {
      this.acceptRuntime(link, state, {
        activeTurnId: latest.id,
        running: latest.status === 'running',
        waitingOnApproval: latest.status === 'waitingApproval',
        waitingOnInput: latest.status === 'waitingInput'
      })
    }

    if (state.runtime?.hasBackgroundWork) {
      for (const turn of ended) if (turn.id !== link.lastReportedTurnId) state.endedTurns.add(turn.id)
    } else {
      const outcome = latestTerminal ?? (ended[0] && terminalStatus(ended[0]))
      if (outcome && outcome !== link.lastOutcome) {
        link.lastOutcome = outcome
        this.store.save()
      }
      for (const turn of ended.reverse()) {
        state.endedTurns.delete(turn.id)
        this.stageEnding(link, turn)
      }
      for (const missing of known) state.endedTurns.delete(missing)
    }
    if (!earlier && revision === state.revision && state.runtime?.waitingOnPlanConfirmation && latestTerminal === 'completed') {
      this.stageWait(link, latest.id, 'planConfirmation', finalReply(latest))
    }
    this.updateStatus(link, state)
  }

  private updateStatus(link: StoredLinkedChat, state: Observation): void {
    const runtime = state.runtime
    const waitingFor: LinkedChatWait | undefined = runtime?.waitingOnApproval ? 'approval'
      : runtime?.waitingOnInput ? 'input'
        : runtime?.waitingOnPlanConfirmation ? 'planConfirmation'
          : undefined
    const status: LinkedChatStatus = waitingFor
      ? 'waitingForInput'
      : runtime?.activeTurnId || runtime?.running || runtime?.hasBackgroundWork
        ? 'running'
        : link.lastOutcome ?? 'idle'
    if (link.status !== status || link.waitingFor !== waitingFor) {
      link.status = status
      if (waitingFor) link.waitingFor = waitingFor
      else delete link.waitingFor
      this.store.save()
      this.onConnectionNeedsChanged?.()
    }
  }

  private stageWait(link: StoredLinkedChat, turnId: string, kind: LinkedChatWait, result?: string): void {
    if (link.reportedWaits?.turnId === turnId && link.reportedWaits.kinds.includes(kind)) return
    const notificationId = reportId(link, turnId, kind)
    if (link.pending.some(candidate => candidate.notificationId === notificationId)) return
    const report: ChatReport = {
      notificationId, turnId, status: 'waitingForInput', wait: kind,
      summary: `${describeChat(link)} is waiting for ${WAIT_ACTION[kind]}.`,
      result
    }
    if (this.waitSettleMs <= 0) {
      this.stage(link, report)
      return
    }
    const state = this.observation(link)
    const waitKey = `${turnId}:${kind}`
    if (state.pendingWaits.has(waitKey)) return
    const timer = setTimeout(() => {
      state.pendingWaits.delete(waitKey)
      if (this.store.get(link) !== link) return
      if (!isWaitStillActive(state.runtime, turnId, kind)) return
      this.stage(link, report)
    }, this.waitSettleMs)
    timer.unref?.()
    state.pendingWaits.set(waitKey, { turnId, kind, timer })
  }

  private stageEnding(link: StoredLinkedChat, turn: LinkedTurn): void {
    const status = terminalStatus(turn)
    if (!status || turn.id === link.lastReportedTurnId || link.pending.some(report => report.notificationId === reportId(link, turn.id))) return
    const ending = status === 'stopped'
      ? link.stoppedTurnId === turn.id ? 'was stopped by this chat' : 'was cancelled'
      : status
    if (link.stoppedTurnId === turn.id) delete link.stoppedTurnId
    this.stage(link, {
      notificationId: reportId(link, turn.id), turnId: turn.id, turnStartedAt: turn.startedAt, status,
      summary: `${describeChat(link)} ${ending}.`,
      result: finalReply(turn)
    })
  }

  private stage(link: StoredLinkedChat, report: ChatReport): void {
    if (link.pending.some(candidate => candidate.notificationId === report.notificationId)) return
    link.pending.push(report)
    this.store.save()
    if (!report.wait && this.shouldBatchCompletion(link)) {
      this.scheduleCompletionBatch(link.parent)
      this.onConnectionNeedsChanged?.()
      return
    }
    this.deliver(link)
    this.onConnectionNeedsChanged?.()
  }

  private shouldBatchCompletion(link: StoredLinkedChat): boolean {
    if (this.completionBatchMs <= 0 || this.isParentPaused?.(link.parent)) return false
    const parentKey = linkedChatRefKey(link.parent)
    if (this.completionBatches.has(parentKey)) return true
    return this.store.all().some(sibling =>
      sibling !== link &&
      sameChat(sibling.parent, link.parent) &&
      (sibling.status === 'running' || sibling.pending.some(report => !report.wait))
    )
  }

  private scheduleCompletionBatch(parent: LinkedChatRef): void {
    const key = linkedChatRefKey(parent)
    if (this.completionBatches.has(key)) return
    const timer = setTimeout(() => {
      this.completionBatches.delete(key)
      this.flushCompletionBatch(parent)
    }, this.completionBatchMs)
    timer.unref?.()
    this.completionBatches.set(key, timer)
  }

  private flushCompletionBatch(parent: LinkedChatRef): void {
    if (this.isParentPaused?.(parent)) return
    const project = this.primary(parent.projectId)
    if (!project) return
    const ready = this.store.all().filter(link =>
      sameChat(link.parent, parent) &&
      this.current(project, link) &&
      !this.observation(link).delivering &&
      link.pending.length > 0 &&
      !link.pending[0].wait
    )
    if (ready.length === 0) return
    if (ready.length === 1) {
      this.deliver(ready[0])
      return
    }
    const entries = ready.map(link => ({ link, state: this.observation(link), report: link.pending[0] }))
    for (const { state } of entries) state.delivering = true
    void (async () => {
      const sent = await this.sendBatchedReports(project, parent, entries)
      if (sent === true) {
        for (const { link, report } of entries) {
          if (this.store.get(link) !== link || link.pending[0] !== report) continue
          link.pending.shift()
          if (!isEarlier(report.turnStartedAt, link.lastReportedTurnStartedAt)) {
            link.lastReportedTurnId = report.turnId
            link.lastReportedTurnStartedAt = report.turnStartedAt
          }
        }
        this.store.save()
      } else if (sent === 'retry') {
        for (const { state } of entries) state.deliverAgain = true
      }
    })().finally(() => {
      for (const { link, state } of entries) {
        const again = state.deliverAgain || link.pending.length > 0
        state.delivering = false
        state.deliverAgain = false
        if (again && this.store.get(link) === link) this.deliver(link)
      }
      this.onConnectionNeedsChanged?.()
    })
  }

  private deliver(link: StoredLinkedChat): void {
    if (!link.pending.length) return
    if (this.isParentPaused?.(link.parent)) return
    const state = this.observation(link)
    if (state.delivering) {
      state.deliverAgain = true
      return
    }
    const project = this.primary(link.parent.projectId)
    if (!project || !this.current(project, link)) return
    state.delivering = true
    void (async () => {
      let retryDelayMs = 1000
      while (this.current(project, link) && link.pending.length && !this.isParentPaused?.(link.parent)) {
        const report = link.pending[0]
        const sent = await this.sendReport(project, link, report)
        if (!this.current(project, link) || sent === false) return
        if (sent === 'retry') {
          await delay(retryDelayMs, undefined, { signal: project.lifetime.signal, ref: false }).catch(() => {})
          retryDelayMs = Math.min(retryDelayMs * 2, 30_000)
          continue
        }
        retryDelayMs = 1000
        link.pending.shift()
        if (report.wait) {
          const kinds = link.reportedWaits?.turnId === report.turnId ? link.reportedWaits.kinds : []
          link.reportedWaits = { turnId: report.turnId, kinds: [...kinds, report.wait] }
        } else if (!isEarlier(report.turnStartedAt, link.lastReportedTurnStartedAt)) {
          link.lastReportedTurnId = report.turnId
          link.lastReportedTurnStartedAt = report.turnStartedAt
        }
        this.store.save()
      }
    })().finally(() => {
      const again = state.deliverAgain
      state.delivering = false
      state.deliverAgain = false
      if (again) this.deliver(link)
      this.onConnectionNeedsChanged?.()
    })
  }

  private async sendBatchedReports(
    project: ProjectConnection,
    parent: LinkedChatRef,
    entries: Array<{ link: StoredLinkedChat; report: ChatReport }>
  ): Promise<boolean | 'retry'> {
    const parentOk = await this.checkParent(project, parent)
    if (parentOk !== true) return parentOk
    if (this.primary(parent.projectId) !== project) return false
    const notificationId = JSON.stringify(['batch', ...entries.map(e => e.report.notificationId).sort()])
    const status: LinkedChatOutcome = entries.some(e => e.report.status === 'failed')
      ? 'failed'
      : entries.some(e => e.report.status === 'completed')
        ? 'completed'
        : 'stopped'
    const summary = entries.map(e => e.report.summary).join(' ')
    const combinedResult = formatBatchedResult(entries)
    try {
      await project.connection.request('turn/notify', {
        threadId: parent.threadId,
        notificationId,
        refId: entries[0].link.child.threadId,
        label: `${entries.length} chats finished`,
        status,
        summary,
        ...(combinedResult ? { result: combinedResult } : {})
      })
      return true
    } catch (error) {
      if (this.primary(parent.projectId) === project && isThreadNotFound(error)) this.remove(parent)
      return isTransientDeliveryError(error) ? 'retry' : false
    }
  }

  private async sendReport(project: ProjectConnection, link: StoredLinkedChat, report: ChatReport): Promise<boolean | 'retry'> {
    const parent = await this.checkParent(project, link.parent)
    if (parent !== true) return parent
    if (!this.current(project, link)) return false
    try {
      await project.connection.request('turn/notify', {
        threadId: link.parent.threadId,
        notificationId: report.notificationId,
        refId: link.child.threadId,
        label: link.child.title,
        project: link.child.projectName,
        status: report.status,
        summary: report.summary,
        ...(report.result ? { result: report.result } : {})
      })
      return true
    } catch (error) {
      if (this.current(project, link) && isThreadNotFound(error)) this.remove(link.parent)
      return isTransientDeliveryError(error) ? 'retry' : false
    }
  }

  private checkParent(project: ProjectConnection, parent: LinkedChatRef): Promise<boolean | 'retry'> {
    const key = linkedChatRefKey(parent)
    const existing = project.parentChecks.get(key)
    if (existing) return existing
    const stillCurrent = () => this.primary(parent.projectId) === project
    const check = (async (): Promise<boolean | 'retry'> => {
      try {
        const { thread } = await project.connection.request<ThreadHeaderResult>('thread/read', { threadId: parent.threadId })
        if (!stillCurrent()) return false
        if (thread?.status !== 'archived') return true
        this.remove(parent)
      } catch (error) {
        if (!stillCurrent()) return false
        if (isThreadNotFound(error)) this.remove(parent)
        else if (isTransientDeliveryError(error)) return 'retry'
      }
      return false
    })()
    project.parentChecks.set(key, check)
    void check.then(result => {
      if (result !== true) project.parentChecks.delete(key)
    })
    return check
  }

  private rename(link: StoredLinkedChat, displayName: string | null | undefined): void {
    if (!displayName || link.child.title === displayName) return
    link.child.title = displayName
    this.store.save()
  }

  private remove(ref: LinkedChatRef): void {
    const refKey = linkedChatRefKey(ref)
    for (const link of this.store.all()) {
      if (linkedChatRefKey(link.parent) === refKey || linkedChatRefKey(link.child) === refKey) {
        this.clearPendingWaits(link)
      }
    }
    this.clearCompletionBatch(ref)
    this.primary(ref.projectId)?.parentChecks.delete(refKey)
    if (this.store.remove(ref)) this.onConnectionNeedsChanged?.()
  }
}

function isWaitStillActive(runtime: RuntimeSnapshot | undefined, turnId: string, kind: LinkedChatWait): boolean {
  if (!runtime) return false
  if (kind === 'approval') return runtime.waitingOnApproval === true && runtime.activeTurnId === turnId
  if (kind === 'input') return runtime.waitingOnInput === true && runtime.activeTurnId === turnId
  return runtime.waitingOnPlanConfirmation === true
}

function formatBatchedResult(entries: Array<{ link: StoredLinkedChat; report: ChatReport }>): string | undefined {
  const parts = entries
    .filter(e => Boolean(e.report.result))
    .map(e => `[${e.link.child.projectName} · ${e.link.child.title}]\n${e.report.result}`)
  if (parts.length === 0) return undefined
  return parts.join('\n\n').slice(0, 2000).replace(/[\uD800-\uDBFF]$/u, '')
}

function sameChat(left: LinkedChatRef, right: LinkedChatRef): boolean {
  return left.threadId === right.threadId && left.projectId === right.projectId
}

function holdsForStatus(status: LinkedChatStatus): boolean {
  return status === 'running' || status === 'waitingForInput'
}

function isThreadNotFound(error: unknown): boolean {
  const { rpcCode, data } = readAppServerErrorFields(error)
  return data?.code === 'ThreadNotFound' || rpcCode === -32010
}

function isTransientDeliveryError(error: unknown): boolean {
  const { code, rpcCode } = readAppServerErrorFields(error)
  return code === 'requestTimeout' || rpcCode === -32001
}

function describeChat(link: StoredLinkedChat): string {
  return `Chat "${link.child.title}" in "${link.child.projectName}"`
}

function reportId(link: StoredLinkedChat, turnId: string, wait?: LinkedChatWait): string {
  return JSON.stringify(wait ? [link.child.threadId, turnId, wait] : [link.child.threadId, turnId])
}

function isEarlier(left?: string, right?: string): boolean {
  return !!left && !!right && Date.parse(left) < Date.parse(right)
}

function terminalStatus(turn: LinkedTurn): LinkedChatOutcome | undefined {
  switch (turn.status) {
    case 'completed': return 'completed'
    case 'failed': return 'failed'
    case 'cancelled': return 'stopped'
    default: return undefined
  }
}

function finalReply(turn: LinkedTurn): string | undefined {
  const text = turn.items?.findLast(item => item.type === 'agentMessage' && item.payload?.phase === 'final_answer' &&
    item.payload.deliveryMode !== 'async' && item.payload.text)?.payload?.text
  return text?.slice(0, 2000).replace(/[\uD800-\uDBFF]$/u, '')
}
