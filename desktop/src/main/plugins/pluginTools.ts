import type { DesktopToolConfirmDetails, DesktopToolConfirmOutcome } from '../../shared/desktopToolConfirm'
import type { AppServerRequestClient, DynamicToolCallResult } from '../desktopRuntimeThreadTools'

type PluginActivationStep = 'install' | 'enable' | 'trustHooks' | 'trustDotNet'
type PluginRuntimeCapability = 'desktop' | 'hooks' | 'mcp' | 'dotnet'

export const LIST_PLUGINS_TOOL_NAME = 'ListPlugins'
export const ACTIVATE_PLUGIN_TOOL_NAME = 'ActivatePlugin'

export const LIST_PLUGINS_DESCRIPTION =
  'List the plugins this workspace can use: installed ones, whether they are on, and catalog entries that can be installed, with what each would run and its diagnostics. Changes nothing.'

export const ACTIVATE_PLUGIN_DESCRIPTION =
  'Make a plugin active in this workspace, or reload it after you changed its files. Desktop installs it, turns it on, and trusts its hooks and .NET code as needed, and asks the user to confirm before any of these. A plugin that is already active is reloaded without asking. Its .NET code restarts unchanged, because CSharp.Build applies .NET source changes. Returns the steps applied and the final state.'

export const ACTIVATE_PLUGIN_PROPERTIES = {
  id: { type: 'string', description: 'Plugin id from ListPlugins.' },
  marketplaceName: { type: 'string', description: 'Marketplace of a catalog entry to install. Omit for an installed plugin.' }
}

export interface DesktopPluginToolHost {
  canSeeCard(threadId: string, turnId: string | undefined): Promise<boolean>
  confirm(details: DesktopToolConfirmDetails, threadId: string, turnId: string | undefined): Promise<DesktopToolConfirmOutcome>
}

interface PluginInfo {
  id: string
  displayName?: string
  installed?: boolean
  installable?: boolean
  enabled?: boolean
  marketplaceName?: string | null
  desktop?: unknown
  dotnet?: unknown
  dotnetRuntime?: { state?: string; trustStatus?: string } | null
  hooks?: { key: string }[]
  mcpServers?: unknown[]
  diagnostics?: { severity?: string; code?: string; message?: string }[]
}

interface PluginOperationResult {
  outcome?: 'applied' | 'noChange' | 'notApplied'
  plugin?: PluginInfo | null
  diagnostics?: { code?: string; message?: string }[]
}

export async function listPluginsTool(client: AppServerRequestClient): Promise<DynamicToolCallResult> {
  const result = await client.sendRequest<{ plugins?: PluginInfo[] }>('plugin/list', { includeDisabled: true })
  const plugins = (result.plugins ?? []).map(pluginSummary)
  return ok(JSON.stringify({ plugins }), { plugins })
}

export async function activatePluginTool(
  client: AppServerRequestClient,
  host: DesktopPluginToolHost | undefined,
  args: Record<string, unknown>,
  call: { threadId?: string; turnId?: string }
): Promise<DynamicToolCallResult> {
  const id = typeof args.id === 'string' ? args.id.trim() : ''
  if (!id) return fail('InvalidArguments', 'id is required.')
  const marketplaceName = typeof args.marketplaceName === 'string' && args.marketplaceName.trim()
    ? args.marketplaceName.trim()
    : undefined

  const { plugin } = await client.sendRequest<{ plugin: PluginInfo }>('plugin/view', { id, ...(marketplaceName ? { marketplaceName } : {}) })
  if (!plugin.installed && !plugin.installable) {
    return fail('InvalidArguments', `Plugin '${id}' is neither installed nor installable from a catalog.`)
  }

  const steps = await plannedSteps(client, plugin)
  if (steps.length === 0) return await reload(client, plugin)

  const threadId = call.threadId?.trim()
  if (!host || !threadId || !(await host.canSeeCard(threadId, call.turnId))) {
    return fail('TargetUnsupported', 'Activating a plugin requires a direct request from someone at the Desktop composer, so nothing was changed.')
  }
  const outcome = await host.confirm({ kind: 'pluginActivation', plugin }, threadId, call.turnId)
  if (outcome !== 'confirmed') {
    return fail('PluginActivationCanceled', outcome === 'abandoned'
      ? 'Plugin activation was canceled before the user answered. Do not ask again unless they do.'
      : 'The user declined to activate the plugin. Do not ask again unless they do.')
  }

  const applied: PluginActivationStep[] = []
  for (const step of steps) {
    const result = await applyStep(client, plugin, step, marketplaceName)
    if (result?.outcome === 'notApplied') {
      return await report(client, plugin.id, applied, `Stopped at ${step}: ${diagnosticText(result.diagnostics)}`)
    }
    applied.push(step)
  }
  return await report(client, plugin.id, applied)
}

