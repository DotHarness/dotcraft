import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InstantInterruptRow } from '../components/settings/panels/instantInterrupt/InstantInterruptRow'
import { useComposerPreferencesStore } from '../stores/composerPreferencesStore'
import { useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useToastStore } from '../stores/toastStore'

const sendRequest = vi.fn()
let serverConfig: Record<string, unknown>

function renderRow(): void {
  render(<LocaleProvider><InstantInterruptRow /></LocaleProvider>)
}

function configReads(): number {
  return sendRequest.mock.calls.filter(([method]) => method === 'config/read').length
}

beforeEach(() => {
  serverConfig = { InstantInterruptEnabled: false, Tools: { CodeMode: { Mode: 'Only' } } }
  sendRequest.mockReset().mockImplementation(async (method: string) => {
    if (method === 'config/read') return { config: serverConfig, origins: {} }
    return { status: 'ok', version: 'sha256:1', filePath: '/remote/.craft/config.json' }
  })
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      appServer: { sendRequest },
      settings: { get: async () => ({ locale: 'en' }), set: async () => {} }
    }
  })
  useConfigStore.getState().reset()
  useToastStore.setState({ toasts: [] })
  useComposerPreferencesStore.setState({ followUpQueueMode: 'steer' })
  useConnectionStore.setState({ status: 'connected', capabilities: { workspaceConfigManagement: true } })
})

describe('configuration client', () => {
  it('shows the connected server value without reading local files', async () => {
    renderRow()

    await waitFor(() => expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false'))
    expect(sendRequest).toHaveBeenCalledWith('config/read', {})
    expect(sendRequest.mock.calls.every(([method]) => method === 'config/read')).toBe(true)
  })

  it('writes the key path when a setting is toggled', async () => {
    renderRow()
    await waitFor(() => expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false'))

    fireEvent.click(screen.getByRole('switch'))

    expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'true')
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'config/value/write',
      { keyPath: 'InstantInterruptEnabled', value: true, mergeStrategy: 'replace' },
      20_000
    ))
  })

  it('restores the previous value and reports an error when the write fails', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'config/read') return { config: serverConfig, origins: {} }
      throw new Error('configValidationError')
    })
    renderRow()
    await waitFor(() => expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false'))

    fireEvent.click(screen.getByRole('switch'))

    await waitFor(() => expect(useToastStore.getState().toasts.some((toast) => toast.type === 'error')).toBe(true))
    expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false')
    expect(screen.getByRole('switch')).not.toBeDisabled()
  })

  it('writes several key paths in one batch', async () => {
    await useConfigStore.getState().ensureLoaded()

    await useConfigStore.getState().write([
      { keyPath: 'ProviderId', value: 'openai' },
      { keyPath: 'ProviderPreferences.openai', value: { model: 'gpt-5' } }
    ])

    expect(sendRequest).toHaveBeenCalledWith('config/batchWrite', {
      edits: [
        { keyPath: 'ProviderId', value: 'openai', mergeStrategy: 'replace' },
        { keyPath: 'ProviderPreferences.openai', value: { model: 'gpt-5' }, mergeStrategy: 'replace' }
      ]
    }, 20_000)
  })

  it('refreshes when a change names a key path it holds', async () => {
    renderRow()
    await waitFor(() => expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false'))
    serverConfig = { ...serverConfig, InstantInterruptEnabled: true }

    act(() => useConfigStore.getState().handleConfigChanged(['InstantInterruptEnabled']))

    await waitFor(() => expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'true'))
    expect(configReads()).toBe(2)
  })

  it('ignores domain tags', async () => {
    await useConfigStore.getState().ensureLoaded()

    useConfigStore.getState().handleConfigChanged(['providers', 'skills', 'plugins.config', 'memory'])

    expect(configReads()).toBe(1)
  })
})
