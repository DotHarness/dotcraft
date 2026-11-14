import { beforeEach, expect, it, vi } from 'vitest'
import { connectCodexRemote, useCodexRemoteStore } from '../stores/codexRemoteStore'
import type { CodexRemoteEvent, CodexRemotePairing, CodexRemoteStatus } from '../../shared/codexRemote'

const ON: CodexRemoteStatus = { state: 'on', signedIn: true, hostName: 'Test host' }
const PAIRING: CodexRemotePairing = { pairingUrl: 'https://example.test/pair', manualCode: null, expiresAt: '2026-12-01T00:00:00Z' }
const api = { status: vi.fn(), enable: vi.fn(), disable: vi.fn(), createPairing: vi.fn(), onEvent: vi.fn() }

beforeEach(() => {
  vi.resetAllMocks()
  Object.defineProperty(globalThis, 'window', { configurable: true, value: { api: { codexRemote: api } } })
  useCodexRemoteStore.getState().closePairing()
  useCodexRemoteStore.setState({ status: null, loadFailure: null, pendingEnabled: null })
})

it('shares a UI subscription and prevents a late initial snapshot from overwriting a live event', async () => {
  let resolveStatus!: (value: { ok: true; value: CodexRemoteStatus }) => void
  api.status.mockImplementation(() => new Promise((resolve) => { resolveStatus = resolve }))
  let event!: (event: CodexRemoteEvent) => void
  const release = vi.fn()
  api.onEvent.mockImplementation((listener) => { event = listener; return release })
  const first = connectCodexRemote()
  const second = connectCodexRemote()
  expect(api.onEvent).toHaveBeenCalledTimes(1)
  event({ kind: 'stateChanged', status: ON })
  resolveStatus({ ok: true, value: { ...ON, state: 'off' } })
  await Promise.resolve()
  expect(useCodexRemoteStore.getState().status).toEqual(ON)
  first()
  expect(release).not.toHaveBeenCalled()
  second()
  second()
  expect(release).toHaveBeenCalledTimes(1)
})

it('clears the pending switch after invoke failure and exposes Hub read failures instead of hiding the entry', async () => {
  api.status.mockResolvedValue({ ok: false, failure: { code: 'hubUnavailable', message: 'Hub is unavailable.' } })
  await useCodexRemoteStore.getState().load()
  expect(useCodexRemoteStore.getState().loadFailure?.message).toBe('Hub is unavailable.')
  useCodexRemoteStore.setState({ status: ON })
  api.disable.mockRejectedValue(new Error('Connection closed.'))
  const failure = await useCodexRemoteStore.getState().setEnabled(false)
  expect(failure?.code).toBe('hubUnavailable')
  expect(useCodexRemoteStore.getState()).toMatchObject({ pendingEnabled: null, status: ON })
})

it('does not restore an invitation after pairing succeeds or the last view closes while creation is pending', async () => {
  let resolvePairing!: (value: { ok: true; value: CodexRemotePairing }) => void
  api.createPairing.mockImplementation(() => new Promise((resolve) => { resolvePairing = resolve }))
  const paired = useCodexRemoteStore.getState().startPairing()
  useCodexRemoteStore.getState().applyEvent({ kind: 'paired' })
  resolvePairing({ ok: true, value: PAIRING })
  await paired
  expect(useCodexRemoteStore.getState().pairing.step).toBe('paired')

  api.status.mockResolvedValue({ ok: true, value: ON })
  api.onEvent.mockReturnValue(() => {})
  const disconnect = connectCodexRemote()
  const closed = useCodexRemoteStore.getState().startPairing()
  disconnect()
  resolvePairing({ ok: true, value: PAIRING })
  await closed
  expect(useCodexRemoteStore.getState().pairing.step).toBe('idle')
})
