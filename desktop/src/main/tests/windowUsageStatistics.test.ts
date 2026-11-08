import { EventEmitter } from 'node:events'
import { expect, it, vi } from 'vitest'
import { resolveLocalUsageConnection } from '../windows/windowUsageStatistics'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'

vi.mock('../defaultChatWorkspace', () => ({ resolveDefaultChatWorkspacePath: () => '/home/sample/.craft/workspaces/chats' }))

it('waits for the existing local handshake and cleans its listeners without creating another client', async () => {
  const client = new EventEmitter()
  const entry = { kind: 'chat', key: 'local-chats', connected: false, client } as WorkspaceConnectionEntry
  const pending = resolveLocalUsageConnection({
    ensureDefaultChatConnection: async () => undefined,
    getWorkspaceConnection: () => entry
  })
  await vi.waitFor(() => expect(client.listenerCount('ready')).toBe(1))
  client.emit('ready', { capabilities: { usageTelemetry: false } })
  await expect(pending).resolves.toEqual({ connectionKey: 'local-chats', usageTelemetry: false })
  expect(client.eventNames()).toEqual([])
})
