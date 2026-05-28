export type ChannelId = 'wecom'

export interface ChannelDefinition {
  id: ChannelId
  nameKey: string
  logoPath?: string
  channelListName: string
}

export const CHANNEL_DEFS: ChannelDefinition[] = [
  {
    id: 'wecom',
    nameKey: 'channels.channel.wecom',
    logoPath: new URL('../../assets/channels/wecom.svg', import.meta.url).toString(),
    channelListName: 'wecom'
  }
]
