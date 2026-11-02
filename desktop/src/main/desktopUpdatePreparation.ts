import { AppUpdateRuntime } from './appUpdateRuntime'
import { AppUpdatePreparationError, prepareAppUpdateProcesses } from './appUpdateProcesses'

export interface DesktopUpdatePreparationOptions {
  runtime: AppUpdateRuntime
  desktopExecutable: string
  runtimeExecutable?: string
  stopBackgroundWork: () => void
  disposeRuntime: () => Promise<void>
}

export class DesktopUpdatePreparation {
  private prepared: { release(): void } | null = null

  constructor(private readonly options: DesktopUpdatePreparationOptions) {}

  async prepare(): Promise<void> {
    const { runtime, desktopExecutable, runtimeExecutable } = this.options
    runtime.quiesce()
    this.options.stopBackgroundWork()
    console.info('[desktop-update] preparing installation', { desktopExecutable, runtimeExecutable })
    try {
      this.prepared = await prepareAppUpdateProcesses({
        desktopExecutable,
        runtimeExecutable,
        log: (stage, details) => console.info('[desktop-update] process preparation', { stage, ...details }),
        quiesce: async (signal: AbortSignal) => {
          await this.options.disposeRuntime()
          await runtime.waitForIdle(signal)
        }
      })
      console.info('[desktop-update] background processes stopped; starting installer')
    } catch (error) {
      console.warn('[desktop-update] process preparation blocked', error instanceof AppUpdatePreparationError
        ? { stage: error.stage, blockers: error.blockers, message: error.message }
        : { message: error instanceof Error ? error.message : String(error) })
      throw error
    }
  }

  release(): void {
    this.prepared?.release()
    this.prepared = null
  }
}
