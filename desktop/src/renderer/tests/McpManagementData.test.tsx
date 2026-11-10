import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useMcpManagementData } from '../components/settings/panels/mcp/useMcpManagementData'
import { useConnectionStore } from '../stores/connectionStore'
import { useMcpStore } from '../stores/mcpStore'
import { applyMcpNotification } from '../stores/mcpNotifications'
import { installDesktopApiMock } from './desktopApiMock'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

function status(name: string) {
  return { name, enabled: true, startupState: 'ready' as const, transport: 'stdio' as const }
}

beforeEach(() => {
  useMcpStore.getState().reset()
  useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: 'A', connectionId: 'A1' })
})

describe('MCP management scope', () => {
  it('loads the new connection and discards old connection responses', async () => {
    const oldServers = deferred<{ servers: Array<{ name: string }> }>()
    const oldStatuses = deferred<{ data: Array<ReturnType<typeof status>> }>()
    const sendConnectionRequest = vi.fn((key: string, method: string) => {
      if (key === 'A') return method === 'mcp/list' ? oldServers.promise : oldStatuses.promise
      return Promise.resolve(method === 'mcp/list' ? { servers: [{ name: 'new' }] } : { data: [status('new')] })
    })
    installDesktopApiMock({ appServer: { sendConnectionRequest } })
    const { result } = renderHook(() => useMcpManagementData(true))
    await waitFor(() => expect(sendConnectionRequest).toHaveBeenCalledTimes(2))

    act(() => useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: 'B', connectionId: 'B1' }))
    await waitFor(() => expect(result.current.mcpServers.map(server => server.name)).toEqual(['new']))
    await act(async () => {
      oldServers.resolve({ servers: [{ name: 'old' }] })
      oldStatuses.resolve({ data: [status('old')] })
    })

    expect(result.current.mcpServers.map(server => server.name)).toEqual(['new'])
    expect(Object.keys(result.current.mcpStatuses)).toEqual(['new'])
    expect(sendConnectionRequest).toHaveBeenCalledWith('B', 'mcp/list', {}, undefined, { connectionId: 'B1' })
  })

  it('refreshes management data only for current unscoped terminal notifications', async () => {
    let currentName = 'initial'
    const sendConnectionRequest = vi.fn(async (_key: string, method: string) =>
      method === 'mcp/list' ? { servers: [] } : { data: [status(currentName)] })
    installDesktopApiMock({ appServer: { sendConnectionRequest } })
    const { result } = renderHook(() => useMcpManagementData(true))
    await waitFor(() => expect(Object.keys(result.current.mcpStatuses)).toEqual(['initial']))
    const initialRevision = useMcpStore.getState().catalogRevision

    act(() => {
      applyMcpNotification({
        method: 'mcpServer/startupStatus/updated', connectionKey: 'A', connectionId: 'A1',
        params: { threadId: 'thread-A', name: 'initial', status: 'failed' }
      }, 'en')
      applyMcpNotification({
        method: 'mcpServer/startupStatus/updated', connectionKey: 'A', connectionId: 'old',
        params: { name: 'initial', status: 'ready' }
      }, 'en')
    })
    expect(useMcpStore.getState().catalogRevision).toBe(initialRevision)
    expect(result.current.mcpStatuses.initial.startupState).toBe('ready')

    currentName = 'refreshed'
    act(() => {
      applyMcpNotification({
        method: 'mcpServer/startupStatus/updated', connectionKey: 'A', connectionId: 'A1',
        params: { name: 'initial', status: 'ready' }
      }, 'en')
    })
    await waitFor(() => expect(Object.keys(result.current.mcpStatuses)).toEqual(['refreshed']))
  })
})
