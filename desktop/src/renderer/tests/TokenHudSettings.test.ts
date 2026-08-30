import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { DesktopPluginSettings, DesktopPluginSettingsSnapshot } from '@dotcraft/plugin'

const initialValue = { visible: true, opacity: 80 }

describe('Token HUD settings preview', () => {
  beforeEach(() => vi.resetModules())

  it('previews opacity locally and persists only the committed value', async () => {
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
    const module = await import('../../../../sdk/typescript/samples/desktop-plugins/token-hud/desktop/src/settings')
    const listener = vi.fn()

    await module.initializeSettings(settings)
    module.subscribeSettings(listener)
    module.previewSettings({ opacity: 56 })
    module.previewSettings({ opacity: 42 })

    expect(mutate).not.toHaveBeenCalled()
    expect(listener).toHaveBeenNthCalledWith(1, { visible: true, opacity: 56 })
    expect(listener).toHaveBeenNthCalledWith(2, { visible: true, opacity: 42 })

    module.setSettings({ opacity: 42 })
    await vi.waitFor(() => expect(mutate).toHaveBeenCalledOnce())
    expect(mutate).toHaveBeenCalledWith('personal', [{ op: 'set', key: 'opacity', value: 42 }])
  })

  it('persists a visibility toggle immediately', async () => {
    const snapshot = (): DesktopPluginSettingsSnapshot<typeof initialValue> => ({
      schema: { fields: [] },
      personal: initialValue,
      workspace: {},
      value: initialValue,
      writableScopes: ['personal', 'workspace']
    })
    const mutate = vi.fn<DesktopPluginSettings['mutate']>(async () => snapshot())
    const settings: DesktopPluginSettings = {
      get: async () => snapshot(),
      mutate,
      onChange: () => () => undefined
    }
    const module = await import('../../../../sdk/typescript/samples/desktop-plugins/token-hud/desktop/src/settings')

    await module.initializeSettings(settings)
    module.setSettings({ visible: false })

    await vi.waitFor(() => expect(mutate).toHaveBeenCalledOnce())
    expect(mutate).toHaveBeenCalledWith('personal', [{ op: 'set', key: 'visible', value: false }])
  })
})
