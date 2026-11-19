import type { ThreadTask, ThreadTaskReasoningEffort } from '../../shared/threadTasks'
import { composeBrief, createChildThread, startChildTurn, type ThreadTaskConnection } from './childThreads'
import { threadTaskText } from './threadTaskPrompts'
import type { DesktopThreadTasks, OfferOutcome, ProposalRefusal } from './threadTaskService'
import { isThreadTaskId } from './threadTaskStore'
import { connectThreadProject, currentThreadProject, readProjectArgument, resolveThreadProject } from './threadProjects'

const INITIATIONS = new Set(['userAsked', 'offer', 'ownInitiative'])
const REASONING_EFFORTS = new Set(['low', 'medium', 'high', 'extraHigh', 'max', 'ultra'])
const PROPOSAL_ARGUMENTS = new Set(['proposalId', 'initiation', 'prompt', 'background', 'title'])
const MAX_TITLE_LENGTH = 200

type JsonObject = Record<string, unknown>

export interface ThreadTaskToolResult {
  success: boolean
  contentItems?: Array<{ type: 'text'; text: string }>
  structuredContent?: unknown
  errorCode?: string
  errorMessage?: string
}

export interface ThreadTaskToolCall {
  threadId?: string
  turnId?: string
  callId?: string
}

export async function createThreadTool(
  connection: ThreadTaskConnection,
  tasks: DesktopThreadTasks | undefined,
  args: JsonObject,
  call: ThreadTaskToolCall
): Promise<ThreadTaskToolResult> {
  const proposalId = typeof args.proposalId === 'string' ? args.proposalId.trim() : ''
  if (proposalId) return startProposalTool(connection, tasks, args, call, proposalId)

  if (typeof args.initiation !== 'string' || !INITIATIONS.has(args.initiation)) {
    return fail('InvalidArguments', threadTaskText.initiationRequired)
  }
  const initiation = args.initiation
  const title = trimmed(args.title)?.slice(0, MAX_TITLE_LENGTH)
  if (!title) return fail('InvalidArguments', threadTaskText.titleRequired)
  const prompt = trimmed(args.prompt)
  const background = trimmed(args.background)
  if (args.model != null && !trimmed(args.model)) {
    return fail('InvalidArguments', 'model must be a non-empty string when provided.')
  }
  if (args.reasoningEffort != null && !(typeof args.reasoningEffort === 'string' && REASONING_EFFORTS.has(args.reasoningEffort))) {
    return fail('InvalidArguments', 'reasoningEffort must be one of low, medium, high, extraHigh, max, or ultra.')
  }
  const defaults = {
    ...(trimmed(args.model) ? { model: trimmed(args.model) } : {}),
    ...(typeof args.reasoningEffort === 'string' ? { reasoningEffort: args.reasoningEffort as ThreadTaskReasoningEffort } : {})
  }
  if (initiation !== 'offer' && !prompt) return fail('InvalidArguments', threadTaskText.promptRequired(initiation))
  if (!connection.workspacePath.trim()) {
    return fail('ThreadManagementUnavailable', 'No Desktop workspace is currently open.')
  }

  const project = await resolveThreadProject(connection, readProjectArgument(args.project))
  if (initiation === 'userAsked') {
    const target = await connectThreadProject(connection, project)
    return startNow(connection, target, tasks, { title, prompt: prompt as string, background, defaults, callerThreadId: call.threadId })
  }

  const threadId = call.threadId?.trim()
  if (!tasks || !threadId) {
    return fail('ThreadManagementUnavailable', 'Task cards are not available for this call.')
  }
  const base = {
    threadId,
    workspacePath: connection.workspacePath,
    ...(project.id === currentThreadProject(connection).id ? {} : { targetProjectId: project.id, targetWorkspacePath: project.path }),
    title,
    defaults,
    ...(call.callId ? { toolCallId: call.callId } : {}),
    ...(call.turnId ? { turnId: call.turnId } : {})
  }

  if (initiation === 'ownInitiative') {
    if (!(await tasks.canSeeCard(connection, threadId, call.turnId))) return ok(threadTaskText.nothingProposed)
    const added = tasks.propose({ ...base, prompt, background })
    return ok(threadTaskText.proposed(added.task, added.evicted, added.others), { task: added.task })
  }

  const decision = await tasks.offerDecision(connection, threadId, call.turnId)
  if (decision === 'nobody') return ok(threadTaskText.noCardNobody)
  if (decision === 'alwaysHere') return ok(threadTaskText.noCardAlwaysHere)
  const outcome = await tasks.runOffer(connection, base, decision)
  return ok(offerOutcomeText(outcome), {
    task: outcome.task,
    outcome: outcome.kind === 'kept' && outcome.typedPast ? 'typedPast' : outcome.kind
  })
}

