import { fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { LocaleProvider } from '../contexts/LocaleContext'
import { parseConfigInput } from '../components/plugins/settings/pluginConfigModel'
import { PluginConfigSettings } from '../components/plugins/settings/PluginConfigSettings'
import { usePluginConfig, type PluginConfigState } from '../components/plugins/settings/usePluginConfig'
import { useConnectionStore } from '../stores/connectionStore'
import { installDesktopApiMock } from './desktopApiMock'

const snapshot = {
  schema: {
    fields: [
      { key: 'endpoint', type: 'text' as const, title: 'Endpoint' },
      { key: 'limit', type: 'number' as const, title: 'Limit', min: 1, max: 10 }
    ]
  },
  personal: { limit: 5 },
  workspace: { endpoint: 'https://example.test' },
  value: { endpoint: 'https://example.test', limit: 5 },
  writableScopes: ['personal', 'workspace'] as const
}

function notDeclared(): Error {
  return Object.assign(new Error('not declared'), { rpcCode: -32099, data: { code: 'PluginConfigurationNotDeclared' } })
}

beforeEach(() => {
  useConnectionStore.setState((state) => ({
    capabilities: { ...state.capabilities, pluginConfiguration: true } as typeof state.capabilities,
    connectionEpoch: state.connectionEpoch + 1
  }))
})

describe('plugin configuration settings', () => {
  it('stays hidden for a plugin without a settings schema', async () => {
    const sendRequestRaw = vi.fn(async () => { throw notDeclared() })
    installDesktopApiMock({ appServer: { sendRequestRaw, onNotificationRaw: () => () => {} } })

    const { result } = renderHook(() => usePluginConfig('acme', true))

    await waitFor(() => expect(sendRequestRaw).toHaveBeenCalledWith('plugin/config/get', { id: 'acme' }))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.declared).toBe(false)
    expect(result.current.failed).toBe(false)
  })

  it('writes changes to the personal scope', async () => {
    const sendRequestRaw = vi.fn(async (method: string) => method === 'plugin/config/get'
      ? snapshot
      : { ...snapshot, personal: { limit: 7 }, value: { ...snapshot.value, limit: 7 } })
    installDesktopApiMock({ appServer: { sendRequestRaw, onNotificationRaw: () => () => {} } })

    const { result } = renderHook(() => usePluginConfig('acme', true))
    await waitFor(() => expect(result.current.declared).toBe(true))
    result.current.set('limit', 7)

    await waitFor(() => expect(result.current.snapshot?.value.limit).toBe(7))
    expect(sendRequestRaw).toHaveBeenCalledWith('plugin/config/mutate', {
      id: 'acme',
      scope: 'personal',
      operations: [{ op: 'set', key: 'limit', value: 7 }]
    })
  })

  it('locks workspace-set fields and resets a personal override', () => {
    const state: PluginConfigState = {
      declared: true,
      snapshot,
      loading: false,
      failed: false,
      pendingChange: null,
      set: vi.fn(),
      reset: vi.fn(),
      retry: vi.fn()
    }
    render(<LocaleProvider loadSettings={false}><PluginConfigSettings state={state} /></LocaleProvider>)

    expect(screen.getByLabelText('Endpoint')).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Reset to default' }))
    expect(state.reset).toHaveBeenCalledWith('limit')
  })

  it('validates typed numbers and JSON before saving', () => {
    const limit = snapshot.schema.fields[1]
    expect(parseConfigInput(limit, '3')).toEqual({ value: 3 })
    expect(parseConfigInput(limit, '12')).toEqual({ error: 'range' })
    expect(parseConfigInput({ key: 'advanced', type: 'json' }, '{"a":')).toEqual({ error: 'json' })
  })
})
