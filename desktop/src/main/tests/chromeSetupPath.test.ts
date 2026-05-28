import path from 'node:path'
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('electron', () => ({
  app: {
    isPackaged: false,
    getAppPath: () => path.join('C:', 'DotCraft')
  }
}))

vi.mock('fs', () => ({
  existsSync: vi.fn(),
  promises: {
    readdir: vi.fn()
  }
}))

import { existsSync } from 'fs'
import { resolveBundledChromePluginRoot, resolveChromePluginRoot } from '../chromeSetup'

describe('chrome plugin root resolution', () => {
  const mockExistsSync = existsSync as ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('prefers an installed workspace chrome plugin', () => {
    const workspace = path.join('C:', 'workspace')
    const installed = path.join(workspace, '.craft', 'plugins', 'chrome')
    mockExistsSync.mockImplementation((candidate: string) => candidate === installed)

    expect(resolveChromePluginRoot(workspace)).toBe(installed)
  })

  it('falls back to the desktop-bundled chrome plugin root', () => {
    const workspace = path.join('C:', 'workspace')
    const bundled = resolveBundledChromePluginRoot()
    mockExistsSync.mockImplementation((candidate: string) => candidate === bundled)

    expect(resolveChromePluginRoot(workspace)).toBe(bundled)
  })
})
