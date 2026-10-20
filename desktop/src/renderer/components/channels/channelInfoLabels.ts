const CAPABILITY_LABEL_KEYS: Record<string, string> = {
  hasChannelTools: 'channels.detail.capability.channelTools',
  requiresInteractiveSetup: 'channels.detail.capability.interactiveSetup'
}

export function transportLabel(transport: string): string {
  if (transport === 'websocket') return 'WebSocket'
  if (transport === 'managedWebsocket') return 'Managed WebSocket'
  if (transport === 'subprocess') return 'Subprocess'
  return transport
}

export function capabilityLabels(
  summary: Record<string, unknown> | undefined,
  t: (key: string) => string
): string[] {
  return Object.entries(summary ?? {})
    .filter(([key, value]) => value === true && CAPABILITY_LABEL_KEYS[key])
    .map(([key]) => t(CAPABILITY_LABEL_KEYS[key]))
}
