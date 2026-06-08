import { describe, expect, it, vi } from 'vitest'
import {
  canBridgeRendererInteractiveServerRequest,
  getWorkspaceNotificationForeground,
  isRendererInteractiveServerRequest,
  shouldBridgeWorkspaceServerRequest,
  type WorkspaceConnectionRole
} from '../workspaceConnectionRouting'

describe('workspace connection routing', () => {
  it('routes notifications and server requests by the current foreground owner after promotion', () => {
    const win = { isDestroyed: vi.fn(() => false) }
    const clientA = { id: 'a' }
    const clientB = { id: 'b' }
    let wireClient: typeof clientA | typeof clientB | null = clientA
    let roleA: WorkspaceConnectionRole = 'foreground'
    let roleB: WorkspaceConnectionRole = 'secondary'

    const notificationForeground = (
      method: string,
      client: typeof clientA | typeof clientB,
      role: WorkspaceConnectionRole
    ): boolean | null =>
      getWorkspaceNotificationForeground(method, {
        appQuitting: false,
        mainWindow: win,
        window: win,
        wireClient,
        client,
        role
      })

    const bridgesServerRequest = (
      client: typeof clientA | typeof clientB,
      role: WorkspaceConnectionRole
    ): boolean =>
      shouldBridgeWorkspaceServerRequest({
        appQuitting: false,
        mainWindow: win,
        window: win,
        wireClient,
        client,
        role
      })

    expect(notificationForeground('turn/started', clientA, roleA)).toBe(true)
    expect(bridgesServerRequest(clientA, roleA)).toBe(true)

    roleA = 'secondary'
    roleB = 'foreground'
    wireClient = clientB

    expect(notificationForeground('turn/started', clientA, roleA)).toBeNull()
    expect(notificationForeground('thread/runtimeChanged', clientA, roleA)).toBe(false)
    expect(bridgesServerRequest(clientA, roleA)).toBe(false)
    expect(notificationForeground('turn/started', clientB, roleB)).toBe(true)
    expect(bridgesServerRequest(clientB, roleB)).toBe(true)

    roleA = 'foreground'
    roleB = 'secondary'
    wireClient = clientA

    expect(notificationForeground('turn/started', clientA, roleA)).toBe(true)
    expect(notificationForeground('item/started', clientA, roleA)).toBe(true)
    expect(bridgesServerRequest(clientA, roleA)).toBe(true)
    expect(notificationForeground('thread/runtimeChanged', clientB, roleB)).toBe(false)
    expect(notificationForeground('turn/started', clientB, roleB)).toBeNull()
    expect(bridgesServerRequest(clientB, roleB)).toBe(false)
  })

  it('allows renderer interactive requests while a workspace connection is secondary', () => {
    const win = { isDestroyed: vi.fn(() => false) }
    const clientA = { id: 'a' }
    const clientB = { id: 'b' }
    const baseState = {
      appQuitting: false,
      mainWindow: win,
      wireClient: clientB,
      client: clientA,
      role: 'secondary' as WorkspaceConnectionRole
    }

    expect(shouldBridgeWorkspaceServerRequest(baseState)).toBe(false)
    expect(isRendererInteractiveServerRequest('item/tool/requestUserInput')).toBe(true)
    expect(isRendererInteractiveServerRequest('item/approval/request')).toBe(true)
    expect(isRendererInteractiveServerRequest('item/tool/call')).toBe(false)
    expect(canBridgeRendererInteractiveServerRequest(baseState)).toBe(true)
  })
})
