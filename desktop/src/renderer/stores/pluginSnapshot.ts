import type { MarketplaceEntry, PluginDiagnosticEntry, DotNetExtensionRuntimeInfo, PluginEntry } from './pluginStore'

// `noChange` found the requested state already current; `notApplied` left the workspace
// untouched and explains why in `diagnostics`.
export type PluginOperationOutcome = 'applied' | 'noChange' | 'notApplied'

export interface PluginRuntimeProjection {
  id: string
  installed: boolean
  enabled: boolean
  dotnetRuntime: DotNetExtensionRuntimeInfo
}

export interface PluginOperationResult {
  outcome?: PluginOperationOutcome
  plugin?: PluginEntry | null
  affectedPlugins?: PluginRuntimeProjection[]
  diagnostics?: PluginDiagnosticEntry[]
  snapshotRevision: number
}

export interface PluginSnapshotState {
  plugins: PluginEntry[]
  selectedPlugin: PluginEntry | null
  snapshotRevision: number
}

// The Host's revision is monotonic per committed batch, so a lower one is stale and must not
// overwrite a newer record. Notification callers can ignore malformed values at their boundary.
export function validRevision(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null
}

export function requireRevision(value: unknown): number {
  const revision = validRevision(value)
  if (revision == null) throw new Error('The plugin response is missing a valid snapshotRevision.')
  return revision
}

export function operationResultPatch(
  state: PluginSnapshotState,
  result: PluginOperationResult
): Partial<PluginSnapshotState> {
  const snapshotRevision = requireRevision(result.snapshotRevision)
  if (snapshotRevision < state.snapshotRevision) return {}

  let plugins = state.plugins
  let selectedPlugin = state.selectedPlugin
  if (result.plugin) {
    const updated = normalizePlugin(result.plugin)
    plugins = upsertPlugin(plugins, updated)
    if (selectedPlugin?.key === updated.key) selectedPlugin = updated
  }

  for (const affected of result.affectedPlugins ?? []) {
    const fields = runtimeFields(affected)
    const matches = (plugin: PluginEntry): boolean => plugin.installed && plugin.id === affected.id
    plugins = plugins.map((plugin) => (matches(plugin) ? { ...plugin, ...fields } : plugin))
    if (selectedPlugin && matches(selectedPlugin)) selectedPlugin = { ...selectedPlugin, ...fields }
  }

  return { plugins, selectedPlugin, snapshotRevision }
}

export function operationFailureMessage(result: PluginOperationResult): string | null {
  if (result.outcome !== 'notApplied') return null
  const diagnostic = (result.diagnostics ?? []).find((entry) => entry.message)
  return diagnostic?.message ?? null
}

export function normalizeMarketplace(marketplace: MarketplaceEntry): MarketplaceEntry {
  return {
    ...marketplace,
    sparsePaths: marketplace.sparsePaths ?? [],
    pluginIds: marketplace.pluginIds ?? [],
    removable: marketplace.removable !== false
  }
}

export function normalizePlugin(plugin: PluginEntry): PluginEntry {
  return {
    ...plugin,
    functions: plugin.functions ?? [],
    skills: plugin.skills ?? [],
    desktop: plugin.desktop
      ? { ...plugin.desktop, styles: plugin.desktop.styles ?? [] }
      : null,
    hooks: plugin.hooks ?? [],
    dependencies: plugin.dependencies ?? [],
    dotnet: plugin.dotnet
      ? { ...plugin.dotnet, exportedApiAssemblies: plugin.dotnet.exportedApiAssemblies ?? [] }
      : null,
    dotnetRuntime: plugin.dotnetRuntime ? normalizeDotnetRuntime(plugin.dotnetRuntime) : null,
    mcpServers: plugin.mcpServers ?? [],
    lspServers: (plugin.lspServers ?? []).map((server) => ({
      ...server,
      extensions: server.extensions ?? []
    }))
  }
}

export function upsertPlugin(plugins: PluginEntry[], updated: PluginEntry): PluginEntry[] {
  const found = plugins.some((plugin) => plugin.key === updated.key)
  if (!found) return [...plugins, updated]
  return plugins.map((plugin) => (plugin.key === updated.key ? updated : plugin))
}

export function findPluginEntry(plugins: PluginEntry[], keyOrId: string): PluginEntry | undefined {
  return plugins.find((plugin) => plugin.key === keyOrId)
    ?? plugins.find((plugin) => plugin.installed && plugin.id === keyOrId)
    ?? plugins.find((plugin) => plugin.id === keyOrId)
}

// Plugin ids never contain '@', so the first one separates the marketplace.
export function pluginKeyParams(key: string): { id: string; marketplaceName?: string } {
  const separator = key.indexOf('@')
  return separator < 0
    ? { id: key }
    : { id: key.slice(0, separator), marketplaceName: key.slice(separator + 1) }
}

export function hasOtherEntries(plugins: PluginEntry[], updated: PluginEntry): boolean {
  return plugins.some((plugin) => plugin.id === updated.id && plugin.key !== updated.key)
}

// A projection carries only these fields; the rest of the record in state is already normalized.
function runtimeFields(
  affected: PluginRuntimeProjection
): Pick<PluginEntry, 'installed' | 'enabled' | 'dotnetRuntime'> {
  return {
    installed: affected.installed,
    enabled: affected.enabled,
    dotnetRuntime: normalizeDotnetRuntime(affected.dotnetRuntime)
  }
}

function normalizeDotnetRuntime(runtime: DotNetExtensionRuntimeInfo): DotNetExtensionRuntimeInfo {
  return {
    ...runtime,
    blockers: runtime.blockers ?? [],
    leakedGenerations: runtime.leakedGenerations ?? 0,
    restartRecommended: runtime.restartRecommended === true,
    trustStatus: runtime.trustStatus ?? 'untrusted'
  }
}
