import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { DesktopPluginSettings, DesktopPluginSettingsSnapshot } from '@dotcraft/plugin'

const initialValue = { visible: true }

describe('Token HUD settings', () => {
  beforeEach(() => vi.resetModules())

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
    const module = await import('./settings')

    await module.initializeSettings(settings)
    module.setSettings({ visible: false })

    await vi.waitFor(() => expect(mutate).toHaveBeenCalledOnce())
    expect(mutate).toHaveBeenCalledWith('personal', [{ op: 'set', key: 'visible', value: false }])
  })
})
