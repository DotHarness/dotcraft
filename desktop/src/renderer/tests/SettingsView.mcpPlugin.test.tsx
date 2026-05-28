import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SettingsView } from '../components/settings/SettingsView'
import { SettingsSidebar } from '../components/layout/SettingsSidebar'
import { useConnectionStore } from '../stores/connectionStore'
import { usePluginStore } from '../stores/pluginStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const workspaceConfigGetCore = vi.fn()
const appServerSendRequest = vi.fn()

function renderView(): void {
  render(
    <LocaleProvider>
      <div style={{ display: 'flex', height: 800 }}>
        <SettingsSidebar />
        <SettingsView workspacePath="E:\\Git\\dotcraft" />
      </div>
    </LocaleProvider>
  )
}

describe('SettingsView plugin MCP servers', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en', connectionMode: 'stdio', visibleChannels: [] })
    settingsSet.mockResolvedValue(undefined)
    workspaceConfigGetCore.mockResolvedValue({
      workspace: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        defaultApprovalPolicy: null
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'channel/list') return { channels: [] }
      if (method === 'mcp/list') {
        return {
          servers: [
            {
              name: 'workspace-docs',
              enabled: true,
              transport: 'stdio',
              command: 'node',
              origin: { kind: 'workspace' },
              readOnly: false
            },
            {
              name: 'review-tools:review',
              enabled: true,
              transport: 'stdio',
              origin: {
                kind: 'plugin',
                pluginId: 'review-tools',
                pluginDisplayName: 'Review Tools',
                declaredName: 'review'
              },
              readOnly: true
            }
          ]
        }
      }
      if (method === 'mcp/status/list') {
        return {
          servers: [
            { name: 'workspace-docs', enabled: true, startupState: 'disabled', transport: 'stdio' },
            { name: 'review-tools:review', enabled: true, startupState: 'ready', toolCount: 2, transport: 'stdio' }
          ]
        }
      }
      if (method === 'plugin/view') {
        return {
          plugin: {
            id: 'review-tools',
            displayName: 'Review Tools',
            enabled: true,
            installed: true,
            installable: false,
            removable: true,
            source: 'workspace',
            rootPath: '',
            functions: [],
            skills: [],
            mcpServers: []
          }
        }
      }
      return {}
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet, set: settingsSet },
        workspaceConfig: { getCore: workspaceConfigGetCore },
        appServer: {
          sendRequest: appServerSendRequest,
          restartManaged: vi.fn(),
          getResolvedBinary: vi.fn().mockResolvedValue({ path: null }),
          pickBinary: vi.fn()
        },
        modules: { list: vi.fn().mockResolvedValue([]) },
        workspace: {
          pickFolder: vi.fn(),
          viewer: { browserUse: { clearCookies: vi.fn() } }
        },
        shell: { openExternal: vi.fn() }
      }
    })

    useConnectionStore.getState().reset()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        workspaceConfigManagement: true,
        mcpManagement: true,
        mcpStatus: true,
        mcpServerOrigins: true,
        pluginManagement: true
      }
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
    useUIStore.setState({ activeMainView: 'settings', activeSettingsTab: 'general', sidebarCollapsed: false })
  })

  it('renders plugin-origin MCP rows read-only and opens the owning plugin', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'MCP' }))

    expect(await screen.findByText('review-tools:review')).toBeInTheDocument()
    expect(screen.getByText('From Review Tools')).toBeInTheDocument()
    expect(screen.queryByRole('switch', { name: 'Toggle MCP server review-tools:review' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'View plugin' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('plugin/view', { id: 'review-tools' })
    })
    expect(useUIStore.getState().activeMainView).toBe('skills')
  })
})
