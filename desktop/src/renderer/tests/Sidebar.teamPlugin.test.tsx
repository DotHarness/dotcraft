import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { Sidebar } from '../components/layout/Sidebar'
import { useConnectionStore } from '../stores/connectionStore'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()

const agentTeamsPlugin: PluginEntry = {
  id: 'agent-teams',
  displayName: 'Agent Teams',
  description: 'Run a small team of DotCraft agents.',
  version: '0.1.0',
  enabled: true,
  installed: true,
  installable: true,
  removable: true,
  source: 'builtin',
  rootPath: '',
  interface: {
    displayName: 'Agent Teams',
    shortDescription: 'Run a small team of DotCraft agents.'
  },
  functions: [],
  skills: [],
  apps: [],
  mcpServers: [],
  lspServers: [],
  desktopExtensions: [
    {
      id: 'team-card-board',
      displayName: 'Team card board',
      description: 'Adds the Agent Teams card board to DotCraft Desktop.',
      entry: 'Z:\\__dotcraft_fixture__\\plugins\\agent-teams\\desktop\\team-card-board.mjs',
      styles: [],
      permissions: ['appServer:teams/*', 'navigation'],
      requiredAppIds: [],
      connectOrigins: [],
      surfaces: [
        {
          type: 'mainView',
          viewId: 'teams',
          label: 'Team',
          placement: 'sidebar',
          order: 40
        },
        {
          type: 'pluginDetail',
          title: 'Team Board',
          description: 'Unlocks the Team card board surface for robot teammates, missions, planning, and task dispatch.'
        }
      ]
    }
  ]
}

function renderSidebar(): void {
  render(
    <LocaleProvider>
      <Sidebar workspaceName="dotcraft" workspacePath="F:\\dotcraft" />
    </LocaleProvider>
  )
}

describe('Sidebar Team plugin gate', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet
        },
        workspace: {
          getRecent: vi.fn().mockResolvedValue([]),
          clearSelection: vi.fn().mockResolvedValue(undefined),
          switch: vi.fn().mockResolvedValue(undefined),
          clearRecent: vi.fn().mockResolvedValue(undefined)
        },
        shell: {
          openPath: vi.fn().mockResolvedValue(undefined)
        }
      }
    })

    useConnectionStore.getState().reset()
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { pluginManagement: true }
    })
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'conversation',
      sidebarCollapsed: false,
      sidebarPreferredCollapsed: false
    })
    usePluginStore.setState({
      plugins: [],
      diagnostics: [],
      loading: false,
      error: null,
      selectedPluginId: null,
      selectedPlugin: null,
      detailLoading: false
    })
  })

  it('hides Team when the agent-teams plugin is not installed and enabled', () => {
    renderSidebar()

    expect(screen.queryByRole('button', { name: 'Team' })).not.toBeInTheDocument()
  })

  it('shows Team after the agent-teams plugin is installed and enabled', () => {
    usePluginStore.setState({ plugins: [agentTeamsPlugin] })

    renderSidebar()

    expect(screen.getByRole('button', { name: 'Team' })).toBeInTheDocument()
  })

  it('hides Team when the installed plugin is disabled, including collapsed mode', () => {
    usePluginStore.setState({
      plugins: [{ ...agentTeamsPlugin, enabled: false }]
    })
    useUIStore.setState({
      sidebarCollapsed: true,
      sidebarPreferredCollapsed: true
    })

    renderSidebar()

    expect(screen.queryByRole('button', { name: 'Team' })).not.toBeInTheDocument()
  })
})
