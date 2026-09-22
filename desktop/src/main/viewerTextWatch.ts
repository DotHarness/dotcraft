import { promises as fs, watch, type FSWatcher } from 'fs'
import * as path from 'path'
import type { WebContents } from 'electron'
import type { TextFileChangedPayload } from '../shared/viewer/types'

interface WatchEntry {
  senderId: number
  sender: WebContents
  onDestroyed: () => void
  watcher: FSWatcher
  timer: NodeJS.Timeout | null
}

class ViewerTextWatchManager {
  private nextId = 0
  private readonly entries = new Map<string, WatchEntry>()

  subscribe(sender: WebContents, absolutePath: string): string {
    const subscriptionId = `viewer-text-${sender.id}-${++this.nextId}`
    const fileName = path.basename(absolutePath)
    const entry: WatchEntry = {
      senderId: sender.id,
      sender,
      onDestroyed: () => this.unsubscribe(subscriptionId, sender.id),
      timer: null,
      watcher: watch(path.dirname(absolutePath), { persistent: false }, (_eventType, changedName) => {
        if (changedName && !sameFileName(changedName.toString(), fileName)) return
        if (entry.timer) clearTimeout(entry.timer)
        entry.timer = setTimeout(() => {
          entry.timer = null
          void this.emitChange(sender, subscriptionId, absolutePath)
        }, 80)
      })
    }
    this.entries.set(subscriptionId, entry)
    entry.watcher.on('error', entry.onDestroyed)
    sender.once('destroyed', entry.onDestroyed)
    return subscriptionId
  }

  unsubscribe(subscriptionId: string, senderId: number): void {
    const entry = this.entries.get(subscriptionId)
    if (!entry || entry.senderId !== senderId) return
    if (entry.timer) clearTimeout(entry.timer)
    entry.sender.removeListener('destroyed', entry.onDestroyed)
    entry.watcher.close()
    this.entries.delete(subscriptionId)
  }

  disposeAll(): void {
    for (const [id, entry] of this.entries) this.unsubscribe(id, entry.senderId)
  }

  private async emitChange(sender: WebContents, subscriptionId: string, absolutePath: string): Promise<void> {
    if (sender.isDestroyed()) return
    try {
      const stat = await fs.stat(absolutePath)
      if (!stat.isFile() || sender.isDestroyed() || !this.entries.has(subscriptionId)) return
      const payload: TextFileChangedPayload & { subscriptionId: string } = {
        subscriptionId,
        absolutePath,
        mtimeMs: stat.mtimeMs,
        sizeBytes: stat.size
      }
      sender.send('workspace:viewer:text-changed', payload)
    } catch {
      // A missing or inaccessible file is handled by the next explicit read/save.
    }
  }
}

function sameFileName(left: string, right: string): boolean {
  return process.platform === 'win32'
    ? left.toLocaleLowerCase() === right.toLocaleLowerCase()
    : left === right
}

export const viewerTextWatchManager = new ViewerTextWatchManager()
