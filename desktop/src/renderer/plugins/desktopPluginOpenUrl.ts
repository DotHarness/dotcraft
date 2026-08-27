import { useDesktopPluginRegistry } from './desktopPluginRegistry'

interface DesktopPluginOpenUrlListener {
  pluginId: string
  revision: string
  ordinal: number
  listener: (url: string) => boolean
}

const listeners = new Set<DesktopPluginOpenUrlListener>()
let nextOrdinal = 0

export function registerDesktopPluginOpenUrlListener(
  pluginId: string,
  revision: string,
  listener: (url: string) => boolean
): () => void {
  const entry = { pluginId, revision, ordinal: nextOrdinal++, listener }
  listeners.add(entry)
  return () => { listeners.delete(entry) }
}

export function openDesktopPluginUrl(url: string): boolean {
  const generations = useDesktopPluginRegistry.getState().generations
  const active = [...listeners]
    .filter((entry) => generations.get(entry.pluginId)?.revision === entry.revision)
    .sort((left, right) => ordinalCompare(left.pluginId, right.pluginId) || left.ordinal - right.ordinal)
  for (const entry of active) {
    if (entry.listener(url)) return true
  }
  return false
}

function ordinalCompare(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0
}
