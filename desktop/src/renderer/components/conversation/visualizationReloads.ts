import { parseVisualizationContentReferences } from './assistantMessageSegments'
import { visualizationKey } from './visualizationParser'

type ReloadListener = (sourceItemId: string) => void

const listeners = new Map<string, Set<ReloadListener>>()

export function subscribeVisualizationReload(threadId: string, path: string, listener: ReloadListener): () => void {
  const key = visualizationKey(threadId, path)
  const set = listeners.get(key) ?? new Set<ReloadListener>()
  set.add(listener)
  listeners.set(key, set)
  return () => {
    set.delete(listener)
    if (set.size === 0 && listeners.get(key) === set) listeners.delete(key)
  }
}

export function reloadVisualizationsReferencedBy(threadId: string, itemId: string, text: string): void {
  const paths = new Set(parseVisualizationContentReferences(text).map(reference => visualizationKey(threadId, reference.path)))
  for (const key of paths) {
    for (const listener of [...(listeners.get(key) ?? [])]) listener(itemId)
  }
}
