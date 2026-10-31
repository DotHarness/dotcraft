import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import type { LineCommentContext } from '../../shared/composerContext'
import type { ThreadConfigurationWire } from '../types/thread'
import { useModelCatalogStore, type ModelReasoningCapability } from '../stores/modelCatalogStore'

const PROVISIONAL_TITLE_MAX_CHARS = 60
const GENERATED_TITLE_MAX_CHARS = 36
const REQUEST_MAX_BYTES = 2000
const GENERATION_TIMEOUT_MS = 30_000
const HELPER_DISPLAY_NAME = '[internal] Thread title'
const EFFORT_ORDER = ['low', 'medium', 'high', 'extraHigh', 'max', 'ultra'] as const

type HelperConfiguration = NonNullable<ClientRequestMethods['thread/start']['params']['config']>
type HelperReasoning = NonNullable<HelperConfiguration['reasoning']>

export const TITLE_INSTRUCTIONS = [
  'You name conversations. The next user message is the request that starts a conversation; do not answer it or act on it, and do not use tools.',
  `Reply only with JSON: {"title": "..."}, where the title is a concise single-line task title of at most ${GENERATED_TITLE_MAX_CHARS} characters, under five words where possible.`,
  'Start with an imperative verb. Capitalize only the first word unless the user\'s language, proper nouns, acronyms, or code terms require otherwise.',
  'Preserve ticket references exactly. Write in the user\'s language. Do not use quotes, markdown, or trailing punctuation in the title.'
].join('\n')

export interface ThreadTitleRequest {
  provisionalTitle: string
  requestText: string
}

const WRAPPING_QUOTES = new Set(['"', '\'', '`', '“', '”', '‘', '’'])
const TRAILING_PUNCTUATION = new Set(['.', '?', '!', '。', '？', '！'])

function collapseWhitespace(text: string): string {
  return text.trim().replace(/\s+/gu, ' ')
}

function truncateChars(text: string, max: number): string {
  const chars = Array.from(text)
  return chars.length <= max ? text : chars.slice(0, max).join('').trimEnd()
}

function truncateUtf8(text: string, maxBytes: number): string {
  const encoder = new TextEncoder()
  let bytes = 0
  let result = ''
  for (const char of text) {
    bytes += encoder.encode(char).length
    if (bytes > maxBytes) break
    result += char
  }
  return result
}

function describeCommentsForTitle(comments: readonly LineCommentContext[]): string {
  return comments
    .map((comment) => `${comment.path}: ${collapseWhitespace(comment.comment)}`)
    .join('\n')
}

export function threadTitleRequest({
  bodyText,
  comments = [],
  visibleText = '',
  referencesTitle
}: {
  bodyText: string
  comments?: readonly LineCommentContext[]
  visibleText?: string
  referencesTitle?: string
}): ThreadTitleRequest | null {
  const words = [bodyText, describeCommentsForTitle(comments)].find((text) => text.trim().length > 0)
  const source = words ?? (visibleText.trim() ? visibleText : null)
  if (!source) return null
  const title = words || !referencesTitle?.trim() ? source : referencesTitle
  return {
    provisionalTitle: truncateChars(collapseWhitespace(title), PROVISIONAL_TITLE_MAX_CHARS),
    requestText: truncateUtf8(source.trim(), REQUEST_MAX_BYTES)
  }
}

function normalizeTitle(title: string): string | null {
  let chars = Array.from(collapseWhitespace(title))
  while (chars.length > 0 && WRAPPING_QUOTES.has(chars[0])) chars = chars.slice(1)
  while (chars.length > 0 && WRAPPING_QUOTES.has(chars[chars.length - 1])) chars = chars.slice(0, -1)
  while (chars.length > 0 && TRAILING_PUNCTUATION.has(chars[chars.length - 1])) chars = chars.slice(0, -1)
  const normalized = chars.join('').trimEnd()
  return normalized.length === 0 ? null : truncateChars(normalized, GENERATED_TITLE_MAX_CHARS)
}

export function parseGeneratedTitle(response: string | null): string | null {
  if (!response?.trim()) return null
  const trimmed = response.trim().replace(/^```(?:json)?\s*\n([\s\S]*?)\n?```$/u, '$1').trim()
  if (!trimmed.startsWith('{')) return trimmed.includes('\n') ? null : normalizeTitle(trimmed)
  try {
    const parsed: unknown = JSON.parse(trimmed)
    if (parsed == null || typeof parsed !== 'object' || Array.isArray(parsed)) return null
    const entries = Object.entries(parsed)
    if (entries.length !== 1 || entries[0][0] !== 'title' || typeof entries[0][1] !== 'string') return null
    return normalizeTitle(entries[0][1])
  } catch {
    return null
  }
}

