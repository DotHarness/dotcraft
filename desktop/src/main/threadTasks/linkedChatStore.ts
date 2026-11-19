import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs'
import { dirname } from 'node:path'

export interface LinkedChatRef {
  threadId: string
  projectId: string
  workspacePath: string
}

export interface LinkedChatChild extends LinkedChatRef {
  projectName: string
  title: string
}

export type LinkedChatOutcome = 'completed' | 'failed' | 'stopped'
export type LinkedChatStatus = 'idle' | 'running' | 'waitingForInput' | LinkedChatOutcome
export type LinkedChatWait = 'approval' | 'input' | 'planConfirmation'

export interface LinkedChat {
  parent: LinkedChatRef
  child: LinkedChatChild
  status: LinkedChatStatus
  createdAt: string
}

export interface ChatReport {
  notificationId: string
  turnId: string
  turnStartedAt?: string
  status: LinkedChatOutcome | 'waitingForInput'
  wait?: LinkedChatWait
  summary: string
  result?: string
}

export interface StoredLinkedChat extends LinkedChat {
  lastOutcome?: LinkedChatOutcome
  lastReportedTurnId?: string
  lastReportedTurnStartedAt?: string
  reportedWaits?: { turnId: string; kinds: LinkedChatWait[] }
  stoppedTurnId?: string
  pending: ChatReport[]
}

export function linkedChatRefKey(ref: LinkedChatRef): string {
  return JSON.stringify([ref.projectId, ref.threadId])
}

export function viewLinkedChat({ parent, child, status, createdAt }: StoredLinkedChat): LinkedChat {
  return { parent: { ...parent }, child: { ...child }, status, createdAt }
}

function linkKey({ parent, child }: Pick<LinkedChat, 'parent' | 'child'>): string {
  return JSON.stringify([parent.projectId, parent.threadId, child.projectId, child.threadId])
}

export class LinkedChatStore {
  private readonly links = new Map<string, StoredLinkedChat>()

  constructor(private readonly filePath: string | null) {
    if (!filePath) return
    try {
      const { links } = JSON.parse(readFileSync(filePath, 'utf8')) as { links: StoredLinkedChat[] }
      for (const link of links) {
        if (Array.isArray(link.pending)) this.links.set(linkKey(link), link)
      }
    } catch {
      this.links.clear()
    }
  }

  all(): StoredLinkedChat[] {
    return [...this.links.values()]
  }

  get(link: Pick<LinkedChat, 'parent' | 'child'>): StoredLinkedChat | undefined {
    return this.links.get(linkKey(link))
  }

  add(parent: LinkedChatRef, child: LinkedChatChild): StoredLinkedChat {
    const key = linkKey({ parent, child })
    const existing = this.links.get(key)
    if (existing) return existing
    const link: StoredLinkedChat = {
      parent: { ...parent },
      child: { ...child },
      status: 'idle',
      createdAt: new Date().toISOString(),
      pending: []
    }
    this.links.set(key, link)
    this.save()
    return link
  }

  remove(ref: LinkedChatRef): boolean {
    let changed = false
    const key = linkedChatRefKey(ref)
    for (const [id, link] of this.links) {
      if (linkedChatRefKey(link.parent) !== key && linkedChatRefKey(link.child) !== key) continue
      this.links.delete(id)
      changed = true
    }
    if (changed) this.save()
    return changed
  }

  save(): void {
    if (!this.filePath) return
    try {
      mkdirSync(dirname(this.filePath), { recursive: true })
      const temporary = `${this.filePath}.tmp`
      writeFileSync(temporary, JSON.stringify({ links: this.all() }), 'utf8')
      renameSync(temporary, this.filePath)
    } catch (error) {
      console.warn('[linkedChats] failed to save linked chats', error)
    }
  }
}
