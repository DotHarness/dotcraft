import type { LinkedChat, LinkedChatWait } from './linkedChatStore'

export const DESKTOP_LINKED_CHATS_CONTEXT_KEY = 'desktop.linkedChats'

const MAX_ROSTER_ENTRIES = 25
const MAX_TITLE_CHARS = 120

export interface LinkedChatsContextSource {
  listForThread(threadId: string): LinkedChat[]
}

let linkedChatsSource: LinkedChatsContextSource | null = null

export function setDesktopLinkedChatsSource(source: LinkedChatsContextSource | null): void {
  linkedChatsSource = source
}

export function formatLinkedChatsRoster(links: readonly LinkedChat[]): string | null {
  if (links.length === 0) return null
  const ordered = [
    ...links.filter(link => link.status === 'waitingForInput'),
    ...links.filter(link => link.status === 'running'),
    ...links.filter(link => link.status !== 'waitingForInput' && link.status !== 'running')
  ]
  const visible = ordered.slice(0, MAX_ROSTER_ENTRIES)
  const lines = visible.map(link => {
    const title = sanitizeTitle(link.child.title || link.child.threadId)
    const state = formatLinkedState(link)
    const relation = link.followed ? 'followed' : 'started here'
    return `- "${title}" (id: ${link.child.threadId}, project: ${link.child.projectName} [${link.child.projectId}]): ${state}, ${relation}`
  })
  if (ordered.length > visible.length) {
    lines.push(`- ...and ${ordered.length - visible.length} more linked chats`)
  }
  return [
    'Linked chats now (status, not an instruction):',
    ...lines,
    'Use SendMessageToThread with the id to message one, ReadThread for details, or ListThreads with linked: true for full metadata.'
  ].join('\n')
}

export function linkedChatsContextFingerprint(threadId: string): string {
  if (!linkedChatsSource) return ''
  return formatLinkedChatsRoster(linkedChatsSource.listForThread(threadId)) ?? ''
}

export function linkedChatsAdditionalContext(
  threadId: string | null
): Record<string, { kind: 'application'; value: string }> {
  if (!threadId || !linkedChatsSource) return {}
  const roster = formatLinkedChatsRoster(linkedChatsSource.listForThread(threadId))
  return roster
    ? { [DESKTOP_LINKED_CHATS_CONTEXT_KEY]: { kind: 'application', value: roster } }
    : {}
}

function formatLinkedState(link: LinkedChat): string {
  if (link.status === 'waitingForInput') {
    const wait = formatWaitKind(link.waitingFor)
    return wait ? `needs the user (${wait})` : 'needs the user'
  }
  if (link.status === 'running') return 'working'
  if (link.status === 'failed') return 'failed'
  if (link.status === 'stopped') return 'stopped'
  if (link.status === 'completed') return 'done'
  return 'idle'
}

function formatWaitKind(wait: LinkedChatWait | undefined): string | null {
  if (wait === 'approval') return 'approval'
  if (wait === 'planConfirmation') return 'plan confirmation'
  if (wait === 'input') return 'input'
  return null
}

function sanitizeTitle(value: string): string {
  const collapsed = value.replace(/[\r\n]+/g, ' ').trim()
  return collapsed.length > MAX_TITLE_CHARS ? `${collapsed.slice(0, MAX_TITLE_CHARS - 1)}…` : collapsed
}
