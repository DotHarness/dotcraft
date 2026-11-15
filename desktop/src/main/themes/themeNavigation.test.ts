import { describe, expect, it, vi } from 'vitest'
import { installThemeNavigationGuard } from './themeNavigation'
import { MCP_APP_SANDBOX_PROXY_URL } from '../../shared/mcpAppSandbox'
import type { BrowserWindow } from 'electron'

it('blocks scripted navigation and redirects from themes descendants without affecting other frames', () => {
  const handlers = new Map<string, (event: unknown) => void>()
  const webContents = {
    on: vi.fn((name, handler) => { handlers.set(name, handler) }),
    isDestroyed: () => false,
    removeListener: vi.fn()
  }
  const cleanup = installThemeNavigationGuard({ webContents } as unknown as BrowserWindow)
  const frame = { name: '', parent: { name: 'dotcraft-themes', parent: null } }
  for (const channel of ['will-frame-navigate', 'will-redirect']) {
    const blocked = { frame, url: 'https://example.com/leak', preventDefault: vi.fn() }
    handlers.get(channel)!(blocked)
    expect(blocked.preventDefault).toHaveBeenCalledOnce()
    const allowed = { ...blocked, url: MCP_APP_SANDBOX_PROXY_URL, preventDefault: vi.fn() }
    handlers.get(channel)!(allowed)
    expect(allowed.preventDefault).not.toHaveBeenCalled()
    const ordinary = { frame: { name: 'other', parent: null }, url: blocked.url, preventDefault: vi.fn() }
    handlers.get(channel)!(ordinary)
    expect(ordinary.preventDefault).not.toHaveBeenCalled()
  }
  cleanup()
  expect(webContents.removeListener).toHaveBeenCalledTimes(2)
})
