import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { PluginsView } from '../components/plugins/PluginsView'
import { useConnectionStore } from '../stores/connectionStore'
import { useAppBindingStore, type AppInfo } from '../stores/appBindingStore'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useSkillsStore, type SkillEntry } from '../stores/skillsStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const appServerSendRequest = vi.fn()
const settingsGet = vi.fn()
const shellOpenExternal = vi.fn()
const shellGetProtocolHandlerName = vi.fn()
const workspacePickFolder = vi.fn()
const confirmDialog = vi.fn()

const browserUsePlugin: PluginEntry = {
  id: 'browser',
  displayName: 'Browser',
  description: 'Control the in-app browser with DotCraft',
  version: '1.0.0',
  enabled: false,
  installed: false,
  installable: true,
  removable: false,
  source: 'builtin',
  rootPath: '',
  interface: {
    displayName: 'Browser',
    shortDescription: 'Control the in-app browser with DotCraft',
    developerName: 'Example Labs',
    category: 'Coding'
  },
  functions: [{ name: 'NodeReplJs', namespace: 'node_repl', description: 'Evaluate JavaScript.' }],
  skills: [{ name: 'browser', description: 'Browser', enabled: false }],
  mcpServers: [],
  lspServers: []
}

const workflowPlugin: PluginEntry = {
  id: 'workflow',
  displayName: 'Workflow App',
  description: 'Manage Workflow App boards from selected DotCraft threads.',
  version: '0.1.0',
  enabled: false,
  installed: false,
  installable: true,
  removable: false,
  source: 'builtin',
  rootPath: '',
  interface: {
    displayName: 'Workflow App',
    shortDescription: 'Manage Workflow App boards from selected DotCraft threads',
    developerName: 'Example Labs',
    category: 'Productivity',
    capabilities: ['App', 'Skill']
  },
  functions: [],
  skills: [{ name: 'workflow', description: 'Workflow App', enabled: false }],
  apps: [
    {
      appId: 'com.example.workflow',
      toolNamespace: 'workflow',
      displayName: 'Workflow App',
      developerName: 'Example Labs',
      description: 'Manage Workflow App board items and review rounds from selected DotCraft threads.',
      category: 'Productivity',
      releasePage: 'https://example.com/workflow/releases',
      nativeApplication: {
        displayName: 'Workflow App',
        protocol: 'workflow',
        installUrl: 'https://example.com/workflow/releases'
      },
      toolCatalog: [
        {
          name: 'QueueReviewRound',
          scope: 'board.manage',
          risk: 'mutate',
          defaultExposure: 'deferred',
          description: 'Queue a review round.'
        }
      ]
    }
  ],
  mcpServers: [],
  lspServers: []
}

const agentTeamsPlugin: PluginEntry = {
  id: 'agent-teams',
  displayName: 'Agent Teams',
  description: 'Unlock the DotCraft Team card board with robot teammates, missions, planning, and task dispatch.',
  version: '0.1.0',
  enabled: false,
  installed: false,
  installable: true,
  removable: false,
  source: 'builtin',
  rootPath: '',
  interface: {
    displayName: 'Agent Teams',
    shortDescription: 'Run missions with a small robot team',
    longDescription: 'Agent Teams opens a DotCraft Team card board where robot teammates plan missions, split work into tasks, and keep progress visible as stackable cards.',
    developerName: 'Example Labs',
    category: 'Productivity',
    capabilities: ['Team', 'Missions', 'Card Board']
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
          description: 'Unlocks the card board for Agent Team.'
        }
      ]
    }
  ]
}

