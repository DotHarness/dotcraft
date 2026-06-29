import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useSourceControlStore } from '../stores/sourceControlStore'
import { useConnectionStore } from '../stores/connectionStore'

const sendRequest = vi.fn()

function flush(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0))
}

describe('sourceControlStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as unknown as { window: unknown }).window = {
      api: { appServer: { sendRequest } }
    }
    useSourceControlStore.setState({
      workspacePath: null,
      effectiveProvider: null,
      status: null,
      perforceChangelist: null
    })
    useConnectionStore.getState().reset()
  })

  afterEach(() => {
    delete (globalThis as unknown as { window?: unknown }).window
  })

  it('adopts the new workspace binding when the foreground switches', async () => {
    sendRequest.mockResolvedValueOnce({
      effectiveProvider: 'perforce',
      status: 'connected',
      capabilities: { perforceChangelist: true }
    })
    useSourceControlStore.getState().ensure('A', true)
    await flush()
    expect(useSourceControlStore.getState().workspacePath).toBe('A')
    expect(useSourceControlStore.getState().effectiveProvider).toBe('perforce')

    sendRequest.mockResolvedValueOnce({
      effectiveProvider: 'git',
      status: 'connected',
      capabilities: {}
    })
    useSourceControlStore.getState().ensure('B', true)
    await flush()
    expect(useSourceControlStore.getState().workspacePath).toBe('B')
    expect(useSourceControlStore.getState().effectiveProvider).toBe('git')
    expect(useSourceControlStore.getState().perforceChangelist).toBe(null)
  })

  it('re-fetches the binding when the connection epoch advances on connect', async () => {
    sendRequest.mockResolvedValue({
      effectiveProvider: 'perforce',
      status: 'connected',
      capabilities: { perforceChangelist: true }
    })
    useSourceControlStore.getState().ensure('A', true)
    await flush()
    expect(useSourceControlStore.getState().effectiveProvider).toBe('perforce')
    sendRequest.mockClear()

    // A workspace switch promotes a different connection and re-emits `connected`, bumping the
    // epoch; `sourceControl/get` now describes the new foreground workspace (git).
    sendRequest.mockResolvedValue({ effectiveProvider: 'git', status: 'connected', capabilities: {} })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    await flush()
    expect(sendRequest).toHaveBeenCalledWith('sourceControl/get', {}, 20_000)
    expect(useSourceControlStore.getState().effectiveProvider).toBe('git')
  })

  it('ignores a superseded in-flight refresh so the latest request wins', async () => {
    let resolveStale: (value: unknown) => void = () => {}
    sendRequest.mockReturnValueOnce(new Promise((resolve) => { resolveStale = resolve }))
    useSourceControlStore.getState().ensure('A', true)

    sendRequest.mockResolvedValueOnce({ effectiveProvider: 'git', status: 'connected', capabilities: {} })
    useSourceControlStore.getState().ensure('B', true)
    await flush()
    expect(useSourceControlStore.getState().effectiveProvider).toBe('git')

    // The stale first request resolves last; it must not overwrite the newer result.
    resolveStale({ effectiveProvider: 'perforce', status: 'connected', capabilities: { perforceChangelist: true } })
    await flush()
    expect(useSourceControlStore.getState().workspacePath).toBe('B')
    expect(useSourceControlStore.getState().effectiveProvider).toBe('git')
  })
})
