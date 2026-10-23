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
  readFileSync: vi.fn(),
  promises: {
    readdir: vi.fn()
  }
}))

import { existsSync, readFileSync } from 'fs'
import { resolveBundledChromePluginRoot, resolveChromeExtensionManagementUrl, resolveChromePluginRoot } from '../chromeSetup'

describe('chrome plugin root resolution', () => {
  const mockExistsSync = existsSync as ReturnType<typeof vi.fn>
  const mockReadFileSync = readFileSync as ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.clearAllMocks()
    mockReadFileSync.mockReturnValue('{"extensionId":"pekajfcokkicggfjmickmkngmmoojlda"}')
  })

  it('prefers an installed workspace chrome plugin and falls back to the desktop-bundled root', () => {
    const workspace = path.join('C:', 'workspace')
    const installed = path.join(workspace, '.craft', 'plugins', 'chrome')
    const bundled = resolveBundledChromePluginRoot()

    mockExistsSync.mockImplementation((candidate: string) => candidate === installed || candidate === bundled)
    expect(resolveChromePluginRoot(workspace)).toBe(installed)
    mockExistsSync.mockImplementation((candidate: string) => candidate === bundled)
    expect(resolveChromePluginRoot(workspace)).toBe(bundled)
  })

  it('builds the Chrome extension detail URL from bundled metadata, else the extensions page', () => {
    mockExistsSync.mockReturnValue(false)
    expect(resolveChromeExtensionManagementUrl()).toBe('chrome://extensions/?id=pekajfcokkicggfjmickmkngmmoojlda')

    mockReadFileSync.mockImplementation(() => {
      throw new Error('missing metadata')
    })
    expect(resolveChromeExtensionManagementUrl()).toBe('chrome://extensions')
  })
})
