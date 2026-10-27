import { isComplete, type HistoryItem, type HistoryTurn } from './history'
import { dividerOf, failureEntry, originOf, threadActionOf } from './timelineMarkers'
import { baseName, entryId, imageUri, planEntry, str, subagentEndingEntry, toolEntry } from './transcriptTools'
import type { NoticeKind, TaskWake, TranscriptEntry } from './transcriptTypes'
import { userImages, userSegments } from './userSegments'

interface ItemContext {
  result?: HistoryItem
  execution?: HistoryItem
  approval?: HistoryItem
  turn?: HistoryTurn
  fallbackOrigin: string | null
}

export interface ItemProjection {
  entry?: TranscriptEntry
  heading?: string | null
  wake?: TaskWake
  action?: Extract<TranscriptEntry, { kind: 'threadAction' }>
}

const DONE = new Set(['completed', 'failed', 'cancelled'])

function approvalSubject(request: HistoryItem | undefined): string | undefined {
  if (!request) return undefined
  const payload = request.payload
  const type = str(payload.approvalType)
  if (type === 'shell') return str(payload.operation).split(/\r?\n/, 1)[0] || undefined
  if (type === 'file') return baseName(str(payload.target)) || undefined
  return str(payload.targetLabel) || str(payload.target) || str(payload.operation) || undefined
}

const DECISION_NOTICE: Record<string, NoticeKind> = {
  accept: 'allowedOnce',
  acceptForSession: 'allowedForSession',
  acceptAlways: 'allowedAlways',
}

function answersOf(payload: Record<string, unknown>): string {
  const response = payload.response as { answers: Record<string, { answers: string[] }> }
  return Object.values(response.answers)
    .flatMap((entry) => entry.answers.map((answer) => answer.replace(/^user_note: /, '')))
    .join(', ')
}


function plainLine(line: string): string {
  return line
    .replace(/<!--.*?(?:-->|$)/g, '')
    .replace(/^#{1,6}\s+/, '')
    .replace(/(\*\*|__|\*|_|`)/g, '')
    .trim()
}

export function thinkingStatus(reasoning: string, complete = false): string | null {
  for (const raw of reasoning.split(/\r?\n/).reverse()) {
    const line = raw.replace(/<!--.*?-->/g, '').trim()
    const heading = /^#{1,6}\s+(.+)$/.exec(line)?.[1] ?? /^(\*\*|__)((?:(?!\1).)+)\1$/.exec(line)?.[2]
    const text = heading ? plainLine(heading) : ''
    if (text) return text
  }
  const paragraphs = reasoning.split(/\r?\n[\t ]*\r?\n/)
  for (let index = paragraphs.length - 1; index >= 0; index--) {
    const paragraph = paragraphs[index].trimStart()
    const newline = paragraph.search(/\r?\n/)
    if (newline === -1 && index === paragraphs.length - 1 && !complete) continue
    const lead = plainLine(newline === -1 ? paragraph : paragraph.slice(0, newline))
    if (lead) return lead
  }
  return null
}


function dependencies(item: HistoryItem, context: ItemContext): unknown[] {
  switch (item.type) {
    case 'userMessage':
      return [context.fallbackOrigin]
    case 'toolCall':
    case 'mcpToolCall':
    case 'dynamicToolCall':
      return [context.result, context.execution, context.turn ? !DONE.has(context.turn.status) : !isComplete(item)]
    case 'approvalResponse':
      return [context.approval]
    case 'error':
      return [context.turn?.providerError]
    default:
      return []
  }
}

function projectItem(item: HistoryItem, context: ItemContext): ItemProjection {
  const payload = item.payload
  const id = entryId(item)
  switch (item.type) {
    case 'userMessage': {
      const mode = str(payload.deliveryMode)
      const trigger = str(payload.triggerKind)
      if (trigger === 'terminal' || trigger === 'subagent' || (trigger === 'workflow' && !!str(payload.triggerStatus))) {
        if (mode === 'guidance') {
          const ending = trigger === 'subagent' ? subagentEndingEntry(item) : null
          return ending ? { entry: ending } : {}
        }
        return { wake: { task: trigger as TaskWake['task'], label: str(payload.triggerLabel).trim(), status: str(payload.triggerStatus) || null } }
      }
      if (mode === 'subagentMailbox') return {}
      const text = str(payload.text)
      const images = userImages(payload.nativeInputParts)
      if (!text && images.length === 0) return {}
      const origin = originOf(payload) ?? (context.fallbackOrigin && !trigger ? { kind: 'thread' as const, label: '', threadId: context.fallbackOrigin } : null)
      return { entry: { kind: 'user', id, text, segments: userSegments(text, payload.nativeInputParts), images, added: mode === 'guidance', origin, goal: payload.sentAsGoal === true } }
    }
    case 'agentMessage': {
      const text = str(payload.text)
      const phase = str(payload.phase)
      return text ? { entry: { kind: 'assistant', id, text, streaming: !isComplete(item), phase: phase === 'commentary' || phase === 'final' ? phase : null, at: item.createdAt, copy: false, changes: null } } : {}
    }
    case 'reasoningContent':
      return { heading: thinkingStatus(str(payload.text), isComplete(item)) }
    case 'toolCall':
    case 'mcpToolCall':
    case 'dynamicToolCall': {
      const plan = planEntry(item, context.result)
      if (plan) return { entry: plan }
      const action = threadActionOf(item, item.type === 'toolCall' ? context.result : item)
      if (action) return { action: { kind: 'threadAction', id, action } }
      const entry = toolEntry(item, context.result, context.execution, context.turn ? !DONE.has(context.turn.status) : !isComplete(item))
      return entry ? { entry } : {}
    }
    case 'imageGeneration': {
      const status = str(payload.status) || (isComplete(item) ? 'completed' : 'inProgress')
      const data = str(payload.result).trim()
      return { entry: { kind: 'image', id, status: status === 'failed' || status === 'completed' ? status : 'inProgress', uri: data ? imageUri(str(payload.mediaType).trim(), data) : null, dropped: payload.imageDropped === true, ...(str(payload.errorMessage) ? { error: str(payload.errorMessage) } : {}) } }
    }
    case 'approvalResponse': {
      const detail = approvalSubject(context.approval)
      return { entry: { kind: 'notice', id, notice: DECISION_NOTICE[str(payload.decision)] ?? 'rejected', ...(detail ? { detail } : {}) } }
    }
    case 'error': {
      const message = str(payload.message)
      return message ? { entry: failureEntry(id, message, context.turn?.providerError) } : {}
    }
    case 'systemNotice': {
      const entry = dividerOf(item)
      return entry ? { entry } : {}
    }
    case 'userInputResponse': {
      const detail = answersOf(payload)
      return { entry: { kind: 'notice', id, notice: 'answered', ...(detail ? { detail } : {}) } }
    }
    default:
      return {}
  }
}

export function createItemProjector() {
  let cache = new WeakMap<HistoryItem, { dependencies: unknown[]; projection: ItemProjection }>()
  return {
    project(item: HistoryItem, context: ItemContext): ItemProjection {
      const inputs = dependencies(item, context)
      const previous = cache.get(item)
      if (previous && inputs.length === previous.dependencies.length && inputs.every((input, index) => Object.is(input, previous.dependencies[index]))) return previous.projection
      const projection = projectItem(item, context)
      cache.set(item, { dependencies: inputs, projection })
      return projection
    },
    clear() {
      cache = new WeakMap()
    },
  }
}
