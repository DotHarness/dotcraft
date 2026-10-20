import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { usePluginStore } from '../stores/pluginStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useToastStore } from '../stores/toastStore'
import { translate } from '../../shared/locales'
import {
  appServerSendRequest,
  browserUsePlugin,
  confirmDialog,
  localPlugin,
  renderPluginsView,
  setupPluginsViewTest,
  workflowPlugin,
  workspacePickFolder
} from './pluginsViewTestFixtures'

describe('PluginsView catalog', () => {
  beforeEach(setupPluginsViewTest)
  afterEach(() => useWorkspaceProjectsStore.getState().reset())

  it('shows installed and catalog plugins by default', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [browserUsePlugin, localPlugin],
      diagnostics: [], snapshotRevision: 1
    })

    renderPluginsView()

    expect(await screen.findByText('External Process Echo')).toBeInTheDocument()
    expect(screen.getByText('Browser')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Filter plugins' })).toBeInTheDocument()
  })

  it('groups plugins installed in the Chats workspace under Chats rather than its folder name', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/home/me/.craft/workspaces/chats',
      secondaryLimit: 8,
      projects: []
    })
    appServerSendRequest.mockResolvedValue({ plugins: [localPlugin], diagnostics: [], snapshotRevision: 1 })

    renderPluginsView()

    const chats = translate('en', 'chatsRail.title')
    expect(await screen.findByText(translate('en', 'plugins.section.installedInWorkspace', { name: chats }))).toBeInTheDocument()
  })

  it('installs a plugin from a picked disk folder via plugin/installLocal', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'plugin/installLocal') {
        return Promise.resolve({
          plugin: { ...browserUsePlugin, id: 'disk-plugin', installed: true, enabled: true, removable: true },
          snapshotRevision: 2
        })
      }
      return Promise.resolve({ plugins: [browserUsePlugin], diagnostics: [], snapshotRevision: 1 })
    })
    workspacePickFolder.mockResolvedValue('/disk/my-plugin')

    renderPluginsView()
    await screen.findByText('Browser')

    fireEvent.click(screen.getByRole('button', { name: 'More create options' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Install from disk' }))

    await waitFor(() => {
      expect(workspacePickFolder).toHaveBeenCalledWith({ title: 'Select plugin folder' })
      expect(appServerSendRequest).toHaveBeenCalledWith('plugin/installLocal', { path: '/disk/my-plugin' })
    })
  })

  it('does not call plugin/installLocal when the folder picker is cancelled', async () => {
    appServerSendRequest.mockResolvedValue({ plugins: [browserUsePlugin], diagnostics: [], snapshotRevision: 1 })
    workspacePickFolder.mockResolvedValue(null)

    renderPluginsView()
    await screen.findByText('Browser')

    fireEvent.click(screen.getByRole('button', { name: 'More create options' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Install from disk' }))

    await waitFor(() => expect(workspacePickFolder).toHaveBeenCalled())
    expect(appServerSendRequest).not.toHaveBeenCalledWith('plugin/installLocal', expect.anything())
  })

  it('does not open the folder picker when the local install caution is declined', async () => {
    appServerSendRequest.mockResolvedValue({ plugins: [browserUsePlugin], diagnostics: [], snapshotRevision: 1 })
    confirmDialog.mockResolvedValue(false)

    renderPluginsView()
    await screen.findByText('Browser')

    fireEvent.click(screen.getByRole('button', { name: 'More create options' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Install from disk' }))

    await waitFor(() => expect(confirmDialog).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Install a plugin from a folder?'
    })))
    expect(workspacePickFolder).not.toHaveBeenCalled()
  })

  it('hides install from disk for remote workspaces', async () => {
    useConversationStore.setState({ remoteWorkspaceActive: true })
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { pluginManagement: true, pluginMarketplaces: true }
    })
    appServerSendRequest.mockResolvedValue({ plugins: [browserUsePlugin], diagnostics: [], snapshotRevision: 1 })

    renderPluginsView()
    await screen.findByText('Browser')

    fireEvent.click(screen.getByRole('button', { name: 'More create options' }))

    expect(await screen.findByRole('menuitem', { name: 'Create plugin' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Install from disk' })).not.toBeInTheDocument()
    expect(workspacePickFolder).not.toHaveBeenCalled()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('plugin/installLocal', expect.anything())
  })

  it('renders plugin diagnostics returned by plugin/list', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [browserUsePlugin],
      diagnostics: [
        {
          severity: 'error',
          code: 'MissingPluginCapabilities',
          message: 'Plugin manifest must declare a skills path or at least one tool.',
          pluginId: 'broken-plugin',
          path: 'X:\\fixtures\\workspace\\.craft\\plugins\\broken-plugin\\.craft-plugin\\plugin.json'
        }
      ],
      snapshotRevision: 1
    })

    renderPluginsView()

    expect(await screen.findByText('Needs attention')).toBeInTheDocument()
    // A code the client carries no copy for falls back to the server's English message.
    expect(screen.getByText('Plugin manifest must declare a skills path or at least one tool.')).toBeInTheDocument()
    expect(screen.queryByText('MissingPluginCapabilities')).not.toBeInTheDocument()
    expect(usePluginStore.getState().diagnostics).toHaveLength(1)
  })

  it('does not refetch plugins when the window regains focus', async () => {
    appServerSendRequest.mockResolvedValue({ plugins: [browserUsePlugin], diagnostics: [], snapshotRevision: 1 })

    renderPluginsView()

    expect(await screen.findByText('Browser')).toBeInTheDocument()
    const initialCalls = appServerSendRequest.mock.calls.length

    fireEvent.focus(window)

    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(appServerSendRequest.mock.calls.length).toBe(initialCalls)
  })

  it('refreshes marketplace snapshots before re-reading plugins from the toolbar', async () => {
    useConnectionStore.getState().setStatus({
      status: 'connected', capabilities: { pluginManagement: true, pluginMarketplaces: true }
    })
    let refreshed = false
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'marketplace/refresh') {
        refreshed = true
        return { errors: [] }
      }
      return { plugins: [{ ...browserUsePlugin, version: refreshed ? '0.6.2' : '0.6.1' }], diagnostics: [], snapshotRevision: 1 }
    })
    renderPluginsView()
    await screen.findByText('Browser')

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => expect(usePluginStore.getState().plugins[0].version).toBe('0.6.2'))
    expect(appServerSendRequest).toHaveBeenNthCalledWith(2, 'marketplace/refresh', {})
    expect(appServerSendRequest).toHaveBeenNthCalledWith(3, 'plugin/list', { includeDisabled: true })
  })

  it('keeps refresh local when marketplace management is unavailable', async () => {
    appServerSendRequest.mockResolvedValue({ plugins: [browserUsePlugin], diagnostics: [], snapshotRevision: 1 })
    renderPluginsView()
    await screen.findByText('Browser')

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => expect(appServerSendRequest).toHaveBeenCalledTimes(2))
    expect(appServerSendRequest).toHaveBeenLastCalledWith('plugin/list', { includeDisabled: true })
  })

  it('reports per-marketplace refresh failures without a success toast', async () => {
    useConnectionStore.getState().setStatus({
      status: 'connected', capabilities: { pluginManagement: true, pluginMarketplaces: true }
    })
    appServerSendRequest.mockImplementation(async (method: string) => method === 'marketplace/refresh'
      ? { errors: [{ name: 'default', code: 'MarketplaceFetchFailed', message: 'Download failed' }] }
      : { plugins: [browserUsePlugin], diagnostics: [], snapshotRevision: 1 })
    renderPluginsView()
    await screen.findByText('Browser')

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => expect(useToastStore.getState().toasts).toEqual([
      expect.objectContaining({ message: 'Download failed', type: 'error' })
    ]))
    expect(usePluginStore.getState().plugins[0].id).toBe('browser')
  })
})
