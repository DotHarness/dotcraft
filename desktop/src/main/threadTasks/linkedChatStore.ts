import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs'
import { dirname } from 'node:path'
import type { LinkedChatOutcome, LinkedChatStatus, LinkedChatWait } from '../../shared/linkedChats'

export type { LinkedChatOutcome, LinkedChatStatus, LinkedChatWait }

export interface LinkedChatRef {
  threadId: string
  projectId: string
  workspacePath: string
}

export interface LinkedChatChild extends LinkedChatRef {
  projectName: string
  title: string
}

export interface LinkedChat {
  parent: LinkedChatRef
  child: LinkedChatChild
  status: LinkedChatStatus
  followed?: true
  waitingFor?: LinkedChatWait
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

export function viewLinkedChat({ parent, child, status, followed, waitingFor, createdAt }: StoredLinkedChat): LinkedChat {
  return { parent: { ...parent }, child: { ...child }, status, ...(followed ? { followed } : {}), ...(waitingFor ? { waitingFor } : {}), createdAt }
}

function linkKey({ parent, child }: Pick<LinkedChat, 'parent' | 'child'>): string {
  return JSON.stringify([parent.projectId, parent.threadId, child.projectId, child.threadId])
}

export class LinkedChatStore {
  private readonly links = new Map<string, StoredLinkedChat>()

  constructor(private readonly filePath: string | null, private readonly onSaved?: () => void) {
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

  add(
    parent: LinkedChatRef,
    child: LinkedChatChild,
    initial?: Partial<Pick<StoredLinkedChat, 'followed' | 'status' | 'lastOutcome' | 'lastReportedTurnId' | 'lastReportedTurnStartedAt'>>
  ): StoredLinkedChat {
    const key = linkKey({ parent, child })
    const existing = this.links.get(key)
    if (existing) return existing
    const link: StoredLinkedChat = {
      parent: { ...parent },
      child: { ...child },
      status: 'idle',
      createdAt: new Date().toISOString(),
      pending: [],
      ...initial
    }
    this.links.set(key, link)
    this.save()
    return link
  }

  removeFollow(link: LinkedChat): boolean {
    const key = linkKey(link)
    if (!this.links.get(key)?.followed) return false
    this.links.delete(key)
    this.save()
    return true
  }

  wouldCycle(parent: LinkedChatRef, child: LinkedChatRef): boolean {
    const parentKey = linkedChatRefKey(parent)
    const targets = new Map<string, string[]>()
    for (const link of this.links.values()) {
      const key = linkedChatRefKey(link.parent)
      const next = targets.get(key) ?? []
      next.push(linkedChatRefKey(link.child))
      targets.set(key, next)
    }
    const pending = [linkedChatRefKey(child)]
    const visited = new Set<string>()
    for (const key of pending) {
      if (key === parentKey) return true
      if (visited.has(key)) continue
      visited.add(key)
      pending.push(...(targets.get(key) ?? []))
    }
    return false
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

  removeForParent(parentThreadId: string): boolean {
    let changed = false
    for (const [id, link] of this.links) {
      if (link.parent.threadId !== parentThreadId) continue
      this.links.delete(id)
      changed = true
    }
    if (changed) this.save()
    return changed
  }

  save(): void {
    this.onSaved?.()
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
