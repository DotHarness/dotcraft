import { EventEmitter } from 'node:events'
import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { BrowserWindow, IpcMainInvokeEvent } from 'electron'
import type { ProjectActionPreferences } from '../../shared/projectActions'

const handlers = vi.hoisted(() => new Map<string, (...args: unknown[]) => unknown>())
vi.mock('electron', () => ({
  ipcMain: {
    handle: (channel: string, handler: (...args: unknown[]) => unknown) => handlers.set(channel, handler),
    removeHandler: (channel: string) => handlers.delete(channel)
  }
}))
import { registerProjectActionsHandlers } from '../projectActions/projectActionsIpc'
import { createWindowIpcMain } from '../windowIpc'

const dispose: (() => void)[] = []
let directory = ''
afterEach(() => {
  for (const release of dispose.splice(0)) release()
  if (directory) rmSync(directory, { recursive: true, force: true })
  directory = ''
})

function registration(id: number, initial: ProjectActionPreferences) {
  const sender = Object.assign(new EventEmitter(), { id, isDestroyed: () => false, send: vi.fn() })
  const owner = Object.assign(new EventEmitter(), {
    isDestroyed: () => false,
    webContents: sender
  }) as unknown as BrowserWindow
  const ipc = createWindowIpcMain(owner)
  const preferences: Record<string, ProjectActionPreferences> = { [resolve(directory)]: initial }
  const update = vi.fn(async (next: Record<string, ProjectActionPreferences>) => Object.assign(preferences, next))
  const cleanup = registerProjectActionsHandlers({
    owner,
    handleSafe: ipc.handle.bind(ipc),
    getPreferences: () => preferences,
    updatePreferences: update
  })
  dispose.push(cleanup, ipc.dispose)
  return { owner, sender, update, preferences, cleanup }
}
function invoke(owner: BrowserWindow, channel: string, ...args: unknown[]): unknown {
  return handlers.get(channel)!({ sender: owner.webContents } as IpcMainInvokeEvent, ...args)
}

describe('window project-action ownership', () => {
  it('retains each registration preferences and sends subscription events only to its owner', async () => {
    directory = mkdtempSync(join(tmpdir(), 'dotcraft-actions-ipc-'))
    const a = registration(101, { selectedEnvironmentId: 'old-a', worktreeEnvironmentId: 'a-setup' })
    const b = registration(102, { selectedEnvironmentId: 'old-b', worktreeEnvironmentId: 'b-setup' })
    invoke(a.owner, 'project-actions:subscribe', directory)
    invoke(b.owner, 'project-actions:subscribe', directory)
    await invoke(a.owner, 'project-actions:select', { workspacePath: directory, environmentId: 'new-a' })
    expect(a.update).toHaveBeenCalledWith({
      [resolve(directory)]: expect.objectContaining({
        selectedEnvironmentId: 'new-a',
        worktreeEnvironmentId: 'a-setup'
      })
    })
    expect(b.update).not.toHaveBeenCalled()
    expect(a.sender.send).toHaveBeenCalledWith('project-actions:changed')
    expect(b.sender.send).not.toHaveBeenCalled()
    a.cleanup()
    await invoke(b.owner, 'project-actions:select', { workspacePath: directory, environmentId: 'new-b' })
    expect(b.update).toHaveBeenCalledWith({
      [resolve(directory)]: expect.objectContaining({
        selectedEnvironmentId: 'new-b',
        worktreeEnvironmentId: 'b-setup'
      })
    })
    expect(b.sender.send).toHaveBeenCalledWith('project-actions:changed')
    expect(a.sender.listenerCount('destroyed')).toBe(0)
  })
})
