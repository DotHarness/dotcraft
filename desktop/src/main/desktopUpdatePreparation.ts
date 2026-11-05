import type { DesktopLifecycleController } from './desktopLifecycle'
import { prepareAppUpdateProcesses } from './appUpdateProcesses'

export interface DesktopUpdatePreparationOptions {
  lifecycle: DesktopLifecycleController
  runtimeExecutable?: string
  stopBackgroundWork(): void
  disposeRuntime(): Promise<void>
}

export class DesktopUpdatePreparation {
  constructor(private readonly options: DesktopUpdatePreparationOptions) {}

  async prepare(): Promise<void> {
    this.options.lifecycle.stop(new Error('Desktop is preparing to install an update.'))
    this.options.stopBackgroundWork()
    const cleanup = this.options.disposeRuntime()
    let timer: ReturnType<typeof setTimeout> | undefined
    try {
      await Promise.race([
        cleanup,
        new Promise<void>((resolve) => {
          timer = setTimeout(resolve, 5_000)
        })
      ])
    } finally {
      if (timer) clearTimeout(timer)
    }
    await prepareAppUpdateProcesses({ runtimeExecutable: this.options.runtimeExecutable })
  }
}
