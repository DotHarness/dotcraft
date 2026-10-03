import type { DesktopPluginHost } from '@dotcraft/plugin'
import { expect, it, vi } from 'vitest'
import { activate } from './index'
import { TokenHud } from './TokenHud'

vi.mock('./usage', () => ({ startUsageFeed: vi.fn() }))

it('registers the HUD and visibility command without a settings page', async () => {
  const value = { visible: true }
  const snapshot = {
    schema: { fields: [] }, personal: value, workspace: {}, value,
    writableScopes: ['personal', 'workspace']
  }
  const mutate = vi.fn(async () => snapshot)
  const add = vi.fn()
  const host = {
    settings: {
      get: async () => snapshot,
      mutate,
      onChange: () => () => undefined
    },
    effect: vi.fn(),
    ui: { add, showToast: vi.fn() },
    environment: { locale: 'en' }
  }

  const activation = await activate(host as unknown as DesktopPluginHost)

  expect(activation?.settingsPages ?? []).toEqual([])
  expect(add).toHaveBeenCalledWith('composer.status.trailing', TokenHud)
  const command = activation?.commands?.find((entry) => entry.id === 'toggle')
  expect(command).toBeDefined()
  await command?.execute(
    { workspacePath: null, threadId: null, viewId: 'conversation' },
    host as unknown as DesktopPluginHost
  )
  expect(mutate).toHaveBeenCalledWith('personal', [{ op: 'set', key: 'visible', value: false }])
})
