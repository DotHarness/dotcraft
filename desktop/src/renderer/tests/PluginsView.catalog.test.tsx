import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { usePluginStore } from '../stores/pluginStore'
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

  it('shows workspace plugins by default under Installed locally', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [browserUsePlugin, localPlugin],
      diagnostics: [], snapshotRevision: 1
    })

    renderPluginsView()

    expect(await screen.findByText('Installed locally')).toBeInTheDocument()
    expect(screen.getByText('External Process Echo')).toBeInTheDocument()
    expect(screen.getByText('Browser')).toBeInTheDocument()
    expect(screen.getByText('All publishers')).toBeInTheDocument()
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

  it('does not render a separate native app catalog section', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [workflowPlugin, browserUsePlugin],
      diagnostics: [], snapshotRevision: 1
    })

    renderPluginsView()

    expect((await screen.findAllByText('Workflow App')).length).toBeGreaterThan(0)
    expect(screen.queryByText('Native apps')).not.toBeInTheDocument()
  })

  it('shows the fixed category set including Productivity', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [workflowPlugin, browserUsePlugin],
      diagnostics: [], snapshotRevision: 1
    })

    renderPluginsView()

    expect(await screen.findByText('Workflow App')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Filter plugin category' }))

    expect(screen.getByRole('menuitem', { name: 'Coding' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Design' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Engineering' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Lifestyle' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Productivity' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Research' })).toBeInTheDocument()
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

    expect(await screen.findByText('Plugin diagnostics')).toBeInTheDocument()
    expect(screen.getByText('MissingPluginCapabilities')).toBeInTheDocument()
    expect(screen.getByText('Plugin manifest must declare a skills path or at least one tool.')).toBeInTheDocument()
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

  it('refreshes plugins from the toolbar refresh action', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [browserUsePlugin],
      diagnostics: [], snapshotRevision: 1
    })

    renderPluginsView()

    expect(await screen.findByText('Browser')).toBeInTheDocument()
    const initialCalls = appServerSendRequest.mock.calls.length

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.length).toBeGreaterThan(initialCalls)
    })
  })
})
