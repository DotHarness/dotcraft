import { pluginIconUrl } from '../components/plugins/PluginCatalogItem'
import { useAppBindingStore, type ThreadAppBinding } from '../stores/appBindingStore'
import { findPluginEntry, usePluginStore, type PluginEntry } from '../stores/pluginStore'
import type { ConversationItem, ToolSourceProvenance } from '../types/conversation'

const EMPTY_BINDINGS: readonly ThreadAppBinding[] = []
const BINDING_PREFIX = 'binding:'

export function resolveMcpSourceIconUrl(
  sourceId: string | null | undefined,
  pluginId: string | null | undefined,
  plugins: readonly PluginEntry[],
  threadBindings: readonly ThreadAppBinding[] = EMPTY_BINDINGS
): string | null {
  if (pluginId) {
    const direct = findPluginEntry(plugins as PluginEntry[], pluginId)
    return direct ? pluginIconUrl(direct) : null
  }
  const id = sourceId?.trim() ?? ''
  if (!id.startsWith(BINDING_PREFIX)) return null
  const binding = threadBindings.find((entry) => entry.bindingId === id.slice(BINDING_PREFIX.length))
  if (!binding) return null
  const appPlugin = plugins.find((plugin) => plugin.apps?.some((app) => app.appId === binding.appId))
  return (appPlugin ? pluginIconUrl(appPlugin) : null) || binding.icon || null
}

export function resolveToolPluginIconUrl(
  item: Pick<ConversationItem, 'type'> & { source?: ToolSourceProvenance },
  plugins: readonly PluginEntry[],
  threadBindings: readonly ThreadAppBinding[] = EMPTY_BINDINGS
): string | null {
  const source = item.source
  if (!source) return null
  const kind = source.kind.toLowerCase()
  if (item.type !== 'mcpToolCall' && kind !== 'mcp' && kind !== 'pluginnative') return null
  return resolveMcpSourceIconUrl(source.sourceId, source.pluginId, plugins, threadBindings)
}

export function useToolPluginIconUrl(
  item: (Pick<ConversationItem, 'type'> & { source?: ToolSourceProvenance }) | null | undefined,
  threadId?: string | null
): string | null {
  const plugins = usePluginStore((state) => state.plugins)
  const bindings = useAppBindingStore((state) =>
    (threadId ? state.bindingsByThread[threadId] : undefined) ?? EMPTY_BINDINGS)
  return item ? resolveToolPluginIconUrl(item, plugins, bindings) : null
}

export function useMcpSourceIconUrl(
  sourceId: string | null | undefined,
  pluginId?: string | null,
  threadId?: string | null
): string | null {
  const plugins = usePluginStore((state) => state.plugins)
  const bindings = useAppBindingStore((state) =>
    (threadId ? state.bindingsByThread[threadId] : undefined) ?? EMPTY_BINDINGS)
  return resolveMcpSourceIconUrl(sourceId, pluginId, plugins, bindings)
}
