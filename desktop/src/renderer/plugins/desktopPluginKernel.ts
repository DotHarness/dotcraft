import type { DesktopPluginDispose } from '@dotcraft/plugin'

interface ServiceEntry {
  token: number
  value: unknown
}

interface EventListenerEntry {
  token: number
  listener: (payload: unknown) => void
}

const services = new Map<string, ServiceEntry[]>()
const eventListeners = new Map<string, EventListenerEntry[]>()
let nextToken = 1

export function provideDesktopPluginService<T>(id: string, value: T): DesktopPluginDispose {
  const entry = { token: nextToken++, value }
  const entries = services.get(id) ?? []
  entries.push(entry)
  services.set(id, entries)

  return () => removeEntry(services, id, entry.token)
}

export function useDesktopPluginService<T>(id: string): T | undefined {
  const entries = services.get(id)
  return entries?.[entries.length - 1]?.value as T | undefined
}

export function onDesktopPluginEvent<T>(
  event: string,
  listener: (payload: T) => void
): DesktopPluginDispose {
  const entry: EventListenerEntry = {
    token: nextToken++,
    listener: listener as (payload: unknown) => void
  }
  const entries = eventListeners.get(event) ?? []
  entries.push(entry)
  eventListeners.set(event, entries)

  return () => removeEntry(eventListeners, event, entry.token)
}

export function emitDesktopPluginEvent<T>(event: string, payload: T): void {
  for (const entry of [...(eventListeners.get(event) ?? [])]) {
    entry.listener(payload)
  }
}

export function clearDesktopPluginKernel(): void {
  services.clear()
  eventListeners.clear()
  nextToken = 1
}

function removeEntry<T extends { token: number }>(
  registry: Map<string, T[]>,
  id: string,
  token: number
): void {
  const entries = registry.get(id)
  if (!entries) return
  const next = entries.filter((entry) => entry.token !== token)
  if (next.length > 0) registry.set(id, next)
  else registry.delete(id)
}
