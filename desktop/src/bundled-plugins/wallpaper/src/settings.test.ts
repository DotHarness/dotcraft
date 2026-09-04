import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { DesktopPluginSettings, DesktopPluginSettingsSnapshot } from '@dotcraft/plugin'

const initialValue = {
  enabled: true,
  light: { kind: 'preset' as const, id: 'aurora' },
  dark: { kind: 'preset' as const, id: 'aurora' },
  blur: 0,
  dim: 0,
  surfaceOpacity: 30,
  fit: 'cover' as const
}

describe('Wallpaper settings preview', () => {
  beforeEach(() => vi.resetModules())

  it('publishes continuous previews locally and persists only the committed value', async () => {
    let value = initialValue
    const snapshot = (): DesktopPluginSettingsSnapshot<typeof value> => ({
      schema: { fields: [] },
      personal: value,
      workspace: {},
      value,
      writableScopes: ['personal', 'workspace']
    })
    const mutate = vi.fn<DesktopPluginSettings['mutate']>(async (_scope, operations) => {
      for (const operation of operations) {
        if (operation.op === 'set') value = { ...value, [operation.key]: operation.value }
      }
      return snapshot()
    })
    const settings: DesktopPluginSettings = {
      get: async () => snapshot(),
      mutate,
      onChange: () => () => undefined
    }
    const module = await import('./settings')
    const listener = vi.fn()

    await module.initializeSettings(settings)
    module.subscribeSettings(listener)
    module.previewSettings({ blur: 8 })
    module.previewSettings({ blur: 12 })

    expect(mutate).not.toHaveBeenCalled()
    expect(listener).toHaveBeenNthCalledWith(1, expect.objectContaining({ blur: 8 }))
    expect(listener).toHaveBeenNthCalledWith(2, expect.objectContaining({ blur: 12 }))

    module.setSettings({ blur: 12 })
    await vi.waitFor(() => expect(mutate).toHaveBeenCalledOnce())
    expect(mutate).toHaveBeenCalledWith('personal', [{ op: 'set', key: 'blur', value: 12 }])
    expect(module.getSettings()).toEqual(expect.objectContaining({ blur: 12 }))
  })
})
