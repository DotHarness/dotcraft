// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useOriginAppStore } from '../stores/originAppStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()

function app(appId: string, originChannel?: string) {
  return { appId, displayName: appId, icon: `${appId}.svg`, originChannel }
}

describe('originAppStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useOriginAppStore.getState().reset()
    installDesktopApiMock({ appServer: { sendRequest } })
  })

  it('maps installed apps by their declared origin channel', async () => {
    sendRequest.mockResolvedValueOnce({ apps: [app('com.example.board', 'Board'), app('com.example.plain')] })

    await useOriginAppStore.getState().refresh()

    expect(sendRequest).toHaveBeenCalledWith('app/list', { includeCatalog: false, includeDisabled: false })
    expect(useOriginAppStore.getState().byChannel).toEqual({
      board: { appId: 'com.example.board', displayName: 'com.example.board', icon: 'com.example.board.svg' }
    })
  })

  it('keeps the newest catalog when an earlier request resolves late', async () => {
    let resolveStale: (value: unknown) => void = () => {}
    sendRequest
      .mockImplementationOnce(() => new Promise((resolve) => { resolveStale = resolve }))
      .mockResolvedValueOnce({ apps: [app('com.example.current', 'board')] })

    const stale = useOriginAppStore.getState().refresh()
    await useOriginAppStore.getState().refresh()
    resolveStale({ apps: [app('com.example.stale', 'board')] })
    await stale

    expect(useOriginAppStore.getState().byChannel.board?.appId).toBe('com.example.current')
  })
})
