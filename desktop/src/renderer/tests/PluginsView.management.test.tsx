import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { useAppBindingStore } from '../stores/appBindingStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import {
  appServerSendRequest,
  browserUsePlugin,
  confirmDialog,
  gitSkill,
  localPlugin,
  memorySkill,
  renderPluginsView,
  setupPluginsViewTest,
  shellGetProtocolHandlerName,
  shellOpenExternal,
  workflowAppInfo,
  workflowPlugin
} from './pluginsViewTestFixtures'

describe('PluginsView management', () => {
  beforeEach(setupPluginsViewTest)

  it('hides connected apps on details for uninstalled app plugins', async () => {
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: {
        pluginManagement: true,
        appBindingVersion: 2
      }
    })
    useAppBindingStore.setState({
      apps: [workflowAppInfo()],
      appsLoading: false,
      appsError: null
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: workflowPlugin, snapshotRevision: 1 }
      if (method === 'app/list') return { apps: [workflowAppInfo()] }
      return {}
    })

    renderPluginsView()

    const workflowLabel = await screen.findByText('Workflow App')
    const workflowRow = workflowLabel.closest('[role="button"]')
    expect(workflowRow).toBeTruthy()
    fireEvent.click(workflowRow!)

    expect(await screen.findByRole('heading', { name: 'Workflow App' })).toBeInTheDocument()
    expect(screen.queryByText('Connected Apps')).not.toBeInTheDocument()
  })

  it('shows only workspace connection state on plugin details', async () => {
    const installedWorkflowApp = { ...workflowPlugin, installed: true, enabled: true, installable: false }
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: {
        pluginManagement: true,
        appBindingVersion: 2
      }
    })
    useThreadStore.getState().setActiveThreadId('thread-1')
    shellGetProtocolHandlerName.mockResolvedValue('Workflow App')
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [installedWorkflowApp], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: installedWorkflowApp, snapshotRevision: 1 }
      if (method === 'thread/appBindings/refresh') {
        return { bindings: [{ bindingId: 'binding-1', state: 'offline', attachedToolCount: 0 }] }
      }
      if (method === 'thread/appBindings/list') return { bindings: [] }
      if (method === 'app/list') {
        return {
          apps: [
            workflowAppInfo({
              nativeStatus: 'installed',
              connectionState: 'connected',
              bindingState: 'offline'
            })
          ]
        }
      }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Workflow App'))

    expect(await screen.findByText('App Settings')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Connected' })).toBeInTheDocument()
    expect(screen.queryByText('Offline')).not.toBeInTheDocument()
    expect(screen.queryByText('Last approved capabilities')).not.toBeInTheDocument()
    expect(screen.queryByText('workflow.ReadBoard')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open app' })).not.toBeInTheDocument()
    expect(screen.queryByText('Connected Apps')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Connected' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Disconnect' }))
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('app/connection/revoke', { appId: 'com.example.workflow' })
      expect(confirmDialog).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
    })
  })

  it('shows remove for removable local plugins and refreshes after confirmation', async () => {
    let removed = false
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') {
        return { plugins: removed ? [browserUsePlugin] : [browserUsePlugin, localPlugin], diagnostics: [], snapshotRevision: removed ? 2 : 1 }
      }
      if (method === 'plugin/view') return { plugin: localPlugin, snapshotRevision: 1 }
      if (method === 'plugin/remove') {
        removed = true
        return { outcome: 'applied', plugin: null, snapshotRevision: 2 }
      }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('External Process Echo'))
    await screen.findByRole('heading', { name: 'External Process Echo' })
    expect(screen.queryByRole('button', { name: 'Uninstall' })).not.toBeInTheDocument()
    fireEvent.click(await screen.findByRole('button', { name: 'More actions' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Uninstall' }))

    await waitFor(() => {
      expect(confirmDialog).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
      expect(appServerSendRequest).toHaveBeenCalledWith('plugin/remove', { id: 'external-process-echo' })
    })
    expect(screen.queryByRole('button', { name: 'Uninstall' })).not.toBeInTheDocument()
  })

  it('reports a rejected removal diagnostic without claiming success', async () => {
    const diagnostic = 'The plugin directory could not be deleted.'
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') {
        return { plugins: [localPlugin], diagnostics: [], snapshotRevision: 2 }
      }
      if (method === 'plugin/view') return { plugin: localPlugin, snapshotRevision: 2 }
      if (method === 'plugin/remove') {
        return {
          outcome: 'notApplied',
          plugin: localPlugin,
          affectedPlugins: [],
          diagnostics: [
            {
              severity: 'error',
              code: 'PluginFilesystemCommitFailed',
              message: diagnostic,
              pluginId: localPlugin.id
            }
          ],
          snapshotRevision: 2
        }
      }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByText('External Process Echo'))
    await screen.findByRole('heading', { name: 'External Process Echo' })
    fireEvent.click(await screen.findByRole('button', { name: 'More actions' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Uninstall' }))

    await waitFor(() => {
      expect(useToastStore.getState().toasts).toEqual(
        expect.arrayContaining([expect.objectContaining({ message: diagnostic, type: 'error' })])
      )
    })
    expect(useToastStore.getState().toasts).not.toEqual(
      expect.arrayContaining([expect.objectContaining({ message: 'Plugin uninstalled', type: 'success' })])
    )
    expect(screen.getByRole('heading', { name: 'External Process Echo' })).toBeInTheDocument()
  })

  it('opens Manage with an isolated plugin filter and restores it after returning from details', async () => {
    appServerSendRequest.mockImplementation(async (method: string, params?: { id?: string }) => {
      if (method === 'plugin/list') return { plugins: [browserUsePlugin, localPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: params?.id === localPlugin.id ? localPlugin : browserUsePlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByText('External Process Echo'))
    await screen.findByRole('heading', { name: 'External Process Echo' })
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Manage' }))

    const search = await screen.findByPlaceholderText('Search installed plugins')
    expect(search).toHaveValue('External Process Echo')
    expect(screen.queryByText('Browser')).not.toBeInTheDocument()

    fireEvent.click(screen.getByText('External Process Echo'))
    await screen.findByRole('heading', { name: 'External Process Echo' })
    fireEvent.click(screen.getByRole('button', { name: 'Plugins' }))
    expect(await screen.findByPlaceholderText('Search installed plugins')).toHaveValue('External Process Echo')

    fireEvent.change(screen.getByPlaceholderText('Search installed plugins'), { target: { value: '' } })
    expect(await screen.findByText('Browser')).toBeInTheDocument()
  })

  it('hides remove for installed plugins that are not removable', async () => {
    const externalRootPlugin = { ...localPlugin, removable: false, source: 'explicit' }
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [externalRootPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: externalRootPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('External Process Echo'))

    expect(await screen.findByRole('button', { name: 'Try in chat' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Uninstall' })).not.toBeInTheDocument()
  })

  it('opens plugin detail links in the external browser', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [localPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: localPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('External Process Echo'))
    fireEvent.click((await screen.findAllByLabelText('Website'))[0]!)
    fireEvent.click(await screen.findByLabelText('Privacy policy'))

    expect(shellOpenExternal).toHaveBeenCalledWith('https://example.com/external-process-echo')
    expect(shellOpenExternal).toHaveBeenCalledWith('https://example.com/privacy')
  })

  it('keeps manage mode while switching between plugin and skill tabs', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') {
        return { plugins: [browserUsePlugin, localPlugin], diagnostics: [], snapshotRevision: 1 }
      }
      if (method === 'skills/list') {
        return { skills: [memorySkill, gitSkill] }
      }
      return {}
    })

    renderPluginsView()

    expect(await screen.findByText('Browser')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))

    expect(await screen.findByText('Plugins 2')).toBeInTheDocument()
    expect(await screen.findByText('Skills 2')).toBeInTheDocument()
    expect(screen.queryByText('Apps 0')).not.toBeInTheDocument()
    expect(screen.queryByText('MCP 0')).not.toBeInTheDocument()
    const pluginsTab = screen.getByRole('button', { name: 'Plugins 2' })
    const skillsTab = screen.getByRole('button', { name: 'Skills 2' })
    expect(pluginsTab).toBeInTheDocument()
    expect(skillsTab).toBeInTheDocument()

    fireEvent.click(skillsTab)

    expect(await screen.findByText('Skills 2')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('Search installed skills')).toBeInTheDocument()
    expect(screen.getByText('Memory')).toBeInTheDocument()
    expect(screen.getByText('Git Local')).toBeInTheDocument()
    expect(screen.getAllByRole('switch')).toHaveLength(2)

    fireEvent.click(screen.getByRole('button', { name: 'Plugins 2' }))

    expect(await screen.findByText('Plugins 2')).toBeInTheDocument()
    expect(await screen.findByPlaceholderText('Search installed plugins')).toBeInTheDocument()
    expect(screen.getByText('External Process Echo')).toBeInTheDocument()
  })

  // The detail page presents the plugin; the manage list is where its state changes,
  // so one control governs enablement rather than two that can disagree.
  it('leaves plugin enablement to the manage list rather than the detail page', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [localPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: localPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByText('External Process Echo'))

    expect(await screen.findByText('Info')).toBeInTheDocument()
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()
  })
})
