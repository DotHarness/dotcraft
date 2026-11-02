export interface AppUpdateExitWatchdog {
  beginQuit(): void
  cancel(): void
}

interface AppUpdateExitOptions {
  app: { exit(code: number): void }
  timeoutMs?: number
}

export function createAppUpdateExitWatchdog({
  app,
  timeoutMs = 10_000
}: AppUpdateExitOptions): AppUpdateExitWatchdog {
  let timer: ReturnType<typeof setTimeout> | null = null
  return {
    beginQuit: () => {
      if (timer) return
      timer = setTimeout(() => app.exit(0), timeoutMs)
      timer.unref()
    },
    cancel: () => {
      if (timer) clearTimeout(timer)
      timer = null
    }
  }
}