function workflowAppInfo({
  nativeStatus = 'missing',
  connectionState = 'notConnected',
  bindingState = null
}: {
  nativeStatus?: string
  connectionState?: string
  bindingState?: string | null
} = {}): AppInfo {
  return {
    appId: 'com.example.workflow',
    pluginId: 'workflow',
    toolNamespace: 'workflow',
    displayName: 'Workflow App',
    developerName: 'Example Labs',
    description: 'Manage Workflow App board items and review rounds from selected DotCraft threads.',
    installed: true,
    enabled: true,
    catalogVisible: true,
    nativeApp: {
      displayName: 'Workflow App',
      protocol: 'workflow',
      status: nativeStatus,
      installUrl: 'https://example.com/workflow/releases'
    },
    connectionState,
    bindingSummary: bindingState == null
      ? null
      : {
          threadId: 'thread-1',
          bindingId: 'binding-1',
          appId: 'com.example.workflow',
          displayName: 'Workflow App',
          state: bindingState,
          connectionState,
          grantedScopes: ['board.read'],
          icon: null,
          toolNamespace: 'workflow'
        },
    handoffModes: [],
    scopes: [],
    toolCatalog: []
  }
}

const localPlugin: PluginEntry = {
  id: 'external-process-echo',
  displayName: 'External Process Echo',
  description: 'Echo text through a plugin-owned local process.',
  version: '0.1.0',
  enabled: true,
  installed: true,
  installable: false,
  removable: true,
  source: 'workspace',
  rootPath: 'F:\\dotcraft\\.craft\\plugins\\external-process-echo',
  interface: {
    displayName: 'External Process Echo',
    shortDescription: 'Run an echo tool in a plugin process',
    developerName: 'Example Labs',
    category: 'Coding',
    websiteUrl: 'https://example.com/external-process-echo',
    privacyPolicyUrl: 'https://example.com/privacy',
    termsOfServiceUrl: 'https://example.com/terms'
  },
  functions: [{ name: 'EchoText', namespace: 'demo', description: 'Echo text.' }],
  skills: [{ name: 'external-process-echo', description: 'Echo plugin skill', enabled: true }],
  mcpServers: [],
  lspServers: []
}

const mcpOnlyPlugin: PluginEntry = {
  id: 'review-tools-mcp',
  displayName: 'Review Tools MCP',
  description: 'Review workflows and MCP tools.',
  version: '0.1.0',
  enabled: true,
  installed: true,
  installable: false,
  removable: true,
  source: 'workspace',
  rootPath: 'F:\\dotcraft\\.craft\\plugins\\review-tools-mcp',
  interface: {
    displayName: 'Review Tools MCP',
    shortDescription: 'Review workflows and MCP tools.',
    developerName: 'Example Labs',
    category: 'Coding',
    defaultPrompt: 'Review this change.'
  },
  functions: [],
  skills: [],
  mcpServers: [
    {
      name: 'review',
      runtimeName: 'review-tools-mcp:review',
      transport: 'stdio',
      enabled: true,
      active: true
    }
  ],
  lspServers: []
}

const lspOnlyPlugin: PluginEntry = {
  id: 'csharp-lsp',
  displayName: 'C# LSP',
  description: 'C# language server.',
  version: '0.1.0',
  enabled: true,
  installed: true,
  installable: false,
  removable: true,
  source: 'workspace',
  rootPath: 'F:\\dotcraft\\.craft\\plugins\\csharp-lsp',
  interface: {
    displayName: 'C# LSP',
    shortDescription: 'C# language server.',
    developerName: 'Example Labs',
    category: 'Coding'
  },
  functions: [],
  skills: [],
  mcpServers: [],
  lspServers: [
    {
      name: 'csharp',
      runtimeName: 'csharp-lsp:csharp',
      transport: 'stdio',
      enabled: true,
      active: false,
      extensions: ['.cs']
    }
  ]
}

const memorySkill: SkillEntry = {
  name: 'memory',
  displayName: 'Memory',
  shortDescription: 'Remember project facts',
  description: 'Remember project facts',
  source: 'builtin',
  available: true,
  enabled: true,
  path: 'F:\\dotcraft\\.craft\\skills\\memory\\SKILL.md'
}

const gitSkill: SkillEntry = {
  name: 'git-local',
  displayName: 'Git Local',
  shortDescription: 'Local git workflows',
  description: 'Local git workflows',
  source: 'workspace',
  available: true,
  enabled: true,
  path: 'F:\\dotcraft\\.craft\\skills\\git-local\\SKILL.md'
}

