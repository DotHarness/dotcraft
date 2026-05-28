import { describe, it, expect, beforeEach } from 'vitest'
import { useConnectionStore } from '../stores/connectionStore'

/**
 * Tests for the connection state machine.
 * Spec §5.3: Connection Status Indicator states
 */
describe('connectionStore', () => {
  beforeEach(() => {
    // Reset store to initial state before each test
    useConnectionStore.getState().reset()
  })

  it('starts in "connecting" state', () => {
    const state = useConnectionStore.getState()
    expect(state.status).toBe('connecting')
    expect(state.serverInfo).toBeNull()
    expect(state.capabilities).toBeNull()
    expect(state.errorMessage).toBeNull()
    expect(state.dashboardUrl).toBeNull()
  })

  it('transitions to "connected" with serverInfo and capabilities', () => {
    useConnectionStore.getState().setStatus({
      status: 'connected',
      serverInfo: { name: 'dotcraft', version: '0.2.0' },
      capabilities: { threadManagement: true, approvalFlow: true }
    })

    const state = useConnectionStore.getState()
    expect(state.status).toBe('connected')
    expect(state.serverInfo?.name).toBe('dotcraft')
    expect(state.serverInfo?.version).toBe('0.2.0')
    expect(state.capabilities?.threadManagement).toBe(true)
    expect(state.dashboardUrl).toBeNull()
  })

  it('stores dashboardUrl when connected and payload includes it', () => {
    useConnectionStore.getState().setStatus({
      status: 'connected',
      serverInfo: { name: 'dotcraft', version: '0.2.0' },
      capabilities: {},
      dashboardUrl: 'http://127.0.0.1:8080/dashboard'
    })
    expect(useConnectionStore.getState().dashboardUrl).toBe('http://127.0.0.1:8080/dashboard')
  })

  it('transitions to "disconnected" with reconnect message', () => {
    // First connect
    useConnectionStore.getState().setStatus({
      status: 'connected',
      serverInfo: { name: 'dotcraft', version: '0.2.0' }
    })

    // Then crash
    useConnectionStore.getState().setStatus({
      status: 'disconnected',
      errorMessage: 'Connection lost. Reconnecting...'
    })

    const state = useConnectionStore.getState()
    expect(state.status).toBe('disconnected')
    expect(state.errorMessage).toBe('Connection lost. Reconnecting...')
    expect(state.serverInfo).toBeNull()
    expect(state.dashboardUrl).toBeNull()
  })

  it('transitions to "error" with binary-not-found errorType', () => {
    useConnectionStore.getState().setStatus({
      status: 'error',
      errorMessage: 'DotCraft AppServer binary not found.',
      errorType: 'binary-not-found'
    })

    const state = useConnectionStore.getState()
    expect(state.status).toBe('error')
    expect(state.errorType).toBe('binary-not-found')
    expect(state.errorMessage).toBeTruthy()
  })

  it('transitions to "error" with handshake-timeout errorType', () => {
    useConnectionStore.getState().setStatus({
      status: 'error',
      errorMessage: 'AppServer is not responding. Restart?',
      errorType: 'handshake-timeout'
    })

    const state = useConnectionStore.getState()
    expect(state.status).toBe('error')
    expect(state.errorType).toBe('handshake-timeout')
  })

  it('clears serverInfo and capabilities on non-connected status', () => {
    useConnectionStore.getState().setStatus({
      status: 'connected',
      serverInfo: { name: 'dotcraft', version: '0.2.0' },
      capabilities: { approvalFlow: true }
    })

    useConnectionStore.getState().setStatus({ status: 'disconnected' })

    const state = useConnectionStore.getState()
    expect(state.serverInfo).toBeNull()
    expect(state.capabilities).toBeNull()
    expect(state.dashboardUrl).toBeNull()
  })

  it('resets to initial state', () => {
    useConnectionStore.getState().setStatus({
      status: 'connected',
      serverInfo: { name: 'dotcraft', version: '0.2.0' }
    })

    useConnectionStore.getState().reset()

    const state = useConnectionStore.getState()
    expect(state.status).toBe('connecting')
    expect(state.serverInfo).toBeNull()
    expect(state.errorMessage).toBeNull()
  })

  it('full lifecycle: connecting -> connected -> disconnected -> error', () => {
    const store = useConnectionStore.getState()

    store.setStatus({ status: 'connecting' })
    expect(useConnectionStore.getState().status).toBe('connecting')

    store.setStatus({
      status: 'connected',
      serverInfo: { name: 'dotcraft', version: '0.2.0' }
    })
    expect(useConnectionStore.getState().status).toBe('connected')

    store.setStatus({ status: 'disconnected' })
    expect(useConnectionStore.getState().status).toBe('disconnected')

    store.setStatus({ status: 'error', errorMessage: 'Fatal', errorType: 'binary-not-found' })
    expect(useConnectionStore.getState().status).toBe('error')
    expect(useConnectionStore.getState().errorType).toBe('binary-not-found')
  })
})
