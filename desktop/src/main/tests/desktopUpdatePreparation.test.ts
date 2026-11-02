import { afterEach, describe, expect, it, vi } from 'vitest'
import { AppUpdateRuntime } from '../appUpdateRuntime'
import { DesktopUpdatePreparation } from '../desktopUpdatePreparation'
import * as processes from '../appUpdateProcesses'

afterEach(() => vi.restoreAllMocks())

describe('DesktopUpdatePreparation', () => {
  it('quiesces clients before process shutdown and holds the lease through installer handoff', async () => {
    const runtime = new AppUpdateRuntime()
    const steps: string[] = []
    const release = vi.fn()
    vi.spyOn(processes, 'prepareAppUpdateProcesses').mockImplementation(async ({ quiesce }) => {
      steps.push('process coordination')
      await quiesce(new AbortController().signal)
      steps.push('processes exited')
      return { release }
    })
    const preparation = new DesktopUpdatePreparation({
      runtime,
      desktopExecutable: 'C:/App/DotCraft.exe',
      runtimeExecutable: 'C:/App/resources/bin/dotcraft.exe',
      stopBackgroundWork: () => { expect(runtime.isPreparing).toBe(true); steps.push('stop watchers') },
      disposeRuntime: async () => { steps.push('dispose runtime') }
    })
    await preparation.prepare()
    expect(steps).toEqual(['stop watchers', 'process coordination', 'dispose runtime', 'processes exited'])
    expect(release).not.toHaveBeenCalled()
    preparation.release()
    preparation.release()
    expect(release).toHaveBeenCalledOnce()
  })
})
