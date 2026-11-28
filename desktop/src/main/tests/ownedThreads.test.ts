import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { JsonRpcError } from '@dotcraft/sdk/wire'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { OwnedThreadRequest } from '../../shared/ownedThreads'
import { DesktopOwnedThreads, type OwnedThreadConnection } from '../threadTasks/ownedThreads'

const directories: string[] = []
afterEach(() => directories.splice(0).forEach(directory => rmSync(directory, { force: true, recursive: true })))

function statePath(): string {
  const directory = mkdtempSync(join(tmpdir(), 'owned-threads-'))
  directories.push(directory)
  return join(directory, 'owned.json')
}

const REQUEST: OwnedThreadRequest = {
  pluginId: 'oratorio',
  key: 'conductor',
  space: 'oratorio',
  title: 'Oratorio',
  config: { developerInstructions: 'Coordinate.', approvalPolicy: 'autoApprove' }
}

function appServer() {
  const threads = new Map<string, { status: string; configuration: Record<string, unknown> }>()
  let next = 0
  const request = vi.fn(async (method: string, params?: unknown): Promise<unknown> => {
    const value = params as Record<string, any>
    switch (method) {
      case 'thread/start': {
        const id = `thread-${++next}`
        threads.set(id, { status: 'active', configuration: { ...value.config, model: 'default' } })
        return { thread: { id } }
      }
      case 'thread/read': {
        const thread = threads.get(value.threadId)
        if (!thread) throw new JsonRpcError(-32010, 'Thread not found', { code: 'ThreadNotFound' })
        return { thread: { id: value.threadId, ...thread } }
      }
      case 'thread/config/update':
        threads.get(value.threadId)!.configuration = value.config
        return {}
      default:
        throw new Error(`Unexpected request: ${method}`)
    }
  })
  const connection: OwnedThreadConnection = {
    workspacePath: '/chats',
    request: request as OwnedThreadConnection['request'],
    createChatFolder: async () => '/chats/folders/oratorio'
  }
  return { connection, request, threads }
}

describe('DesktopOwnedThreads', () => {
  it('creates the thread once in its own chat folder and returns it again after a restart', async () => {
    const path = statePath()
    const server = appServer()
    const first = await new DesktopOwnedThreads(path).ensure(REQUEST, server.connection)

    const start = server.request.mock.calls.find(([method]) => method === 'thread/start')?.[1] as Record<string, any>
    expect(start.cwd).toBe('/chats/folders/oratorio')
    expect(start.config).toMatchObject({ approvalPolicy: 'autoApprove' })
    expect(start.config.developerInstructions).toContain('Coordinate.')

    const reopened = new DesktopOwnedThreads(path)
    expect(reopened.list()).toEqual([expect.objectContaining({ pluginId: 'oratorio', key: 'conductor', space: 'oratorio', threadId: first.threadId })])
    await expect(reopened.ensure(REQUEST, server.connection)).resolves.toMatchObject({ threadId: first.threadId })
    expect(server.request.mock.calls.filter(([method]) => method === 'thread/start')).toHaveLength(1)
  })

  it('joins concurrent calls for the same key into one thread', async () => {
    const server = appServer()
    const owned = new DesktopOwnedThreads(null)
    const [left, right] = await Promise.all([owned.ensure(REQUEST, server.connection), owned.ensure(REQUEST, server.connection)])
    expect(left.threadId).toBe(right.threadId)
    expect(server.threads.size).toBe(1)
  })

  it('creates a new thread when the recorded one was deleted', async () => {
    const server = appServer()
    const owned = new DesktopOwnedThreads(null)
    const first = await owned.ensure(REQUEST, server.connection)
    server.threads.delete(first.threadId)
    const second = await owned.ensure(REQUEST, server.connection)
    expect(second.threadId).not.toBe(first.threadId)
    expect(owned.list()).toEqual([expect.objectContaining({ threadId: second.threadId })])
  })

  it('updates the existing thread when the plugin changes its configuration, keeping other settings', async () => {
    const server = appServer()
    const owned = new DesktopOwnedThreads(null)
    const first = await owned.ensure(REQUEST, server.connection)
    await owned.ensure({ ...REQUEST, config: { ...REQUEST.config, developerInstructions: 'Coordinate better.' } }, server.connection)

    const configuration = server.threads.get(first.threadId)!.configuration
    expect(configuration.model).toBe('default')
    expect(configuration.developerInstructions).toContain('Coordinate better.')
    expect(server.threads.size).toBe(1)
  })

  it('persists paused state and removes owned records by thread id', async () => {
    const path = statePath()
    const server = appServer()
    const owned = new DesktopOwnedThreads(path)
    const created = await owned.ensure(REQUEST, server.connection)
    expect(owned.isPaused(created.threadId)).toBe(false)

    expect(owned.setPaused(created.threadId, true)).toMatchObject({ threadId: created.threadId, paused: true })
    expect(owned.isPaused(created.threadId)).toBe(true)

    const reopened = new DesktopOwnedThreads(path)
    expect(reopened.isPaused(created.threadId)).toBe(true)
    expect(reopened.setPaused(created.threadId, false)?.paused).toBeUndefined()
    expect(reopened.isPaused(created.threadId)).toBe(false)

    expect(reopened.removeByThreadId(created.threadId)).toMatchObject({ threadId: created.threadId })
    expect(new DesktopOwnedThreads(path).list()).toEqual([])
  })
})
