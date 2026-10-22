import type { SessionTurn, SystemEventNotification } from '@dotcraft/sdk/contracts'
import { TERMINAL_COMPACTION } from './contextUsage'
import type { ChatSignals } from './state'

function count(value: unknown): number | null {
  return typeof value === 'number' ? value : null
}

export function systemEventSignals(signals: ChatSignals, event: SystemEventNotification): ChatSignals {
  const kind = event.kind ?? ''
  if (kind === 'compacting') return { ...signals, compacting: event.turnId ? 'auto' : 'manual' }
  if (TERMINAL_COMPACTION.has(kind)) return signals.compacting ? { ...signals, compacting: null } : signals
  if (kind !== 'streamError' || !event.turnId) return signals
  const attempt = count(event.params?.attempt)
  const max = count(event.params?.max)
  if (attempt === null || attempt <= 1 || max === null) return signals
  return { ...signals, retry: { turnId: event.turnId, attempt, max, busy: event.messageKey === 'system.streamError.serverBusy' } }
}

export function turnSignals(signals: ChatSignals, turn: Pick<SessionTurn, 'id' | 'status'>): ChatSignals {
  const retryEnded = signals.retry !== null && (signals.retry.turnId !== turn.id || turn.status !== 'running')
  if (!signals.compacting && !retryEnded) return signals
  return { ...signals, compacting: null, retry: retryEnded ? null : signals.retry }
}