async function plannedSteps(client: AppServerRequestClient, plugin: PluginInfo): Promise<PluginActivationStep[]> {
  const steps: PluginActivationStep[] = []
  if (!plugin.installed) steps.push('install')
  else if (!plugin.enabled) steps.push('enable')
  if (plugin.hooks?.length && (!plugin.installed || !plugin.enabled || await hasUntrustedHooks(client, plugin))) steps.push('trustHooks')
  if (plugin.dotnet && plugin.dotnetRuntime?.trustStatus !== 'trusted') steps.push('trustDotNet')
  return steps
}

async function hasUntrustedHooks(client: AppServerRequestClient, plugin: PluginInfo): Promise<boolean> {
  const keys = new Set(plugin.hooks?.map((hook) => hook.key))
  const { hooks } = await client.sendRequest<{ hooks?: { key: string; trustStatus?: string }[] }>('hooks/list', {})
  return (hooks ?? []).some((hook) => keys.has(hook.key) && hook.trustStatus !== 'trusted' && hook.trustStatus !== 'managed')
}

async function applyStep(
  client: AppServerRequestClient,
  plugin: PluginInfo,
  step: PluginActivationStep,
  marketplaceName: string | undefined
): Promise<PluginOperationResult | undefined> {
  switch (step) {
    case 'install':
      return await client.sendRequest('plugin/install', { id: plugin.id, ...(marketplaceName ? { marketplaceName } : {}) })
    case 'enable':
      return await client.sendRequest('plugin/setEnabled', { id: plugin.id, enabled: true })
    case 'trustHooks':
      await client.sendRequest('hooks/trustPlugin', { pluginId: plugin.id })
      return undefined
    case 'trustDotNet':
      return await client.sendRequest('plugin/setTrusted', { id: plugin.id, trusted: true })
  }
}

async function reload(client: AppServerRequestClient, plugin: PluginInfo): Promise<DynamicToolCallResult> {
  await client.sendRequest('plugin/setEnabled', { id: plugin.id, enabled: false })
  const result = await client.sendRequest<PluginOperationResult>('plugin/setEnabled', { id: plugin.id, enabled: true })
  return await report(client, plugin.id, [], result.outcome === 'notApplied'
    ? `Reload stopped: ${diagnosticText(result.diagnostics)}`
    : plugin.dotnet
      ? 'Reloaded the plugin. Its .NET code restarted unchanged: CSharp.Build applies .NET source changes, and other changes to .NET files take effect after DotCraft restarts.'
      : 'Reloaded the plugin so its current files take effect.')
}

async function report(
  client: AppServerRequestClient,
  id: string,
  applied: PluginActivationStep[],
  note?: string
): Promise<DynamicToolCallResult> {
  const { plugin } = await client.sendRequest<{ plugin: PluginInfo }>('plugin/view', { id })
  const state = pluginSummary(plugin)
  const lines = [
    applied.length > 0 ? `Applied: ${applied.join(', ')}.` : '',
    note ?? '',
    `State: ${JSON.stringify(state)}`
  ].filter(Boolean)
  return ok(lines.join('\n'), { applied, plugin: state })
}

function pluginSummary(plugin: PluginInfo) {
  return {
    id: plugin.id,
    ...(plugin.marketplaceName ? { marketplaceName: plugin.marketplaceName } : {}),
    name: plugin.displayName || plugin.id,
    installed: plugin.installed === true,
    installable: plugin.installable === true,
    enabled: plugin.enabled === true,
    runs: runtimeCapabilities(plugin),
    ...(plugin.dotnetRuntime ? { dotnet: { state: plugin.dotnetRuntime.state, trustStatus: plugin.dotnetRuntime.trustStatus } } : {}),
    ...(plugin.diagnostics?.length ? { diagnostics: plugin.diagnostics.map((entry) => `${entry.severity ?? 'info'} ${entry.code ?? ''}: ${entry.message ?? ''}`) } : {})
  }
}

function runtimeCapabilities(plugin: PluginInfo): PluginRuntimeCapability[] {
  const runs: PluginRuntimeCapability[] = []
  if (plugin.desktop) runs.push('desktop')
  if (plugin.hooks?.length) runs.push('hooks')
  if (plugin.mcpServers?.length) runs.push('mcp')
  if (plugin.dotnet) runs.push('dotnet')
  return runs
}

function diagnosticText(diagnostics: PluginOperationResult['diagnostics']): string {
  return diagnostics?.map((entry) => `${entry.code ?? 'Error'}: ${entry.message ?? ''}`).join('; ') || 'no diagnostics'
}

function ok(text: string, structuredContent: unknown): DynamicToolCallResult {
  return { success: true, contentItems: [{ type: 'text', text }], structuredContent }
}

function fail(errorCode: string, errorMessage: string): DynamicToolCallResult {
  return { success: false, errorCode, errorMessage }
}
