export type ShellRuntimeSource = 'commandExecution' | 'terminal'

export interface ShellRuntimeEntry {
  output: string
  source: ShellRuntimeSource
}

export interface PendingShellRuntimeUpdate extends ShellRuntimeEntry {
  replace: boolean
}

export interface ShellRuntimeBuffer {
  queue(callId: string, source: ShellRuntimeSource, output: string, replace?: boolean): void
  flush(): void
  clear(callId: string): void
  reset(): void
}

const DEFAULT_FLUSH_MS = 50
/** Matches the bounded live command-output window used by the Codex desktop app. */
export const SHELL_RUNTIME_MAX_CHARS = 20_000
export const SHELL_RUNTIME_TRUNCATION_MARKER = '[output truncated]\n'

export function limitShellRuntimeOutput(output: string): string {
  if (output.length <= SHELL_RUNTIME_MAX_CHARS) return output
  const tailLength = SHELL_RUNTIME_MAX_CHARS - SHELL_RUNTIME_TRUNCATION_MARKER.length
  return `${SHELL_RUNTIME_TRUNCATION_MARKER}${output.slice(-tailLength)}`
}

type ShellRuntimeOutputTransform = (output: string) => string

const preserveShellRuntimeOutput: ShellRuntimeOutputTransform = (output) => output

export function mergeShellRuntimeUpdates(
  current: Map<string, ShellRuntimeEntry>,
  updates: ReadonlyMap<string, PendingShellRuntimeUpdate>,
  transformOutput: ShellRuntimeOutputTransform = preserveShellRuntimeOutput
): Map<string, ShellRuntimeEntry> {
  const next = new Map(current)
  let changed = false
  for (const [callId, update] of updates) {
    const previous = next.get(callId)
    if (update.source === 'commandExecution' && previous?.source === 'terminal') continue
    const resetForTerminal = update.source === 'terminal' && previous?.source !== 'terminal'
    next.set(callId, {
      source: update.source,
      output: transformOutput(update.replace || resetForTerminal
        ? update.output
        : `${previous?.output ?? ''}${update.output}`)
    })
    changed = true
  }
  return changed ? next : current
}

/**
 * Coalesces live shell output outside the durable turn tree. Terminal output is
 * authoritative once observed; commandExecution deltas remain a fallback.
 */
export function createShellRuntimeBuffer(
  commit: (updates: ReadonlyMap<string, PendingShellRuntimeUpdate>) => void,
  options: {
    flushMs?: number
    transformOutput?: ShellRuntimeOutputTransform
  } = {}
): ShellRuntimeBuffer {
  const flushMs = options.flushMs ?? DEFAULT_FLUSH_MS
  const transformOutput = options.transformOutput ?? preserveShellRuntimeOutput
  const pending = new Map<string, PendingShellRuntimeUpdate>()
  let flushTimer: ReturnType<typeof setTimeout> | null = null

  const flush = (): void => {
    if (flushTimer != null) {
      clearTimeout(flushTimer)
      flushTimer = null
    }
    if (pending.size === 0) return

    const updates = new Map(pending)
    pending.clear()
    commit(updates)
  }

  return {
    queue(callId, source, output, replace = false) {
      if (!callId || !output) return
      const previous = pending.get(callId)
      if (source === 'commandExecution' && previous?.source === 'terminal') return

      if (source === 'terminal') {
        pending.set(callId, {
          source,
          output: transformOutput(replace || previous?.source !== 'terminal'
            ? output
            : `${previous.output}${output}`),
          replace: replace || previous?.source !== 'terminal'
        })
      } else {
        pending.set(callId, {
          source,
          output: transformOutput(previous && !previous.replace ? `${previous.output}${output}` : output),
          replace: previous?.replace ?? false
        })
      }

      if (flushTimer == null) {
        flushTimer = setTimeout(flush, flushMs)
      }
    },
    flush,
    clear(callId) {
      pending.delete(callId)
    },
    reset() {
      pending.clear()
      if (flushTimer != null) clearTimeout(flushTimer)
      flushTimer = null
    }
  }
}
