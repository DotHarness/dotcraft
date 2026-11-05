export interface DesktopLifecycle {
  readonly signal: AbortSignal
  readonly isStopping: boolean
}

export interface DesktopLifecycleController extends DesktopLifecycle {
  stop(reason?: unknown): void
}

export function createDesktopLifecycle(): DesktopLifecycleController {
  const controller = new AbortController()
  return {
    signal: controller.signal,
    get isStopping() {
      return controller.signal.aborted
    },
    stop(reason) {
      controller.abort(reason)
    }
  }
}
