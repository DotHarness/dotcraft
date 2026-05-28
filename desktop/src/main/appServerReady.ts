export const APP_SERVER_READY_POLL_MS = 250

export async function waitForReadyz(
  host: string,
  port: number,
  shouldContinue: () => boolean = () => true
): Promise<boolean> {
  const base = `http://${host}:${port}`
  while (shouldContinue()) {
    try {
      const res = await fetch(`${base}/readyz`)
      if (res.ok) return true
    } catch {
      // Keep polling until the AppServer is ready or the caller cancels.
    }
    await new Promise((resolve) => setTimeout(resolve, APP_SERVER_READY_POLL_MS))
  }
  return false
}