export function dismissProposalTool(
  tasks: DesktopThreadTasks | undefined,
  args: JsonObject,
  call: ThreadTaskToolCall
): ThreadTaskToolResult {
  const taskId = trimmed(args.taskId) ?? ''
  if (!isThreadTaskId(taskId)) return fail('InvalidArguments', threadTaskText.dismissFormat)
  const threadId = call.threadId?.trim()
  if (!tasks || !threadId) return fail('ThreadManagementUnavailable', 'Task cards are not available for this call.')
  switch (tasks.dismiss(threadId, taskId)) {
    case 'dismissed':
      return ok(threadTaskText.dismissed(taskId))
    case 'alreadyStarted':
      return ok(threadTaskText.dismissAlreadyStarted(taskId))
    case 'alreadyDismissed':
      return ok(threadTaskText.dismissAlreadyDismissed(taskId))
    case 'expired':
      return ok(threadTaskText.dismissExpired(taskId))
    case 'starting':
      return ok(threadTaskText.dismissStarting(taskId))
    case 'notFound':
      return ok(threadTaskText.dismissNotFound(taskId))
  }
}

async function startProposalTool(
  connection: ThreadTaskConnection,
  tasks: DesktopThreadTasks | undefined,
  args: JsonObject,
  call: ThreadTaskToolCall,
  proposalId: string
): Promise<ThreadTaskToolResult> {
  if (!isThreadTaskId(proposalId)) return fail('InvalidArguments', threadTaskText.proposalIdFormat)
  const threadId = call.threadId?.trim()
  if (!tasks || !threadId) return fail('ThreadManagementUnavailable', 'Task cards are not available for this call.')
  const extras = Object.keys(args).filter((key) => !PROPOSAL_ARGUMENTS.has(key) && args[key] !== undefined && args[key] !== null)
  const prompt = trimmed(args.prompt)
  if (extras.length > 0 && !prompt) return fail('InvalidArguments', threadTaskText.proposalExtras(extras))

  const outcome = await tasks.startProposal(connection, threadId, proposalId, {
    ...(prompt ? { prompt } : {}),
    ...(trimmed(args.background) ? { background: trimmed(args.background) } : {})
  })
  if (outcome.kind === 'refused') return fail('InvalidArguments', refusalText(outcome.reason, proposalId))
  if (outcome.kind === 'failed') {
    return fail(outcome.code ?? 'AppServerRequestFailed', outcome.message, outcome.thread ? { thread: outcome.thread, turn: null, started: false } : undefined)
  }
  return ok(threadTaskText.started(outcome.task.title, outcome.thread.id, extras), {
    thread: outcome.thread,
    turn: outcome.turn,
    started: Boolean(outcome.turn)
  })
}

async function startNow(
  source: ThreadTaskConnection,
  target: ThreadTaskConnection,
  tasks: DesktopThreadTasks | undefined,
  spec: {
    title: string
    prompt: string
    background?: string
    defaults: ThreadTask['defaults']
    callerThreadId?: string
  }
): Promise<ThreadTaskToolResult> {
  const childSpec = {
    title: spec.title,
    callerThreadId: spec.callerThreadId,
    defaults: spec.defaults,
    folderSeed: spec.prompt
  }
  const thread = await (tasks ? tasks.createLinkedChild(source, target, childSpec) : createChildThread(target, childSpec))
  const threadId = thread.id
  try {
    const turn = await startChildTurn(target, threadId, composeBrief(spec.prompt, spec.background))
    return ok(threadTaskText.started(spec.title, threadId), { thread, turn, started: Boolean(turn) })
  } catch (error) {
    return fail(
      'AppServerRequestFailed',
      `Created chat "${spec.title}" (${threadId}) but failed to start its first turn: ${error instanceof Error ? error.message : String(error)}`,
      { thread, turn: null, started: false }
    )
  }
}

function offerOutcomeText(outcome: OfferOutcome): string {
  switch (outcome.kind) {
    case 'accepted':
      return threadTaskText.offerAccepted(outcome.task)
    case 'kept':
      return outcome.typedPast ? threadTaskText.offerTypedPast(outcome.task.id) : threadTaskText.offerKept(outcome.task.id)
    case 'unstarted':
      return threadTaskText.offerUnstarted(outcome.task.id)
    case 'abandoned':
      return threadTaskText.offerAbandoned(outcome.task.id)
  }
}

function refusalText(reason: ProposalRefusal, taskId: string): string {
  switch (reason) {
    case 'notFound':
      return threadTaskText.proposalNotFound(taskId)
    case 'notPending':
      return threadTaskText.proposalNotPending(taskId)
    case 'dismissed':
      return threadTaskText.proposalDismissed(taskId)
    case 'expired':
      return threadTaskText.proposalExpired(taskId)
    case 'starting':
      return threadTaskText.proposalStarting(taskId)
    case 'briefRequired':
      return threadTaskText.briefRequired(taskId)
  }
}

function trimmed(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value.trim() : undefined
}

function ok(text: string, structuredContent?: unknown): ThreadTaskToolResult {
  return {
    success: true,
    contentItems: [{ type: 'text', text }],
    ...(structuredContent === undefined ? {} : { structuredContent })
  }
}

function fail(errorCode: string, errorMessage: string, structuredContent?: unknown): ThreadTaskToolResult {
  return {
    success: false,
    errorCode,
    errorMessage,
    ...(structuredContent === undefined ? {} : { structuredContent })
  }
}
