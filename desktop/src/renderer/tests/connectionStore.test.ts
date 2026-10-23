import { describe, it, expect, beforeEach } from 'vitest'
import { useConnectionStore } from '../stores/connectionStore'

const state = () => useConnectionStore.getState()

describe('connectionStore', () => {
  beforeEach(() => {
    state().reset()
  })

  it('stores connected server details and bumps the epoch for every status event', () => {
    state().setStatus({
      status: 'connected',
      serverInfo: { name: 'dotcraft', version: '0.2.0' },
      capabilities: { threadManagement: true, approvalFlow: true },
      dashboardUrl: 'http://127.0.0.1:8080/dashboard'
    })

    expect(state()).toMatchObject({
      status: 'connected',
      serverInfo: { name: 'dotcraft', version: '0.2.0' },
      capabilities: { threadManagement: true },
      dashboardUrl: 'http://127.0.0.1:8080/dashboard',
      connectionEpoch: 1
    })

    state().setStatus({ status: 'connected' })
    expect(state().connectionEpoch).toBe(2)
    expect(state().dashboardUrl).toBeNull()
  })

  it('clears server details when the connection drops or fails', () => {
    state().setStatus({
      status: 'connected',
      serverInfo: { name: 'dotcraft', version: '0.2.0' },
      capabilities: { approvalFlow: true },
      dashboardUrl: 'http://127.0.0.1:8080/dashboard'
    })

    state().setStatus({ status: 'disconnected', errorMessage: 'Connection lost. Reconnecting...' })
    expect(state()).toMatchObject({
      status: 'disconnected',
      errorMessage: 'Connection lost. Reconnecting...',
      serverInfo: null,
      capabilities: null,
      dashboardUrl: null
    })

    state().setStatus({ status: 'error', errorMessage: 'AppServer is not responding. Restart?', errorType: 'handshake-timeout' })
    expect(state()).toMatchObject({ status: 'error', errorType: 'handshake-timeout' })

    state().setStatus({ status: 'connecting' })
    expect(state()).toMatchObject({ status: 'connecting', errorMessage: null, errorType: null })

    state().reset()
    expect(state()).toMatchObject({ status: 'connecting', serverInfo: null, errorMessage: null, connectionEpoch: 0 })
  })
})
