import type { PluginEntry } from '../stores/pluginStore'
import { getPluginDesktopContent } from './pluginDesktop'

export type PluginContentType =
  | 'app'
  | 'desktopPlugin'
  | 'hooks'
  | 'skill'
  | 'tool'
  | 'mcp'
  | 'lsp'

export interface PluginContentSummary {
  key: string
  type: PluginContentType
  kind: string
  title: string
  description: string
  /** Set for skills, so a caller can open the skill rather than parse the key. */
  skillName?: string
}

type Translate = (key: string, vars?: Record<string, string | number>) => string

const HOOK_EVENT_ORDER = [
  'SessionStart',
  'UserPromptSubmit',
  'PrePrompt',
  'PreToolUse',
  'PermissionRequest',
  'PostToolUse',
  'PostToolUseFailure',
  'PreCompact',
  'PostCompact',
  'SubagentStart',
  'SubagentStop',
  'Stop',
  'StopFailure'
]

export function getPluginContentSummaries(plugin: PluginEntry, t: Translate): PluginContentSummary[] {
  return [
    ...getPluginDesktopContent(plugin, t).map((desktop) => ({
      key: desktop.key,
      type: 'desktopPlugin' as const,
      kind: desktop.kind,
      title: desktop.title,
      description: desktop.description
    })),
    ...(plugin.apps ?? []).map((app) => ({
      key: `app:${app.appId}`,
      type: 'app' as const,
      kind: t('plugins.content.app'),
      title: app.displayName,
      description: app.description
    })),
    ...getPluginHookContent(plugin, t),
    ...plugin.skills.map((skill) => ({
      key: `skill:${skill.name}`,
      type: 'skill' as const,
      kind: t('plugins.content.skill'),
      title: skill.displayName || skill.name,
      description: skill.shortDescription || skill.description,
      skillName: skill.name
    })),
    ...plugin.functions.map((fn) => ({
      key: `function:${fn.name}`,
      type: 'tool' as const,
      kind: t('plugins.content.tool'),
      title: fn.name,
      description: fn.description
    })),
    ...(plugin.mcpServers ?? []).map((server) => ({
      key: `mcp:${server.runtimeName}`,
      type: 'mcp' as const,
      kind: t('plugins.content.mcpServer'),
      title: server.runtimeName,
      description: describePluginMcpServer(server, t)
    })),
    ...(plugin.lspServers ?? []).map((server) => ({
      key: `lsp:${server.runtimeName}`,
      type: 'lsp' as const,
      kind: t('plugins.content.lspServer'),
      title: server.runtimeName,
      description: describePluginLspServer(server, t)
    }))
  ]
}

function getPluginHookContent(plugin: PluginEntry, t: Translate): PluginContentSummary[] {
  const hooks = plugin.hooks ?? []
  if (hooks.length === 0) return []

  const events = [...new Set(hooks.map((hook) => hook.eventName).filter(Boolean))]
    .sort(compareHookEvents)
  return [{
    key: 'hooks',
    type: 'hooks',
    kind: t('plugins.content.hooks'),
    title: t('plugins.content.hooks.title'),
    description: t('plugins.content.hooks.description', {
      count: hooks.length,
      events: events.join(', ')
    })
  }]
}

function compareHookEvents(a: string, b: string): number {
  const aIndex = HOOK_EVENT_ORDER.indexOf(a)
  const bIndex = HOOK_EVENT_ORDER.indexOf(b)
  if (aIndex !== -1 || bIndex !== -1) {
    if (aIndex === -1) return 1
    if (bIndex === -1) return -1
    return aIndex - bIndex
  }
  return a.localeCompare(b)
}

function describePluginMcpServer(
  server: NonNullable<PluginEntry['mcpServers']>[number],
  t: Translate
): string {
  const transport =
    server.transport === 'stdio' ? t('settings.mcp.transport.stdio') : t('settings.mcp.transport.http')
  let state = server.active ? t('plugins.content.mcp.active') : t('plugins.content.mcp.inactive')
  if (!server.enabled) state = t('plugins.content.mcp.disabled')
  if (server.shadowedBy === 'workspace') state = t('plugins.content.mcp.shadowedWorkspace')
  if (server.shadowedBy === 'plugin') state = t('plugins.content.mcp.shadowedPlugin')
  return `${transport} · ${state}`
}

function describePluginLspServer(
  server: NonNullable<PluginEntry['lspServers']>[number],
  t: Translate
): string {
  let state = server.active ? t('plugins.content.lsp.active') : t('plugins.content.lsp.inactive')
  if (!server.enabled) state = t('plugins.content.lsp.disabled')
  if (server.shadowedBy === 'workspace') state = t('plugins.content.lsp.shadowedWorkspace')
  if (server.shadowedBy === 'plugin') state = t('plugins.content.lsp.shadowedPlugin')
  const extensions = server.extensions.length > 0 ? ` · ${server.extensions.join(', ')}` : ''
  return `${server.transport.toUpperCase()} · ${state}${extensions}`
}
