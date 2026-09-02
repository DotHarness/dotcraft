export type OratorioNavigationTarget = { kind: 'board' } | { kind: 'task'; taskId: string } | { kind: 'settings'; section?: string; provider?: 'github' | 'gitlab' }

let pendingTarget: OratorioNavigationTarget | null = null
const listeners = new Set<(target: OratorioNavigationTarget) => void>()

export function requestOratorioNavigation(target: OratorioNavigationTarget): void {
  pendingTarget = target
  for (const listener of listeners) listener(target)
}
export function consumeOratorioNavigation(): OratorioNavigationTarget | null {
  const target = pendingTarget
  pendingTarget = null
  return target
}

export function onOratorioNavigation(listener: (target: OratorioNavigationTarget) => void): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

export function parseOratorioNavigationUrl(value: string): OratorioNavigationTarget | null {
  let url: URL
  try { url = new URL(value) } catch { return null }
  if (url.protocol !== 'oratorio:' || url.hostname !== 'open') return null
  const [kind, id, detail] = url.pathname.replace(/^\//, '').split('/')
  if (kind === 'board') return { kind: 'board' }
  if (kind === 'task' && id) return { kind: 'task', taskId: decodeURIComponent(id) }
  if (kind === 'settings') {
    const provider = detail === 'github' || detail === 'gitlab' ? detail : undefined
    return { kind: 'settings', section: id ? decodeURIComponent(id) : undefined, ...(provider ? { provider } : {}) }
  }
  return null
}
