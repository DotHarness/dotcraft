import { render } from '@testing-library/react'
import { vi } from 'vitest'
import { PluginsView } from '../components/plugins/PluginsView'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useAppBindingStore, type AppInfo } from '../stores/appBindingStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useSkillsStore, type SkillEntry } from '../stores/skillsStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

export const appServerSendRequest = vi.fn()
const settingsGet = vi.fn()
export const shellOpenExternal = vi.fn()
export const shellGetProtocolHandlerName = vi.fn()
export const workspacePickFolder = vi.fn()
export const confirmDialog = vi.fn()

export const browserUsePlugin: PluginEntry = {
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

export const dotnetPlugin: PluginEntry = {
  ...browserUsePlugin,
  id: 'acme.review-core',
  displayName: 'Review Core',
  description: 'Runs native review tools.',
  version: '1.0.0',
  interface: {
    ...browserUsePlugin.interface,
    displayName: 'Review Core',
    shortDescription: 'Runs native review tools.',
    developerName: 'Acme Review'
  },
  functions: [],
  skills: [],
  dotnet: {
    entryAssembly: './dotnet/Acme.Review.dll',
    entryType: 'Acme.Review.Plugin',
    exportedApiAssemblies: [],
    minHostVersion: '0.5.0'
  },
  dependencies: [],
  dotnetRuntime: {
    state: 'blocked',
    generationId: null,
    blockers: [{ code: 'PluginUntrusted', message: 'Plugin has no trust grant.' }],
    leakedGenerations: 0,
    restartRecommended: false,
    trustStatus: 'untrusted'
  }
}

export const installedDotNetPlugin: PluginEntry = {
  ...dotnetPlugin,
  installed: true,
  installable: false,
  removable: true,
  source: 'workspace',
  enabled: false
}

export const workflowPlugin: PluginEntry = {
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
      displayName: 'Workflow App',
      developerName: 'Example Labs',
      description: 'Manage Workflow App board items and review rounds from selected DotCraft threads.',
      category: 'Productivity',
      releasePage: 'https://example.com/workflow/releases',
      nativeApplication: {
        displayName: 'Workflow App',
        protocol: 'workflow',
        installUrl: 'https://example.com/workflow/releases'
      }
    }
  ],
  mcpServers: [],
  lspServers: []
}

export const agentTeamsPlugin: PluginEntry = {
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

export function workflowAppInfo({
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
          icon: null,
          authorityRevision: 2,
          approvedCapabilityRevision: 1,
          approvedTools: [{ namespace: 'workflow', name: 'ReadBoard' }]
        },
    handoffModes: []
  }
}

export const localPlugin: PluginEntry = {
  id: 'external-process-echo',
  displayName: 'External Process Echo',
  description: 'Echo text through a plugin-owned local process.',
  version: '0.1.0',
  enabled: true,
  installed: true,
  installable: false,
  removable: true,
  source: 'workspace',
  rootPath: 'X:\\fixtures\\workspace\\.craft\\plugins\\external-process-echo',
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

export const mcpOnlyPlugin: PluginEntry = {
  id: 'review-tools-mcp',
  displayName: 'Review Tools MCP',
  description: 'Review workflows and MCP tools.',
  version: '0.1.0',
  enabled: true,
  installed: true,
  installable: false,
  removable: true,
  source: 'workspace',
  rootPath: 'X:\\fixtures\\workspace\\.craft\\plugins\\review-tools-mcp',
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

export const lspOnlyPlugin: PluginEntry = {
  id: 'csharp-lsp',
  displayName: 'C# LSP',
  description: 'C# language server.',
  version: '0.1.0',
  enabled: true,
  installed: true,
  installable: false,
  removable: true,
  source: 'workspace',
  rootPath: 'X:\\fixtures\\workspace\\.craft\\plugins\\csharp-lsp',
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

export const memorySkill: SkillEntry = {
  name: 'memory',
  displayName: 'Memory',
  shortDescription: 'Remember project facts',
  description: 'Remember project facts',
  source: 'builtin',
  available: true,
  enabled: true,
  path: 'X:\\fixtures\\workspace\\.craft\\skills\\memory\\SKILL.md'
}

export const gitSkill: SkillEntry = {
  name: 'git-local',
  displayName: 'Git Local',
  shortDescription: 'Local git workflows',
  description: 'Local git workflows',
  source: 'workspace',
  available: true,
  enabled: true,
  path: 'X:\\fixtures\\workspace\\.craft\\skills\\git-local\\SKILL.md'
}

export function renderPluginsView(): void {
  render(
    <LocaleProvider>
      <PluginsView />
    </LocaleProvider>
  )
}

export function setupPluginsViewTest(): void {
  vi.clearAllMocks()
  settingsGet.mockResolvedValue({ locale: 'en' })
  useConnectionStore.getState().reset()
  useAppBindingStore.getState().reset()
  useConversationStore.setState({ remoteWorkspaceActive: false })
  useThreadStore.getState().reset()
  useToastStore.setState({ toasts: [] })
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
    detailLoading: false,
    snapshotRevision: 0,
    completeSnapshotRevision: 0
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
  installDesktopApiMock({
    settings: { get: settingsGet },
    appServer: { sendRequest: appServerSendRequest },
    shell: { openExternal: shellOpenExternal, getProtocolHandlerName: shellGetProtocolHandlerName },
    workspace: { pickFolder: workspacePickFolder }
  })
  workspacePickFolder.mockResolvedValue(null)
  ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirmDialog
  shellOpenExternal.mockResolvedValue(undefined)
  shellGetProtocolHandlerName.mockResolvedValue('')
  confirmDialog.mockResolvedValue(true)
}
