import { act, fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useToastStore } from '../stores/toastStore'
import {
  appServerSendRequest,
  browserUsePlugin,
  installedDotNetPlugin,
  renderPluginsView,
  setupPluginsViewTest
} from './pluginsViewTestFixtures'

const disabledPlugin: PluginEntry = {
  ...browserUsePlugin,
  installed: true,
  installable: false,
  enabled: false
}

describe('PluginsView detail activation', () => {
  beforeEach(setupPluginsViewTest)

  it('keeps Enable busy in place, triggers once, then shows Try in chat', async () => {
    let finishEnable: ((value: unknown) => void) | undefined
    const enableResult = new Promise((resolve) => {
      finishEnable = resolve
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [disabledPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: disabledPlugin, snapshotRevision: 1 }
      if (method === 'plugin/setEnabled') return enableResult
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByText('Browser'))

    const enableButton = await screen.findByRole('button', { name: 'Enable' })
    expect(screen.queryByRole('button', { name: 'Try in chat' })).not.toBeInTheDocument()
    fireEvent.click(enableButton)

    await waitFor(() => {
      expect(enableButton).toBeDisabled()
      expect(enableButton).toHaveAttribute('aria-busy', 'true')
      expect(screen.getByRole('button', { name: 'Enabling…' })).toBe(enableButton)
    })
    fireEvent.click(enableButton)
    expect(appServerSendRequest.mock.calls.filter(([method]) => method === 'plugin/setEnabled')).toHaveLength(1)

    finishEnable?.({
      outcome: 'applied',
      plugin: { ...disabledPlugin, enabled: true },
      affectedPlugins: [],
      snapshotRevision: 2
    })

    expect(await screen.findByRole('button', { name: 'Try in chat' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Enable' })).not.toBeInTheDocument()
  })

  it('restores Enable and reports the standard error when activation fails', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [disabledPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: disabledPlugin, snapshotRevision: 1 }
      if (method === 'plugin/setEnabled') throw new Error('offline')
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByText('Browser'))
    fireEvent.click(await screen.findByRole('button', { name: 'Enable' }))

    expect(await screen.findByRole('button', { name: 'Enable' })).toBeEnabled()
    await waitFor(() => {
      expect(useToastStore.getState().toasts.at(-1)?.message).toBe('Failed to update plugin')
    })
  })

  it('routes an untrusted in-process plugin through the existing authorization dialog', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [installedDotNetPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: installedDotNetPlugin, snapshotRevision: 1 }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByText('Review Core'))
    fireEvent.click(await screen.findByRole('button', { name: 'Enable' }))

    expect(await screen.findByText('Security authorization')).toBeInTheDocument()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('plugin/setEnabled', expect.anything())
  })

  it('keeps manage rows mounted while a toggle snapshot refresh is pending', async () => {
    const enabledPlugin = { ...disabledPlugin, enabled: true }
    let listCalls = 0
    let finishRefresh: ((value: unknown) => void) | undefined
    let finishToggle: ((value: unknown) => void) | undefined
    const refreshResult = new Promise((resolve) => { finishRefresh = resolve })
    const toggleResult = new Promise((resolve) => { finishToggle = resolve })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') {
        listCalls += 1
        if (listCalls === 1) return { plugins: [enabledPlugin], diagnostics: [], snapshotRevision: 1 }
        return refreshResult
      }
      if (method === 'plugin/setEnabled') return toggleResult
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByRole('button', { name: 'Manage' }))
    const toggle = await screen.findByRole('switch', { name: 'Browser enabled' })
    fireEvent.click(toggle)

    expect(toggle).toBeDisabled()
    expect(toggle).toHaveAttribute('aria-busy', 'true')
    act(() => usePluginStore.getState().handleSnapshotUpdated(2))

    await waitFor(() => expect(usePluginStore.getState().loading).toBe(true))
    expect(screen.getByText('Browser')).toBeInTheDocument()
    expect(screen.queryByRole('status', { name: 'Loading plugins…' })).not.toBeInTheDocument()

    const disabledResult = {
      outcome: 'applied',
      plugin: disabledPlugin,
      affectedPlugins: [],
      snapshotRevision: 2
    }
    finishToggle?.(disabledResult)
    finishRefresh?.({ plugins: [disabledPlugin], diagnostics: [], snapshotRevision: 2 })

    await waitFor(() => expect(screen.getByRole('switch', { name: 'Browser enabled' })).toBeEnabled())
  })
})
