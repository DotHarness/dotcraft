import { translate, type AppLocale } from '../../shared/locales'

/** Board errors arrive as a successful call whose result is `{ error: { code, message } }`. */
export const BOARD_OPERATIONS = [
  'createChannel',
  'getChannels',
  'listThreads',
  'searchPosts',
  'readThread',
  'readPost',
  'subscribe',
  'unsubscribe',
  'post'
] as const

export type BoardOperation = (typeof BOARD_OPERATIONS)[number]

export function isBoardOperation(value: unknown): value is BoardOperation {
  return (BOARD_OPERATIONS as readonly unknown[]).includes(value)
}

const READ_OPERATIONS = new Set<unknown>(['getChannels', 'listThreads', 'searchPosts', 'readThread', 'readPost'])

export function isBoardReadOperation(operation: unknown): boolean {
  return READ_OPERATIONS.has(operation)
}

const BOARD_ROOT_AGENT_PATH = '/root'

export interface BoardPost {
  messageId: string
  authorPath: string
  channel: string | null
  threadId: string | null
  createdAt: string | null
  text: string
  totalChars: number
  /** Characters the board holds beyond `text`. */
  hiddenChars: number
  topic: boolean
}

export interface BoardTopic {
  threadId: string
  root: BoardPost
  replyCount: number
  latest: BoardPost | null
}

export interface BoardChannel {
  name: string
  description: string | null
  messageCount: number
}

export type BoardPhase = 'running' | 'completed' | 'failed'

export interface BoardCall {
  operation: BoardOperation
  phase: BoardPhase
  channel: string | null
  query: string | null
  postKind: 'topic' | 'reply' | 'newChannel' | null
  text: string | null
  /** Agent paths as the model wrote them; relative paths resolve from the caller. */
  recipients: string[]
  targetPath: string | null
  post: BoardPost | null
  /** A topic's posts oldest first, or search hits newest first. */
  posts: BoardPost[]
  topics: BoardTopic[]
  channels: BoardChannel[]
  hasMore: boolean
  errorMessage: string | null
}

export interface BoardCallInput {
  operation: BoardOperation
  args: Record<string, unknown> | undefined
  result: string | undefined
  running: boolean
  /** An invocation-level failure (declined, cancelled, thrown), as opposed to a board error result. */
  invocationError?: string | null
}

export function parseBoardCall({ operation, args = {}, result, running, invocationError }: BoardCallInput): BoardCall {
  const parsed = running ? null : parseObject(result)
  const error = asObject(parsed?.error)
  const errorMessage = invocationError != null
    ? invocationError
    : error ? (asString(error.message) ?? asString(error.code) ?? '') : null
  const settled = !running && errorMessage == null
  const rootPost = asObject(parsed?.rootPost)
  const posts = !settled
    ? []
    : operation === 'readThread'
      ? [rootPost, ...asObjects(parsed?.results).reverse()].flatMap((entry) => (entry ? [toPost(entry)] : []))
      : operation === 'searchPosts'
        ? asObjects(parsed?.results).map(toPost)
        : []
  const post = settled && parsed && operation === 'readPost'
    ? toPost(parsed)
    : settled && parsed && operation === 'post'
      ? toPost({ ...parsed, text: asString(args.text) ?? '' })
      : null
  const targetPath = asString(parsed?.targetAgent) ?? asString(args.targetAgent)

  return {
    operation,
    phase: running ? 'running' : errorMessage != null ? 'failed' : 'completed',
    channel: asString(args.channelName) ?? asString(args.newChannelName) ?? asString(parsed?.channelName)
      ?? asString(rootPost?.channelName) ?? null,
    query: asString(args.query),
    postKind: operation !== 'post' ? null : args.newChannelName ? 'newChannel' : args.threadId ? 'reply' : 'topic',
    text: asString(args.text),
    recipients: Array.isArray(args.agentsToNotify)
      ? args.agentsToNotify.filter((entry): entry is string => typeof entry === 'string' && entry.trim().length > 0)
      : [],
    targetPath,
    post,
    posts,
    topics: settled && operation === 'listThreads' ? asObjects(parsed?.results).map(toTopic) : [],
    channels: settled && operation === 'getChannels' ? asObjects(parsed?.results).map(toChannel) : [],
    hasMore: parsed?.hasMore === true,
    errorMessage
  }
}

/** A sentence as a catalog key, so the card can render `name` as an agent link. */
export interface BoardLabel {
  key: string
  vars: Record<string, string | number>
  agentPath?: string
}

