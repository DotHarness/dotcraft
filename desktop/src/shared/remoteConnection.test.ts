import { describe, expect, it } from 'vitest'
import { resolveRemoteWebSocketConfig } from './remoteConnection'

describe('remote connection settings', () => {
  it('rejects missing, malformed, and non-WebSocket URLs', () => {
    expect(resolveRemoteWebSocketConfig(undefined)).toMatchObject({ ok: false, code: 'missing-url' })
    expect(resolveRemoteWebSocketConfig({ url: '  ' })).toMatchObject({ ok: false, code: 'missing-url' })
    expect(resolveRemoteWebSocketConfig({ url: 'not a url' })).toMatchObject({ ok: false, code: 'invalid-url' })
    expect(resolveRemoteWebSocketConfig({ url: 'https://example.test/ws' })).toMatchObject({
      ok: false,
      code: 'unsupported-protocol'
    })
  })

  it('accepts ws and wss URLs, adding the external token only when the URL has none', () => {
    expect(resolveRemoteWebSocketConfig({ url: 'wss://dotcraft.example/ws' })).toMatchObject({
      ok: true,
      wsUrl: 'wss://dotcraft.example/ws',
      connectUrl: 'wss://dotcraft.example/ws'
    })
    expect(resolveRemoteWebSocketConfig({ url: 'ws://127.0.0.1:9100/ws', token: 'secret' })).toMatchObject({
      ok: true,
      wsUrl: 'ws://127.0.0.1:9100/ws',
      connectUrl: 'ws://127.0.0.1:9100/ws?token=secret',
      token: 'secret'
    })
    expect(resolveRemoteWebSocketConfig({ url: 'ws://127.0.0.1:9100/ws?token=existing', token: 'secret' })).toMatchObject({
      ok: true,
      wsUrl: 'ws://127.0.0.1:9100/ws?token=existing',
      connectUrl: 'ws://127.0.0.1:9100/ws?token=existing',
      token: 'secret'
    })
  })
})