function renderPluginsView(): void {
  render(
    <LocaleProvider>
      <PluginsView />
    </LocaleProvider>
  )
}

describe('PluginsView local plugin visibility', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    useConnectionStore.getState().reset()
    useAppBindingStore.getState().reset()
    useConversationStore.setState({ remoteWorkspaceActive: false })
    useThreadStore.getState().reset()
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: {
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
    useSkillsStore.setState({
      skills: [],
      loading: false,
      error: null,
      selectedSkillName: null,
      skillContent: null,
      contentLoading: false
    })
    useUIStore.setState({ welcomeDraft: null, activeMainView: 'conversation' })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        shell: { openExternal: shellOpenExternal, getProtocolHandlerName: shellGetProtocolHandlerName },
        workspace: { pickFolder: workspacePickFolder }
      }
    })
    workspacePickFolder.mockResolvedValue(null)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirmDialog
    shellOpenExternal.mockResolvedValue(undefined)
    shellGetProtocolHandlerName.mockResolvedValue('')
    confirmDialog.mockResolvedValue(true)
  })

  it('shows workspace plugins by default under Installed locally', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [browserUsePlugin, localPlugin],
      diagnostics: []
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
          plugin: { ...browserUsePlugin, id: 'disk-plugin', installed: true, enabled: true, removable: true }
        })
      }
      return Promise.resolve({ plugins: [browserUsePlugin], diagnostics: [] })
    })
    workspacePickFolder.mockResolvedValue('/disk/my-plugin')

    renderPluginsView()
    await screen.findByText('Browser')

    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    fireEvent.click(await screen.findByText('Install from disk'))

    await waitFor(() => {
      expect(workspacePickFolder).toHaveBeenCalledWith({ title: 'Select plugin folder' })
      expect(appServerSendRequest).toHaveBeenCalledWith('plugin/installLocal', { path: '/disk/my-plugin' })
    })
  })

  it('does not call plugin/installLocal when the folder picker is cancelled', async () => {
    appServerSendRequest.mockResolvedValue({ plugins: [browserUsePlugin], diagnostics: [] })
    workspacePickFolder.mockResolvedValue(null)

    renderPluginsView()
    await screen.findByText('Browser')

    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    fireEvent.click(await screen.findByText('Install from disk'))

    await waitFor(() => expect(workspacePickFolder).toHaveBeenCalled())
    expect(appServerSendRequest).not.toHaveBeenCalledWith('plugin/installLocal', expect.anything())
  })

  it('hides install from disk for remote workspaces', async () => {
    useConversationStore.setState({ remoteWorkspaceActive: true })
    appServerSendRequest.mockResolvedValue({ plugins: [browserUsePlugin], diagnostics: [] })

    renderPluginsView()
    await screen.findByText('Browser')

    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))

    expect(await screen.findByText('Refresh')).toBeInTheDocument()
    expect(screen.queryByText('Install from disk')).not.toBeInTheDocument()
    expect(workspacePickFolder).not.toHaveBeenCalled()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('plugin/installLocal', expect.anything())
  })

  it('does not render a separate native app catalog section', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [workflowPlugin, browserUsePlugin],
      diagnostics: []
    })

    renderPluginsView()

    expect((await screen.findAllByText('Workflow App')).length).toBeGreaterThan(0)
    expect(screen.queryByText('Native apps')).not.toBeInTheDocument()
  })

  it('hides connected apps on details for uninstalled app plugins', async () => {
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: {
        pluginManagement: true,
        appBinding: true
      }
    })
    useAppBindingStore.setState({
      apps: [workflowAppInfo()],
      appsLoading: false,
      appsError: null
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: workflowPlugin }
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

  it('shows app settings with authorization and offline thread availability on plugin details', async () => {
    const installedWorkflowApp = { ...workflowPlugin, installed: true, enabled: true, installable: false }
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: {
        pluginManagement: true,
        appBinding: true
      }
    })
    useThreadStore.getState().setActiveThreadId('thread-1')
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [installedWorkflowApp], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: installedWorkflowApp }
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
    expect(screen.getByText('Authorized')).toBeInTheDocument()
    expect(screen.getByText('Offline')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open app' })).toBeInTheDocument()
    expect(screen.queryByText('Connected Apps')).not.toBeInTheDocument()
    expect(screen.queryByText('Connected')).not.toBeInTheDocument()
  })

  it('shows the fixed category set including Productivity', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [workflowPlugin, browserUsePlugin],
      diagnostics: []
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
          path: 'F:\\dotcraft\\.craft\\plugins\\broken-plugin\\.craft-plugin\\plugin.json'
        }
      ]
    })

    renderPluginsView()

    expect(await screen.findByText('Plugin diagnostics')).toBeInTheDocument()
    expect(screen.getByText('MissingPluginCapabilities')).toBeInTheDocument()
    expect(screen.getByText('Plugin manifest must declare a skills path or at least one tool.')).toBeInTheDocument()
    expect(usePluginStore.getState().diagnostics).toHaveLength(1)
  })

  it('refreshes plugins when the window regains focus', async () => {
    appServerSendRequest
      .mockResolvedValueOnce({ plugins: [browserUsePlugin], diagnostics: [] })
      .mockResolvedValueOnce({ plugins: [browserUsePlugin, localPlugin], diagnostics: [] })

    renderPluginsView()

    expect(await screen.findByText('Browser')).toBeInTheDocument()
    expect(screen.queryByText('External Process Echo')).not.toBeInTheDocument()

    fireEvent.focus(window)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledTimes(2)
    })
    expect(await screen.findByText('External Process Echo')).toBeInTheDocument()
  })

  it('refreshes plugins from the more actions menu', async () => {
    appServerSendRequest.mockResolvedValue({
      plugins: [browserUsePlugin],
      diagnostics: []
    })

    renderPluginsView()

    expect(await screen.findByText('Browser')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Refresh' })).not.toBeInTheDocument()
    const initialCalls = appServerSendRequest.mock.calls.length

    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Refresh' }))

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.length).toBeGreaterThan(initialCalls)
    })
  })

  it('shows remove for removable local plugins and refreshes after confirmation', async () => {
    let removed = false
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') {
        return { plugins: removed ? [browserUsePlugin] : [browserUsePlugin, localPlugin], diagnostics: [] }
      }
      if (method === 'plugin/view') return { plugin: localPlugin }
      if (method === 'plugin/remove') {
        removed = true
        return {}
      }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('External Process Echo'))
    fireEvent.click(await screen.findByRole('button', { name: 'Remove from DotCraft' }))

    await waitFor(() => {
      expect(confirmDialog).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
      expect(appServerSendRequest).toHaveBeenCalledWith('plugin/remove', { id: 'external-process-echo' })
    })
    expect(screen.queryByRole('button', { name: 'Remove from DotCraft' })).not.toBeInTheDocument()
  })

  it('hides remove for installed plugins that are not removable', async () => {
    const externalRootPlugin = { ...localPlugin, removable: false, source: 'explicit' }
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [externalRootPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: externalRootPlugin }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('External Process Echo'))

    expect(await screen.findByRole('button', { name: 'Try in chat' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove from DotCraft' })).not.toBeInTheDocument()
  })

  it('opens plugin detail links in the external browser', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [localPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: localPlugin }
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
        return { plugins: [browserUsePlugin, localPlugin], diagnostics: [] }
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

  it('shows plugin-bundled MCP content on plugin details', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [mcpOnlyPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: mcpOnlyPlugin }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Review Tools MCP'))

    expect(await screen.findByText('review-tools-mcp:review')).toBeInTheDocument()
    expect(screen.getByText('MCP server')).toBeInTheDocument()
    expect(screen.getByText('STDIO · Active')).toBeInTheDocument()
  })

  it('shows plugin-bundled LSP content on plugin details', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [lspOnlyPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: lspOnlyPlugin }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('C# LSP'))

    expect(await screen.findByText('csharp-lsp:csharp')).toBeInTheDocument()
    expect(screen.getByText('LSP server')).toBeInTheDocument()
    expect(screen.getByText('STDIO · Inactive · .cs')).toBeInTheDocument()
  })

  it('shows Agent Teams as a desktop extension on plugin details', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [agentTeamsPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: agentTeamsPlugin }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Agent Teams'))

    expect(await screen.findByText('Team Board')).toBeInTheDocument()
    expect(screen.getByText('Desktop Extension')).toBeInTheDocument()
    expect(screen.getByText('Unlocks the card board for Agent Team.')).toBeInTheDocument()
  })

  it('shows ordinary plugin install first for app plugins', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: workflowPlugin }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))

    expect(await screen.findByRole('heading', { name: 'Install Workflow App' })).toBeInTheDocument()
    expect((await screen.findAllByText('Workflow App')).length).toBeGreaterThan(0)
    expect(screen.getByText('App')).toBeInTheDocument()
    expect(screen.getByText('workflow')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add to DotCraft' })).toBeInTheDocument()
    expect(screen.queryByText('Install or open Workflow App')).not.toBeInTheDocument()
    expect(screen.queryByText('Connect Workflow App')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Install app' })).not.toBeInTheDocument()
  })

  it('shows only the native app install stage after installing an app plugin with a missing app', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: workflowPlugin }
      if (method === 'plugin/install') return { plugin: { ...workflowPlugin, installed: true, enabled: true, installable: false } }
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
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: workflowPlugin }
      if (method === 'plugin/install') return { plugin: { ...workflowPlugin, installed: true, enabled: true, installable: false } }
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
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: workflowPlugin }
      if (method === 'plugin/install') return { plugin: { ...workflowPlugin, installed: true, enabled: true, installable: false } }
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
    expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument()

    connectionState = 'connected'
    expect(await screen.findByText('Setup complete')).toBeInTheDocument()
  })

  it('shows the completion state when required apps are already connected', async () => {
    shellGetProtocolHandlerName.mockResolvedValue('Workflow App')
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [workflowPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: workflowPlugin }
      if (method === 'plugin/install') return { plugin: { ...workflowPlugin, installed: true, enabled: true, installable: false } }
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
      if (method === 'plugin/list') return { plugins: [browserUsePlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: browserUsePlugin }
      if (method === 'plugin/install') return { plugin: { ...browserUsePlugin, installed: true, enabled: true, installable: false } }
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

  it('shows Agent Teams desktop extension content in the install dialog', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [agentTeamsPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: agentTeamsPlugin }
      if (method === 'plugin/install') return { plugin: { ...agentTeamsPlugin, installed: true, enabled: true, installable: false } }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))

    expect(await screen.findByRole('heading', { name: 'Install Agent Teams' })).toBeInTheDocument()
    expect(screen.getByText('Team Board · Desktop Extension')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add to DotCraft' })).toBeInTheDocument()
  })

  it('enables LSP explicitly from plugin details', async () => {
    let lspEnabled = false
    const activeLspPlugin = {
      ...lspOnlyPlugin,
      lspServers: lspOnlyPlugin.lspServers.map((server) => ({ ...server, active: true }))
    }
    appServerSendRequest.mockImplementation(async (method: string) => {
      const plugin = lspEnabled ? activeLspPlugin : lspOnlyPlugin
      if (method === 'plugin/list') return { plugins: [plugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin }
      if (method === 'workspace/config/update') {
        lspEnabled = true
        return { toolsLspEnabled: true }
      }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('C# LSP'))
    fireEvent.click(await screen.findByRole('button', { name: 'Enable LSP' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', { toolsLspEnabled: true })
    })
    expect(await screen.findByText('STDIO · Active · .cs')).toBeInTheDocument()
  })

  it('does not generate a skill mention for MCP-only plugin try in chat', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [mcpOnlyPlugin], diagnostics: [] }
      if (method === 'plugin/view') return { plugin: mcpOnlyPlugin }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Review Tools MCP'))
    fireEvent.click(await screen.findByRole('button', { name: 'Try in chat' }))

    expect(useUIStore.getState().welcomeDraft?.text).toBe('Review this change.')
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([])
  })
})
