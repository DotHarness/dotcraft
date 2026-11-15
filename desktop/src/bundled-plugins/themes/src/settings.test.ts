import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { DesktopPluginSettings } from '@dotcraft/plugin'

const initialValue = { enabled: true, animate: true }

describe('Themes settings', () => {
  beforeEach(() => vi.resetModules())

  it('restores the saved settings when persistence fails', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const module = await import('./settings')
    const snapshot = { value: initialValue, schema: { fields: [] }, personal: initialValue, workspace: {}, writableScopes: ['personal'] }
    await module.initializeSettings({ get: async () => snapshot, mutate: async () => { throw new Error('Disk unavailable') }, onChange: () => () => {} } as unknown as DesktopPluginSettings)
    module.setSettings({ animate: false })
    expect(module.getSettings()?.animate).toBe(false)
    await vi.waitFor(() => expect(module.getSettings()?.animate).toBe(true))
    expect(console.error).toHaveBeenCalledOnce()
  })
})
