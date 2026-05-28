export interface ExternalChannelPersistenceInfo {
  name?: string | null
  enabled?: boolean
  transport?: string | null
  builtinModule?: string | null
}

function hasText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

export function isPersistedEmbeddedModuleChannelEnabled(
  channel: ExternalChannelPersistenceInfo
): boolean {
  const transport = channel.transport?.trim().toLowerCase()
  return (
    channel.enabled === true &&
    (transport === 'subprocess' || transport === 'managedwebsocket') &&
    hasText(channel.builtinModule)
  )
}

export function getEnabledEmbeddedModuleChannelNames(
  channels: ExternalChannelPersistenceInfo[]
): string[] {
  return channels
    .filter(isPersistedEmbeddedModuleChannelEnabled)
    .map((channel) => channel.name?.trim() ?? '')
    .filter(Boolean)
}