function minimalReasoning(capability: ModelReasoningCapability | null | undefined): HelperReasoning | null {
  if (!capability) return null
  const output = capability.supportedOutputs.includes('none') ? 'none' : capability.defaultOutput
  if (capability.supportsDisable) return { enabled: false, effort: capability.defaultEffort, output }
  const effort = EFFORT_ORDER.find((candidate) => capability.supportedEfforts.some((option) => option.effort === candidate))
  return effort ? { enabled: true, effort, output } : null
}

function reasoningFor(providerId: string | undefined, model: string | undefined): HelperReasoning | null {
  if (!model) return null
  const catalog = useModelCatalogStore.getState()
  if (providerId && catalog.providerId && catalog.providerId !== providerId) return null
  return minimalReasoning(catalog.models.find((item) => item.id === model)?.reasoning)
}

export function buildTitleHelperConfiguration(source: ThreadConfigurationWire | null | undefined): HelperConfiguration {
  const providerId = (source?.providerId ?? source?.ProviderId) || undefined
  const model = (source?.model ?? source?.Model) || undefined
  const reasoning = reasoningFor(providerId, model)
  return {
    mode: 'agent',
    ...(providerId ? { providerId } : {}),
    ...(model ? { model } : {}),
    ...(reasoning ? { reasoning } : {}),
    approvalPolicy: 'deny',
    mcpServers: [],
    mcpPolicy: { servers: [] },
    toolPolicy: { allow: [] },
    skillsPolicy: { allow: [] },
    developerInstructions: TITLE_INSTRUCTIONS
  }
}

interface SourceThread {
  displayName?: string | null
  workspacePath?: string
  userId?: string | null
  channelContext?: string | null
  configuration?: ThreadConfigurationWire | null
}

interface TerminalTurn {
  id?: string
  threadId?: string
  status?: string
  items?: Array<{ type?: string; payload?: { text?: string } }>
}

async function readSourceThread(threadId: string): Promise<SourceThread | null> {
  const result = await window.api.appServer.sendRequest('thread/read', { threadId }) as { thread?: SourceThread }
  return result.thread ?? null
}

async function runHelperTurn(source: SourceThread, requestText: string): Promise<string | null> {
  let helperId: string | null = null
  let helperTurnId: string | null = null
  let terminal = false
  let resolveTerminal!: (turn: TerminalTurn | null) => void
  const terminalTurn = new Promise<TerminalTurn | null>((resolve) => { resolveTerminal = resolve })
  const timeout = window.setTimeout(() => resolveTerminal(null), GENERATION_TIMEOUT_MS)
  const unsubscribe = window.api.appServer.onNotification((notification) => {
    if (helperId == null || !['turn/completed', 'turn/failed', 'turn/cancelled'].includes(notification.method)) return
    const turn = (notification.params as { turn?: TerminalTurn }).turn
    if (turn?.threadId !== helperId) return
    terminal = true
    resolveTerminal(notification.method === 'turn/completed' ? turn : null)
  })

  try {
    const workspacePath = source.workspacePath ?? ''
    const started = await window.api.appServer.sendRequest('thread/start', {
      identity: {
        channelName: 'dotcraft-desktop',
        userId: source.userId || 'local',
        channelContext: source.channelContext || `workspace:${workspacePath}`,
        workspacePath
      },
      historyMode: 'server',
      ephemeral: true,
      displayName: HELPER_DISPLAY_NAME,
      config: buildTitleHelperConfiguration(source.configuration)
    }, GENERATION_TIMEOUT_MS) as { thread?: { id?: string } }
    helperId = started.thread?.id ?? null
    if (helperId == null) return null

    const turn = await window.api.appServer.sendRequest('turn/start', {
      threadId: helperId,
      input: [{ type: 'text', text: requestText }]
    }, GENERATION_TIMEOUT_MS) as { turn?: { id?: string } }
    helperTurnId = turn.turn?.id ?? null

    const result = await terminalTurn
    if (result?.status !== 'completed') return null
    return result.items?.filter((item) => item.type === 'agentMessage').at(-1)?.payload?.text ?? null
  } finally {
    unsubscribe()
    window.clearTimeout(timeout)
    if (helperId != null) {
      if (!terminal && helperTurnId != null) {
        await window.api.appServer.sendRequest('turn/interrupt', { threadId: helperId, turnId: helperTurnId }).catch(() => {})
      }
      await window.api.appServer.sendRequest('thread/delete', { threadId: helperId }).catch(() => {})
    }
  }
}

export async function generateThreadTitle(threadId: string, request: ThreadTitleRequest): Promise<string | null> {
  try {
    const source = await readSourceThread(threadId)
    if (!source || source.displayName !== request.provisionalTitle) return null
    const title = parseGeneratedTitle(await runHelperTurn(source, request.requestText))
    if (!title || title === request.provisionalTitle) return null
    const current = await readSourceThread(threadId)
    if (current?.displayName !== request.provisionalTitle) return null
    await window.api.appServer.sendRequest('thread/rename', { threadId, displayName: title })
    return title
  } catch (error) {
    console.debug('[thread-title] generation failed', { threadId, error })
    return null
  }
}
