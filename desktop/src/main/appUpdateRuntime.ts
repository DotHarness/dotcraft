export interface DesktopHubOperationGate {
  run<T>(operation: () => Promise<T>): Promise<T>
}

export class AppUpdateRuntime implements DesktopHubOperationGate {
  private preparing = false
  private readonly pending = new Set<Promise<unknown>>()

  get isPreparing(): boolean {
    return this.preparing
  }

  quiesce(): void {
    this.preparing = true
  }

  async run<T>(operation: () => Promise<T>): Promise<T> {
    this.assertRunning()
    const pending = Promise.resolve().then(() => {
      this.assertRunning()
      return operation()
    })
    this.pending.add(pending)
    try {
      const result = await pending
      this.assertRunning()
      return result
    } finally {
      this.pending.delete(pending)
    }
  }

  async waitForIdle(signal: AbortSignal): Promise<void> {
    signal.throwIfAborted()
    if (this.pending.size === 0) return
    await new Promise<void>((resolve, reject) => {
      const abort = (): void => reject(signal.reason)
      signal.addEventListener('abort', abort, { once: true })
      void Promise.allSettled([...this.pending]).then(() => {
        signal.removeEventListener('abort', abort)
        resolve()
      })
    })
    signal.throwIfAborted()
  }

  private assertRunning(): void {
    if (this.preparing) throw new Error('Desktop is preparing to install an update. Local runtime operations are paused.')
  }
}
