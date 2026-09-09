import type { EventEmitter } from 'events'

export function isBrokenStandardIoError(error: unknown): boolean {
  const code = (error as NodeJS.ErrnoException | undefined)?.code
  if (code === 'EIO' || code === 'EPIPE') return true
  const message = error instanceof Error ? error.message : String(error ?? '')
  return /write EIO|EPIPE/i.test(message)
}

export function installStandardIoErrorGuard(
  stream: EventEmitter,
  onBrokenPipe: () => void = () => undefined
): () => void {
  const handleError = (error: unknown): void => {
    if (!isBrokenStandardIoError(error)) throw error
    onBrokenPipe()
  }

  stream.on('error', handleError)
  return () => stream.off('error', handleError)
}
