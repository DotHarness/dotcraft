/**
 * Default cross-channel origins: Desktop shows all discoverable built-in, social,
 * and external origins in thread lists.
 */

import { defaultCrossChannelOriginsFromAvailableChannels, mergeAvailableChannels } from './availableChannels'

export interface ResolveDefaultCrossChannelOriginsOptions {
  includeTeams?: boolean
}

export async function resolveDefaultCrossChannelOrigins(
  options: ResolveDefaultCrossChannelOriginsOptions = {}
): Promise<string[]> {
  try {
    const [channelListRes, modules] = await Promise.all([
      window.api.appServer.sendRequest('channel/list', {}),
      window.api.modules.list().catch(() => [])
    ])
    const r = channelListRes as { channels?: { name: string; category?: string }[] }
    const mergedChannels = mergeAvailableChannels(r.channels ?? [], modules)
    return defaultCrossChannelOriginsFromAvailableChannels(mergedChannels, options)
  } catch {
    return []
  }
}