export function describeBoardCall(call: BoardCall): BoardLabel {
  const channel = call.channel ?? ''
  const label = (key: string, vars: Record<string, string | number> = {}, agentPath?: string): BoardLabel => ({
    key: `toolCall.board.${key}`,
    vars: channel ? { channel, ...vars } : vars,
    ...(agentPath ? { agentPath } : {})
  })
  const counted = (key: string, count: number, vars: Record<string, string | number> = {}): BoardLabel =>
    label(`${key}.${count === 1 ? 'one' : 'other'}`, { count, ...vars })
  const running = call.phase === 'running'

  if (call.phase === 'failed') return label(`failed.${call.operation}`)
  switch (call.operation) {
    case 'post':
      if (call.postKind === 'newChannel') return label(running ? 'creatingChannel' : 'openedChannel')
      if (call.postKind === 'reply') {
        if (running) return label('replyingGeneric')
        return label(channel ? 'replied' : 'repliedGeneric')
      }
      if (running) return label(channel ? 'posting' : 'postingGeneric')
      return label(channel ? 'startedTopic' : 'startedTopicGeneric')
    case 'createChannel':
      return label(running ? 'creatingChannel' : 'createdChannel')
    case 'getChannels':
      if (running) return call.query ? label('searchingChannels', { query: call.query }) : label('listingChannels')
      if (call.query) {
        return call.channels.length
          ? counted('foundChannels', call.channels.length, { query: call.query })
          : label('noChannels', { query: call.query })
      }
      return call.channels.length ? counted('listedChannels', call.channels.length) : label('noChannelsYet')
    case 'listThreads':
      if (running) return label(channel ? 'listingTopics' : 'listingTopicsGeneric')
      return counted('listedTopics', call.topics.length)
    case 'searchPosts':
      if (running) return call.query ? label('searchingPosts', { query: call.query }) : label('checkingRecent')
      if (!call.query) return counted('checkedRecent', call.posts.length)
      return call.posts.length
        ? counted('foundPosts', call.posts.length, { query: call.query })
        : label('noPosts', { query: call.query })
    case 'readThread': {
      const root = call.posts[0]
      if (running || !root) return label(running ? 'readingTopic' : 'readTopicGeneric')
      return label('readTopic', { title: firstLine(root.text, 48) })
    }
    case 'readPost':
      if (running || !call.post) return label(running ? 'readingPost' : 'readPostGeneric')
      return label('readPost', {}, call.post.authorPath)
    case 'subscribe':
    case 'unsubscribe': {
      const verb = `${call.operation === 'subscribe' ? 'subscrib' : 'unsubscrib'}${running ? 'ing' : 'ed'}`
      const subject = channel ? 'Channel' : 'Topic'
      const other = !running && call.targetPath && !isRootAgentPath(call.targetPath) ? call.targetPath : undefined
      return label(`${verb}${other ? 'Other' : ''}${subject}`, {}, other)
    }
  }
}

export function describeBoardAccessory(call: BoardCall): BoardLabel | null {
  if (call.phase !== 'completed') return null
  if (call.operation === 'readThread' && call.posts.length > 0) {
    const replies = call.posts.length - 1
    return replies
      ? { key: `toolCall.board.replies.${replies === 1 ? 'one' : 'other'}`, vars: { count: replies } }
      : { key: 'toolCall.board.noReplies', vars: {} }
  }
  if (call.operation === 'readPost' && call.post && call.post.hiddenChars > 0) {
    return {
      key: 'toolCall.board.partial',
      vars: { shown: call.post.totalChars - call.post.hiddenChars, total: call.post.totalChars }
    }
  }
  return null
}

export function isRootAgentPath(path: string): boolean {
  return path.split('/').filter(Boolean).length <= 1 && path.startsWith('/')
}

export function boardAgentName(path: string, locale: AppLocale): string {
  if (isRootAgentPath(path)) return translate(locale, 'toolCall.board.mainAgent')
  const segments = path.split('/').filter(Boolean)
  return segments[segments.length - 1] ?? path
}

export function formatBoardLabel(label: BoardLabel, locale: AppLocale, call?: BoardCall): string {
  const vars = label.agentPath ? { ...label.vars, name: boardAgentName(label.agentPath, locale) } : label.vars
  const text = translate(locale, label.key, vars)
  return call?.phase === 'failed' && call.errorMessage ? `${text} — ${call.errorMessage}` : text
}

export function formatBoardToolLabel(
  operation: BoardOperation,
  args: Record<string, unknown> | undefined,
  locale: AppLocale,
  running: boolean
): string {
  const call = parseBoardCall({ operation, args, result: undefined, running: true })
  // Without a result a read has no count to report, so it keeps its in-progress wording.
  const phase = running || isBoardReadOperation(operation) ? 'running' : 'completed'
  return formatBoardLabel(describeBoardCall({ ...call, phase }), locale)
}

export function firstLine(text: string, max: number): string {
  const line = text.split(/\r?\n/).find((entry) => entry.trim()) ?? ''
  const chars = Array.from(line.trim())
  return chars.length > max ? `${chars.slice(0, max - 1).join('')}…` : chars.join('')
}

function toPost(value: Record<string, unknown>): BoardPost {
  const text = asString(value.text) ?? asString(value.textPreview) ?? ''
  const shownChars = Array.from(text).length
  const totalChars = typeof value.nChars === 'number' ? value.nChars : shownChars
  const messageId = asString(value.messageId) ?? ''
  const threadId = asString(value.threadId)
  return {
    messageId,
    authorPath: asString(value.author) ?? BOARD_ROOT_AGENT_PATH,
    channel: asString(value.channelName),
    threadId,
    createdAt: asString(value.createdAt),
    text,
    totalChars,
    hiddenChars: Math.max(0, totalChars - shownChars),
    topic: messageId.length > 0 && messageId === threadId
  }
}

function toTopic(value: Record<string, unknown>): BoardTopic {
  const latest = asObject(value.latestReply)
  return {
    threadId: asString(value.threadId) ?? '',
    root: toPost(asObject(value.rootPost) ?? {}),
    replyCount: typeof value.replyCount === 'number' ? value.replyCount : 0,
    latest: latest ? toPost(latest) : null
  }
}

function toChannel(value: Record<string, unknown>): BoardChannel {
  return {
    name: asString(value.channelName) ?? '',
    description: asString(value.description),
    messageCount: typeof value.messageCount === 'number' ? value.messageCount : 0
  }
}

function parseObject(value: string | undefined): Record<string, unknown> | null {
  if (!value) return null
  try {
    return asObject(JSON.parse(value))
  } catch {
    return null
  }
}

function asObject(value: unknown): Record<string, unknown> | null {
  return value != null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : null
}

function asObjects(value: unknown): Array<Record<string, unknown>> {
  return Array.isArray(value)
    ? value.flatMap((entry) => {
      const object = asObject(entry)
      return object ? [object] : []
    })
    : []
}

function asString(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null
}
