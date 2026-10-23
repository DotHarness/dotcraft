import { describe, expect, it, vi } from 'vitest'

const electronMocks = vi.hoisted(() => ({
  app: {
    isPackaged: false,
    setName: vi.fn(),
    setAppUserModelId: vi.fn()
  }
}))

vi.mock('electron', () => electronMocks)

import { resolveWindowsAppUserModelId } from '../appIdentity'

describe('app identity', () => {
  it('uses the product name in development and the package app id for installed builds', () => {
    expect(resolveWindowsAppUserModelId()).toBe('DotCraft')

    electronMocks.app.isPackaged = true
    expect(resolveWindowsAppUserModelId()).toBe('com.dotcraft.desktop')
  })
})
