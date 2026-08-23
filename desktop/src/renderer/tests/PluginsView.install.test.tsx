import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import type { PluginEntry } from '../stores/pluginStore'
import {
  agentTeamsPlugin,
  appServerSendRequest,
  browserUsePlugin,
  dotnetPlugin,
  installedDotnetPlugin,
  renderPluginsView,
  setupPluginsViewTest,
  shellGetProtocolHandlerName,
  workflowAppInfo,
  workflowPlugin
} from './pluginsViewTestFixtures'

describe('PluginsView installation', () => {
  beforeEach(setupPluginsViewTest)

  it('shows ordinary plugin install first for app plugins', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: workflowPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    const installButton = await screen.findByRole('button', { name: 'Install' })
    expect(installButton).toHaveClass('dc-plugin-install-button')
    fireEvent.click(installButton)

    expect(await screen.findByRole('heading', { name: 'Install Workflow App' })).toBeInTheDocument()
    expect((await screen.findAllByText('Workflow App')).length).toBeGreaterThan(0)
    expect(screen.getByText('App')).toBeInTheDocument()
    expect(screen.getByText('workflow')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add to DotCraft' })).toBeInTheDocument()
    expect(screen.queryByText('Install or open Workflow App')).not.toBeInTheDocument()
    expect(screen.queryByText('Connect Workflow App')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Install app' })).not.toBeInTheDocument()
  })

  it('keeps the install dialog free of a warning that the authorization step already carries', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [dotnetPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: dotnetPlugin, snapshotRevision: 1 }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))

    expect(await screen.findByRole('heading', { name: 'Install Review Core' })).toBeInTheDocument()
    expect(screen.queryByText('Security authorization')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add to DotCraft' })).toBeInTheDocument()
  })

  it('shows only the native app install stage after installing an app plugin with a missing app', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: workflowPlugin, snapshotRevision: 1 }
      if (method === 'plugin/install') return { plugin: { ...workflowPlugin, installed: true, enabled: true, installable: false }, snapshotRevision: 2 }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'app/list') return { apps: [workflowAppInfo({ nativeStatus: 'missing' })] }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))

    fireEvent.click(screen.getByRole('button', { name: 'Add to DotCraft' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('plugin/install', { id: 'workflow' })
    })
    expect(await screen.findByRole('heading', { name: 'Complete setup Workflow App' })).toBeInTheDocument()
    expect(screen.getByText('Required app')).toBeInTheDocument()
    expect(await screen.findByText('Install or open Workflow App')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Install app' })).toBeInTheDocument()
    expect(screen.queryByText('Connect Workflow App')).not.toBeInTheDocument()
  })

  it('shows only the connect stage after installing an app plugin when the native app is installed', async () => {
    shellGetProtocolHandlerName.mockResolvedValue('Workflow App')
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: workflowPlugin, snapshotRevision: 1 }
      if (method === 'plugin/install') return { plugin: { ...workflowPlugin, installed: true, enabled: true, installable: false }, snapshotRevision: 2 }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'app/list') return { apps: [workflowAppInfo({ nativeStatus: 'installed' })] }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))
    fireEvent.click(screen.getByRole('button', { name: 'Add to DotCraft' }))

    expect(await screen.findByText('Connect required app')).toBeInTheDocument()
    expect(await screen.findByText('Connect Workflow App')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Connect' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Install app' })).not.toBeInTheDocument()
  })

  it('shows a handoff-opened waiting state while app connection is pending', async () => {
    shellGetProtocolHandlerName.mockResolvedValue('Workflow App')
    let connectionState = 'notConnected'
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: workflowPlugin, snapshotRevision: 1 }
      if (method === 'plugin/install') return { plugin: { ...workflowPlugin, installed: true, enabled: true, installable: false }, snapshotRevision: 2 }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'app/list') return { apps: [workflowAppInfo({ nativeStatus: 'installed', connectionState })] }
      if (method === 'app/connection/start') {
        connectionState = 'connecting'
        return {
          connectionRequestId: 'connection-1',
          appId: 'com.example.workflow',
          state: 'connecting',
          expiresAt: '2026-05-18T00:00:00Z',
          handoff: { mode: 'customProtocol', uri: 'workflow://dotcraft/connect?request=connection-1' }
        }
      }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))
    fireEvent.click(screen.getByRole('button', { name: 'Add to DotCraft' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Connect' }))

    expect(await screen.findByText('Waiting for confirmation in the app')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Link opened' })).toBeDisabled()
    expect(screen.getAllByRole('button', { name: 'Refresh' }).some((button) => !button.hasAttribute('aria-label'))).toBe(true)

    connectionState = 'connected'
    expect(await screen.findByText('Setup complete')).toBeInTheDocument()
  })

  it('shows the completion state when required apps are already connected', async () => {
    shellGetProtocolHandlerName.mockResolvedValue('Workflow App')
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: workflowPlugin, snapshotRevision: 1 }
      if (method === 'plugin/install') return { plugin: { ...workflowPlugin, installed: true, enabled: true, installable: false }, snapshotRevision: 2 }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'app/list') return { apps: [workflowAppInfo({ nativeStatus: 'installed', connectionState: 'connected' })] }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))
    fireEvent.click(screen.getByRole('button', { name: 'Add to DotCraft' }))

    expect(await screen.findByRole('heading', { name: 'Complete setup Workflow App' })).toBeInTheDocument()
    expect(await screen.findByText('Setup complete')).toBeInTheDocument()
    expect(screen.getByText('Required apps are authorized')).toBeInTheDocument()
  })

  it('keeps no-app plugin installation as a single-button flow', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [browserUsePlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: browserUsePlugin, snapshotRevision: 1 }
      if (method === 'plugin/install') return { plugin: { ...browserUsePlugin, installed: true, enabled: true, installable: false }, snapshotRevision: 2 }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))

    expect(await screen.findByRole('heading', { name: 'Install Browser' })).toBeInTheDocument()
    const addButton = screen.getByRole('button', { name: 'Add to DotCraft' })
    expect(addButton).toBeInTheDocument()
    expect(addButton.style.width).toBe('100%')
    expect(screen.queryByText('Required app')).not.toBeInTheDocument()
  })

  it('asks for authority as a setup step after installing an in-process plugin', async () => {
    const installedUntrusted = { ...dotnetPlugin, installed: true, enabled: false, installable: false }
    let trusted = false
    let installed: PluginEntry = dotnetPlugin
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') {
        const plugin = trusted
          ? { ...installedUntrusted, enabled: true, dotnetRuntime: { ...installedUntrusted.dotnetRuntime, state: 'active', trustStatus: 'trusted', blockers: [] } }
          : installed
        return { plugins: [plugin], diagnostics: [], snapshotRevision: trusted ? 3 : 1 }
      }
      if (method === 'plugin/view') return { plugin: installed, snapshotRevision: 1 }
      if (method === 'plugin/install') {
        installed = installedUntrusted
        return { outcome: 'applied', plugin: installedUntrusted, affectedPlugins: [], snapshotRevision: 2 }
      }
      if (method === 'plugin/setTrusted') {
        trusted = true
        return { outcome: 'applied', plugin: installedUntrusted, affectedPlugins: [], snapshotRevision: 3 }
      }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Add to DotCraft' }))

    // Copying the bundle is not authority: the grant is a separate setup step.
    expect(await screen.findByText('Security authorization')).toBeInTheDocument()
    expect(appServerSendRequest).toHaveBeenCalledWith('plugin/install', { id: 'acme.review-core' })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('plugin/setTrusted', expect.anything())

    fireEvent.click(screen.getByRole('button', { name: 'Authorize' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('plugin/setTrusted', {
        id: 'acme.review-core',
        trusted: true
      })
    })
  })

  it('routes enabling an untrusted in-process plugin back through the setup dialog', async () => {
    const untrusted = { ...installedDotnetPlugin, enabled: false }
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [untrusted], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: untrusted, snapshotRevision: 1 }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Manage' }))
    fireEvent.click(await screen.findByRole('switch', { name: 'Review Core enabled' }))

    expect(await screen.findByText('Security authorization')).toBeInTheDocument()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('plugin/setEnabled', expect.anything())
  })

  it('shows Agent Teams desktop extension content in the install dialog', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [agentTeamsPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: agentTeamsPlugin, snapshotRevision: 1 }
      if (method === 'plugin/install') return { plugin: { ...agentTeamsPlugin, installed: true, enabled: true, installable: false }, snapshotRevision: 2 }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))

    expect(await screen.findByRole('heading', { name: 'Install Agent Teams' })).toBeInTheDocument()
    expect(screen.getByText('Team Board · Desktop Extension')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add to DotCraft' })).toBeInTheDocument()
  })
})
