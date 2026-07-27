import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'

import { LocaleProvider } from '../contexts/LocaleContext'
import { SettingsSidebar } from '../components/layout/SettingsSidebar'
import { SettingsView } from '../components/settings/SettingsView'
import { useConnectionStore } from '../stores/connectionStore'
import { useHooksStore, type HookMetadata } from '../stores/hooksStore'
import { usePluginStore } from '../stores/pluginStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const appServerSendRequest = vi.fn()

let hooks: HookMetadata[]

function renderView(): void {
  render(
    <LocaleProvider>
      <div style={{ display: 'flex', height: 800 }}>
        <SettingsSidebar />
        <SettingsView workspacePath="/workspace/dotcraft" />
      </div>
    </LocaleProvider>
  )
}

describe('Settings Hooks', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    hooks = [
      {
        key: '/user-config/.craft/hooks.json:pre_tool_use:0:0',
        eventName: 'PreToolUse',
        handlerType: 'command',
        matcher: 'shell_command',
        command: '/hooks/audit.sh',
        timeoutSec: 600,
        statusMessage: null,
        sourcePath: '/user-config/.craft/hooks.json',
        source: 'user',
        pluginId: null,
        displayOrder: 0,
        enabled: true,
        isManaged: false,
        currentHash: 'sha256:user',
        trustStatus: 'untrusted'
      },
      {
        key: 'review-tools:hooks/hooks.json:session_start:0:0',
        eventName: 'SessionStart',
        handlerType: 'command',
        matcher: null,
        command: '${DOTCRAFT_PLUGIN_ROOT}\\hooks\\start.cmd',
        timeoutSec: 30,
        statusMessage: 'Needs trust before it can run',
        sourcePath: '/workspace/dotcraft/.craft/plugins/review-tools/hooks/hooks.json',
        source: 'plugin',
        pluginId: 'review-tools',
        displayOrder: 1,
        enabled: false,
        isManaged: false,
        currentHash: 'sha256:plugin',
        trustStatus: 'untrusted'
      }
    ]

    settingsGet.mockResolvedValue({ locale: 'en', connectionMode: 'local' })
    settingsSet.mockResolvedValue(undefined)
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'channel/list') return { channels: [] }
      if (method === 'hooks/list') return { hooks, warnings: [], errors: [] }
      if (method === 'hooks/setState') {
        hooks = hooks.map((hook) => hook.key === params?.key
          ? {
              ...hook,
              enabled: typeof params.enabled === 'boolean' ? params.enabled : hook.enabled,
              trustStatus: params.trustedHash === hook.currentHash ? 'trusted' : hook.trustStatus
            }
          : hook)
        return { hooks, warnings: [], errors: [] }
      }
      if (method === 'hooks/trustPlugin') {
        hooks = hooks.map((hook) => hook.pluginId === params?.pluginId
          ? {
              ...hook,
              trustStatus: 'trusted'
            }
          : hook)
        return { hooks, warnings: [], errors: [] }
      }
      if (method === 'plugin/list') {
        return {
          plugins: [
            {
              id: 'review-tools',
              displayName: 'Review Tools',
              enabled: true,
              installed: true,
              installable: false,
              removable: true,
              source: 'workspace',
              rootPath: '/workspace/dotcraft/.craft/plugins/review-tools',
              interface: { displayName: 'Review Tools', shortDescription: 'Review helpers' },
              functions: [],
              skills: [],
              hooks: [{ key: 'review-tools:hooks/hooks.json:session_start:0:0', eventName: 'SessionStart' }],
              mcpServers: [],
              lspServers: []
            }
          ],
          diagnostics: []
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
            rootPath: '/workspace/dotcraft/.craft/plugins/review-tools',
            functions: [],
            skills: [],
            hooks: [{ key: 'review-tools:hooks/hooks.json:session_start:0:0', eventName: 'SessionStart' }],
            mcpServers: [],
            lspServers: []
          }
        }
      }
      return {}
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet, set: settingsSet },
        workspaceConfig: {
          getCore: vi.fn().mockResolvedValue({
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
        },
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
        shell: { openExternal: vi.fn(), showItemInFolder: vi.fn() }
      }
    })

    useConnectionStore.getState().reset()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        hooksManagement: true,
        pluginManagement: true,
        workspaceConfigManagement: true
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
    useHooksStore.getState().reset()
    useUIStore.setState({ activeMainView: 'settings', activeSettingsTab: 'general', sidebarCollapsed: false })
  })

  it('shows the Hooks settings tab under Coding and renders hook sources', async () => {
    renderView()

    expect(await screen.findByText('Coding')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Hooks' }))

    expect(await screen.findByText('From Config')).toBeInTheDocument()
    expect(screen.getByText('User config')).toBeInTheDocument()
    expect(screen.queryByText('Workspace config')).not.toBeInTheDocument()
    expect(await screen.findByText('Review Tools')).toBeInTheDocument()

    fireEvent.click(screen.getByText('User config'))

    expect(await screen.findByText('PreToolUse')).toBeInTheDocument()
    expect(screen.getByText('Before a tool executes')).toBeInTheDocument()
  })

  it('hides the config section when no config hooks are present', async () => {
    hooks = hooks.filter((hook) => hook.source === 'plugin')

    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Hooks' }))

    expect(await screen.findByText('Review Tools')).toBeInTheDocument()
    expect(screen.queryByText('From Config')).not.toBeInTheDocument()
    expect(screen.queryByText('User config')).not.toBeInTheDocument()
    expect(screen.queryByText('Workspace config')).not.toBeInTheDocument()
  })

  it('keeps per-hook actions for config hooks', async () => {
    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Hooks' }))
    fireEvent.click(await screen.findByText('User config'))

    const toggle = await screen.findByRole('switch', { name: 'Enable or disable hook' })
    fireEvent.click(toggle)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('hooks/setState', {
        key: '/user-config/.craft/hooks.json:pre_tool_use:0:0',
        enabled: false
      })
    })

    fireEvent.click(screen.getByRole('button', { name: /Hook 1/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Trust' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('hooks/setState', {
        key: '/user-config/.craft/hooks.json:pre_tool_use:0:0',
        trustedHash: 'sha256:user'
      })
    })
  })

  it('trusts plugin hooks as a bundle and keeps plugin hook rows read-only', async () => {
    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Hooks' }))
    fireEvent.click(await screen.findByText('Review Tools'))

    expect(await screen.findByText('Plugin hooks')).toBeInTheDocument()
    expect(screen.getByText('Trust this plugin before its hooks can run.')).toBeInTheDocument()
    expect(screen.queryByRole('switch', { name: 'Enable or disable hook' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Trust hooks' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('hooks/trustPlugin', {
        pluginId: 'review-tools'
      })
    })
    expect(await screen.findByText('All hooks from this plugin are trusted.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Trust hooks' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Hook 1/ }))

    expect(screen.getByText('${DOTCRAFT_PLUGIN_ROOT}\\hooks\\start.cmd')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Trust' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Copy command' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open source file' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'View plugin' })).not.toBeInTheDocument()
    expect(screen.queryByRole('switch', { name: 'Enable or disable hook' })).not.toBeInTheDocument()
  })
})
