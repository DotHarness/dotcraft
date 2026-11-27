import { pluginIconUrl } from '../components/plugins/PluginCatalogItem'
import { findPluginEntry, usePluginStore, type PluginEntry } from '../stores/pluginStore'
import type { ConversationItem, ToolSourceProvenance } from '../types/conversation'

export function resolveMcpSourceIconUrl(
  pluginId: string | null | undefined,
  plugins: readonly PluginEntry[]
): string | null {
  if (!pluginId) return null
  const direct = findPluginEntry(plugins as PluginEntry[], pluginId)
  return direct ? pluginIconUrl(direct) : null
}

export function resolveToolPluginIconUrl(
  item: Pick<ConversationItem, 'type'> & { source?: ToolSourceProvenance },
  plugins: readonly PluginEntry[]
): string | null {
  const source = item.source
  if (!source) return null
  const kind = source.kind.toLowerCase()
  if (item.type !== 'mcpToolCall' && kind !== 'mcp' && kind !== 'pluginnative') return null
  return resolveMcpSourceIconUrl(source.pluginId, plugins)
}

export function useToolPluginIconUrl(
  item: (Pick<ConversationItem, 'type'> & { source?: ToolSourceProvenance }) | null | undefined
): string | null {
  const plugins = usePluginStore((state) => state.plugins)
  return item ? resolveToolPluginIconUrl(item, plugins) : null
}

export function useMcpSourceIconUrl(pluginId?: string | null): string | null {
  const plugins = usePluginStore((state) => state.plugins)
  return resolveMcpSourceIconUrl(pluginId, plugins)
}
