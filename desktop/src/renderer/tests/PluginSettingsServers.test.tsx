import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { usePluginSettingsServers } from '../components/plugins/settings/useMcpServerSettings'
import { useMcpStore } from '../stores/mcpStore'
import { installDesktopApiMock } from './desktopApiMock'

const settings = { extensions: { 'dotcraft/settings': { readTool: 'settings_read', updateTool: 'settings_update' } } }

function pluginServer(name: string, ready: boolean) {
  return {
    name,
    startupState: ready ? 'ready' : 'starting',
    origin: { kind: 'plugin', pluginId: 'acme' },
    serverCapabilities: ready ? settings : null,
    tools: ready ? { settings_read: { name: 'settings_read' }, settings_update: { name: 'settings_update' } } : {}
  }
}

function otherServer(index: number) {
  return { name: `server-${index}`, startupState: 'ready', origin: { kind: 'workspace' }, tools: {} }
}

beforeEach(() => {
  useMcpStore.getState().reset()
})

describe('usePluginSettingsServers', () => {
  it('finds a plugin server listed on a later status page', async () => {
    const sendRequestRaw = vi.fn(async (_method: string, params?: { cursor?: string }) =>
      params?.cursor === '100'
        ? { data: [pluginServer('acme', true)], nextCursor: null }
        : { data: Array.from({ length: 100 }, (_, index) => otherServer(index)), nextCursor: '100' })
    installDesktopApiMock({ appServer: { sendRequestRaw } })

    const { result } = renderHook(() => usePluginSettingsServers('acme', true))

    await waitFor(() => expect(result.current.map((server) => server.status.name)).toEqual(['acme']))
  })

  it('refreshes when a starting plugin server becomes ready through an origin-less notification', async () => {
    let ready = false
    const sendRequestRaw = vi.fn(async () => ({ data: [pluginServer('acme', ready)], nextCursor: null }))
    installDesktopApiMock({ appServer: { sendRequestRaw } })
    useMcpStore.getState().upsertStatus({ name: 'acme', enabled: true, startupState: 'starting', transport: 'stdio' })

    const { result } = renderHook(() => usePluginSettingsServers('acme', true))
    await waitFor(() => expect(sendRequestRaw).toHaveBeenCalledTimes(1))
    expect(result.current).toEqual([])

    ready = true
    act(() => {
      useMcpStore.getState().upsertStatus({ name: 'acme', enabled: true, startupState: 'ready', transport: 'stdio' })
    })

    await waitFor(() => expect(result.current.map((server) => server.status.name)).toEqual(['acme']))
  })
})
