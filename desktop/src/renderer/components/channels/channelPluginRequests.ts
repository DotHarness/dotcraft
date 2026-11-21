import { useConnectionStore } from '../../stores/connectionStore'
import type { ChannelStatus } from './channelPluginModel'

interface ChannelPluginMethods {
  'channel/status': { params: Record<string, never>; result: { channels: ChannelStatus[] } }
  'channel/setEnabled': { params: { name: string; pluginId: string; enabled: boolean; select?: boolean }; result: Record<string, never> }
  'channel/logs': { params: { name: string; tail?: number }; result: { name: string; lines: string[] } }
  'channel/configOptionAvailability': {
    params: { name: string; key: string }
    result: { name: string; key: string; available?: string[] | null }
  }
  'channel/setup/read': {
    params: { pluginId: string }
    result: { artifact: null | { revision: string; mimeType: 'image/png'; data: string } }
  }
}

export function channelRequest<M extends keyof ChannelPluginMethods>(
  method: M,
  params: ChannelPluginMethods[M]['params']
): Promise<ChannelPluginMethods[M]['result']> {
  const { connectionId } = useConnectionStore.getState()
  const sendRequest = window.api.appServer.sendRequest as (
    method: M, params: ChannelPluginMethods[M]['params'], timeoutMs?: number,
    options?: { connectionId: string }
  ) => Promise<ChannelPluginMethods[M]['result']>
  return connectionId ? sendRequest(method, params, undefined, { connectionId }) : sendRequest(method, params)
}
