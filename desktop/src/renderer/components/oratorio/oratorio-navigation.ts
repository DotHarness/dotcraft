export type OratorioNavigationTarget = { kind: 'board' } | { kind: 'task'; taskId: string } | { kind: 'settings'; section?: string }

let pendingTarget: OratorioNavigationTarget | null = null

export function requestOratorioNavigation(target: OratorioNavigationTarget): void {
  pendingTarget = target
  window.dispatchEvent(new CustomEvent<OratorioNavigationTarget>('dotcraft:oratorio-navigate', { detail: target }))
}

export function consumeOratorioNavigation(): OratorioNavigationTarget | null {
  const target = pendingTarget
  pendingTarget = null
  return target
}

export function parseOratorioNavigationUrl(value: string): OratorioNavigationTarget | null {
  let url: URL
  try { url = new URL(value) } catch { return null }
  if (url.protocol !== 'oratorio:' || url.hostname !== 'open') return null
  const [kind, id] = url.pathname.replace(/^\//, '').split('/')
  if (kind === 'board') return { kind: 'board' }
  if (kind === 'task' && id) return { kind: 'task', taskId: decodeURIComponent(id) }
  if (kind === 'settings') return { kind: 'settings', section: id ? decodeURIComponent(id) : undefined }
  return null
}
