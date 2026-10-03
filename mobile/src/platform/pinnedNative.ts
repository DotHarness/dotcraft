import PinnedTransport from '../../modules/pinned-transport'
import type { PinnedNative } from '../core/pinned'

export const pinnedNative: PinnedNative = {
  request: (request) => PinnedTransport.request(request),
  openSocket: (options) => PinnedTransport.openSocket(options),
  send: (id, text) => PinnedTransport.send(id, text),
  close: (id, code, reason) => PinnedTransport.close(id, code, reason),
  subscribe(listener) {
    const subscriptions = [
      PinnedTransport.addListener('open', (event) => listener({ type: 'open', ...event })),
      PinnedTransport.addListener('message', (event) => listener({ type: 'message', ...event })),
      PinnedTransport.addListener('close', (event) => listener({ type: 'close', ...event })),
      PinnedTransport.addListener('error', (event) => listener({ type: 'error', ...event })),
    ]
    return () => subscriptions.forEach((subscription) => subscription.remove())
  },
}
