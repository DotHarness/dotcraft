import type { DiscoveredModule } from '../../preload/api'

export interface ChannelInfoLike {
  name: string
  category?: string
}

export interface DefaultCrossChannelOriginOptions {
  includeTeams?: boolean
}

const SOCIAL_MODULE_CHANNELS = new Set(['weixin', 'wechat', 'feishu', 'telegram', 'qq', 'wecom'])

function normalizeName(name: string): string {
  return name.trim().toLowerCase()
}

function moduleChannelCategory(channelName: string): string | null {
  return SOCIAL_MODULE_CHANNELS.has(normalizeName(channelName)) ? 'social' : null
}

export function mergeAvailableChannels(
  serverChannels: ChannelInfoLike[],
  modules: Pick<DiscoveredModule, 'channelName'>[]
): ChannelInfoLike[] {
  const merged = new Map<string, ChannelInfoLike>()

  for (const channel of serverChannels) {
    const key = normalizeName(channel.name)
    if (!key) continue
    merged.set(key, {
      name: channel.name,
      category: channel.category || 'builtin'
    })
  }

  for (const module of modules) {
    const key = normalizeName(module.channelName)
    if (!key || merged.has(key)) continue
    const category = moduleChannelCategory(module.channelName)
    if (!category) continue
    merged.set(key, {
      name: module.channelName,
      category
    })
  }

  return [...merged.values()]
}

export function defaultCrossChannelOriginsFromAvailableChannels(
  channels: ChannelInfoLike[],
  options: DefaultCrossChannelOriginOptions = {}
): string[] {
  const seeded = new Set<string>()
  for (const channel of channels) {
    const category = channel.category || 'builtin'
    const name = channel.name.trim()
    if (!name) continue
    const isDefaultVisibleCategory =
      category === 'builtin' || category === 'social' || category === 'external'
    const isTeamsOrigin = options.includeTeams === true && name.toLowerCase() === 'teams'
    if (!isDefaultVisibleCategory && !isTeamsOrigin) continue
    seeded.add(name)
  }
  return [...seeded]
}
