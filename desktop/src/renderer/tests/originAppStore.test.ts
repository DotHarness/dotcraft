// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useOriginAppStore } from '../stores/originAppStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendConnectionRequest = vi.fn()
const foreground = { connectionKey: 'workspace-a', connectionId: 'a-1' }
const secondary = { connectionKey: 'workspace-b', connectionId: 'b-1' }

function app(appId: string, originChannel?: string) {
  return { appId, displayName: appId, icon: `${appId}.svg`, originChannel }
}

async function settle(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0))
}

describe('originAppStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useOriginAppStore.getState().reset()
    installDesktopApiMock({ appServer: { sendConnectionRequest } })
  })

  it('maps each connection\'s installed apps by their declared origin channel', async () => {
    sendConnectionRequest.mockImplementation(async (key: string) => ({
      apps: key === foreground.connectionKey
        ? [app('com.example.board', 'Board'), app('com.example.plain')]
        : [app('com.example.other-board', 'board')]
    }))

    useOriginAppStore.getState().ensure(foreground)
    useOriginAppStore.getState().ensure(secondary)
    await settle()

    expect(sendConnectionRequest).toHaveBeenCalledWith(
      foreground.connectionKey, 'app/list', { includeCatalog: false, includeDisabled: false }, undefined,
      { connectionId: foreground.connectionId })
    const { byScope } = useOriginAppStore.getState()
    expect(byScope[`${foreground.connectionKey}\u0000a-1`]).toEqual({
      board: { appId: 'com.example.board', displayName: 'com.example.board', icon: 'com.example.board.svg' }
    })
    expect(byScope[`${secondary.connectionKey}\u0000b-1`]?.board?.appId).toBe('com.example.other-board')
  })

  it('retries a failed load once the connection is replaced', async () => {
    sendConnectionRequest
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce({ apps: [app('com.example.board', 'board')] })

    useOriginAppStore.getState().ensure(foreground)
    await settle()
    useOriginAppStore.getState().ensure({ ...foreground, connectionId: 'a-2' })
    await settle()

    expect(Object.keys(useOriginAppStore.getState().byScope)).toEqual([`${foreground.connectionKey}\u0000a-2`])
  })

  it('keeps the newest catalog when an earlier request resolves late', async () => {
    let resolveStale: (value: unknown) => void = () => {}
    sendConnectionRequest
      .mockResolvedValueOnce({ apps: [] })
      .mockImplementationOnce(() => new Promise((resolve) => { resolveStale = resolve }))
      .mockResolvedValueOnce({ apps: [app('com.example.current', 'board')] })
    useOriginAppStore.getState().ensure(foreground)
    await settle()

    const stale = useOriginAppStore.getState().refresh(foreground.connectionKey)
    await useOriginAppStore.getState().refresh(foreground.connectionKey)
    resolveStale({ apps: [app('com.example.stale', 'board')] })
    await stale

    expect(useOriginAppStore.getState().byScope[`${foreground.connectionKey}\u0000a-1`]?.board?.appId)
      .toBe('com.example.current')
  })
})
