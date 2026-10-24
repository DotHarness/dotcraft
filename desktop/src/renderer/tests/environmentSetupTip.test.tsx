import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'

type TipModule = typeof import('../components/projectActions/environmentSetupTip')

async function loadTip(): Promise<TipModule> {
  vi.resetModules()
  return import('../components/projectActions/environmentSetupTip')
}

async function flush(): Promise<void> {
  for (let index = 0; index < 5; index++) await Promise.resolve()
}

beforeEach(() => {
  vi.restoreAllMocks()
})

describe('environment setup tip', () => {
  it('stores a dismissal in the app settings', async () => {
    const set = vi.fn().mockResolvedValue(undefined)
    installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({}), set } })
    const tip = await loadTip()
    const listener = vi.fn()
    tip.onEnvironmentSetupTipDismissedChange(listener)
    await flush()
    expect(tip.environmentSetupTipDismissed()).toBe(false)

    tip.dismissEnvironmentSetupTip()

    expect(set).toHaveBeenCalledWith({ environmentSetupTipDismissed: true })
    expect(tip.environmentSetupTipDismissed()).toBe(true)
    expect(listener).toHaveBeenLastCalledWith(true)
  })

  it('stays dismissed after a restart', async () => {
    installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({ environmentSetupTipDismissed: true }), set: vi.fn() } })
    const tip = await loadTip()
    const listener = vi.fn()
    tip.onEnvironmentSetupTipDismissedChange(listener)
    await flush()

    expect(listener).toHaveBeenCalledWith(true)
    expect(tip.environmentSetupTipDismissed()).toBe(true)
  })
})
