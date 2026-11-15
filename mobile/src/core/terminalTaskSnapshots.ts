import type { BackgroundTerminalSnapshot } from '@dotcraft/sdk/contracts'

export class TerminalTaskSnapshots {
  private revision = 0
  private readonly sessions = new Map<string, { revision: number; terminal: BackgroundTerminalSnapshot }>()
  private readonly ended = new Set<string>()

  get version(): number {
    return this.revision
  }

  observe(terminal: BackgroundTerminalSnapshot): BackgroundTerminalSnapshot | null {
    const id = terminal.sessionId
    if (!id || !terminal.threadId) return null
    const previous = this.sessions.get(id)?.terminal
    if (this.ended.has(id) && terminal.status === 'running') return null
    if (terminal.status !== 'running') this.ended.add(id)
    const current = { ...terminal, isBackground: terminal.isBackground || previous?.isBackground === true }
    this.sessions.set(id, { revision: ++this.revision, terminal: current })
    return current
  }

  ofThread(threadId: string): BackgroundTerminalSnapshot[] {
    return [...this.sessions.values()].flatMap(({ terminal }) => terminal.threadId === threadId ? [terminal] : [])
  }

  reconcile(threadId: string, listed: BackgroundTerminalSnapshot[], version: number): BackgroundTerminalSnapshot[] {
    const snapshots = new Map(listed.map((terminal) => [terminal.sessionId, terminal]))
    const result = new Map([...snapshots].filter(([id, terminal]) => terminal.status !== 'running' || !this.ended.has(id ?? '')))
    for (const [id, { revision, terminal }] of this.sessions) {
      if (terminal.threadId !== threadId) continue
      const snapshot = snapshots.get(id)
      if (!snapshot && revision <= version) {
        this.sessions.delete(id)
        this.ended.add(id)
        continue
      }
      if (revision > version || (snapshot?.status === 'running' && terminal.status !== 'running')) result.set(terminal.sessionId, terminal)
      else if (snapshot && terminal.isBackground) result.set(terminal.sessionId, { ...snapshot, isBackground: true })
    }
    for (const terminal of result.values()) {
      if (!terminal.sessionId) continue
      if (terminal.status !== 'running') this.ended.add(terminal.sessionId)
      this.sessions.set(terminal.sessionId, { revision: this.revision, terminal })
    }
    return [...result.values()]
  }
}
