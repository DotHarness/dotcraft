import { afterEach, describe, expect, it, vi } from 'vitest'
import { createDesktopLifecycle } from '../desktopLifecycle'
import { DesktopUpdatePreparation } from '../desktopUpdatePreparation'
import * as processes from '../appUpdateProcesses'

afterEach(() => {
  vi.restoreAllMocks()
  vi.useRealTimers()
})

describe('DesktopUpdatePreparation', () => {
  it('cancels starts and subscriptions before stopping the application-owned Hub', async () => {
    const lifecycle = createDesktopLifecycle()
    const order: string[] = []
    vi.spyOn(processes, 'prepareAppUpdateProcesses').mockImplementation(async () => {
      order.push('stop Hub')
    })
    const preparation = new DesktopUpdatePreparation({
      lifecycle,
      stopBackgroundWork: () => {
        expect(lifecycle.signal.aborted).toBe(true)
        order.push('stop retries')
      },
      disposeRuntime: async () => {
        order.push('close window resources')
      }
    })
    await preparation.prepare()
    expect(order).toEqual(['stop retries', 'close window resources', 'stop Hub'])
  })

  it('does not make an unended subscription an idle barrier before Hub shutdown', async () => {
    vi.useFakeTimers()
    const stop = vi.spyOn(processes, 'prepareAppUpdateProcesses').mockResolvedValue()
    const preparation = new DesktopUpdatePreparation({
      lifecycle: createDesktopLifecycle(),
      stopBackgroundWork: () => {},
      disposeRuntime: () => new Promise(() => {})
    })
    const preparing = preparation.prepare()
    await vi.advanceTimersByTimeAsync(5_000)
    await preparing
    expect(stop).toHaveBeenCalledOnce()
  })
})
