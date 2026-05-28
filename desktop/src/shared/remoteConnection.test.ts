import { describe, expect, it } from 'vitest'
import { resolveRemoteWebSocketConfig } from './remoteConnection'

describe('remote connection settings', () => {
  it('requires a remote WebSocket URL', () => {
    expect(resolveRemoteWebSocketConfig(undefined)).toMatchObject({
      ok: false,
      code: 'missing-url'
    })
    expect(resolveRemoteWebSocketConfig({ url: '  ' })).toMatchObject({
      ok: false,
      code: 'missing-url'
    })
  })

  it('rejects malformed URLs', () => {
    expect(resolveRemoteWebSocketConfig({ url: 'not a url' })).toMatchObject({
      ok: false,
      code: 'invalid-url'
    })
  })

  it('rejects non-WebSocket protocols', () => {
    expect(resolveRemoteWebSocketConfig({ url: 'https://example.test/ws' })).toMatchObject({
      ok: false,
      code: 'unsupported-protocol'
    })
  })

  it('accepts ws and wss URLs', () => {
    expect(resolveRemoteWebSocketConfig({ url: 'ws://127.0.0.1:9100/ws' })).toMatchObject({
      ok: true,
      wsUrl: 'ws://127.0.0.1:9100/ws',
      connectUrl: 'ws://127.0.0.1:9100/ws'
    })
    expect(resolveRemoteWebSocketConfig({ url: 'wss://dotcraft.example/ws' })).toMatchObject({
      ok: true,
      wsUrl: 'wss://dotcraft.example/ws',
      connectUrl: 'wss://dotcraft.example/ws'
    })
  })

  it('adds the external token when the URL has no token', () => {
    expect(resolveRemoteWebSocketConfig({
      url: 'ws://127.0.0.1:9100/ws',
      token: 'secret'
    })).toMatchObject({
      ok: true,
      wsUrl: 'ws://127.0.0.1:9100/ws',
      connectUrl: 'ws://127.0.0.1:9100/ws?token=secret',
      token: 'secret'
    })
  })

  it('does not overwrite an existing URL token', () => {
    expect(resolveRemoteWebSocketConfig({
      url: 'ws://127.0.0.1:9100/ws?token=existing',
      token: 'secret'
    })).toMatchObject({
      ok: true,
      wsUrl: 'ws://127.0.0.1:9100/ws?token=existing',
      connectUrl: 'ws://127.0.0.1:9100/ws?token=existing',
      token: 'secret'
    })
  })
})

