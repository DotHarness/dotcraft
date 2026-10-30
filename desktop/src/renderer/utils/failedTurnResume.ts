import type { ConversationTurn } from '../types/conversation'

const RESUMABLE_PROVIDER_ERRORS: ReadonlySet<string> = new Set([
  'serverOverloaded',
  'internalServerError',
  'httpConnectionFailed',
  'responseStreamConnectionFailed',
  'responseStreamDisconnected',
  'responseTooManyFailedAttempts'
])

export function canResumeLastTurn(turns: readonly ConversationTurn[]): boolean {
  const last = turns[turns.length - 1]
  if (last?.status === 'cancelled') return true
  return last?.status === 'failed' && RESUMABLE_PROVIDER_ERRORS.has(last.providerError ?? '')
}

function isBareRetryFailure(turn: ConversationTurn): boolean {
  return turn.status === 'failed' && turn.items.every((item) => item.type === 'error')
}

/** A run of identical failed retries renders only its latest turn. */
export function findSupersededRetryTurnIds(turns: readonly ConversationTurn[]): ReadonlySet<string> {
  const superseded = new Set<string>()
  for (let i = 0; i < turns.length - 1; i++) {
    const turn = turns[i]
    const next = turns[i + 1]
    if (isBareRetryFailure(turn) && isBareRetryFailure(next)
      && (turn.providerError ?? turn.error) === (next.providerError ?? next.error)) {
      superseded.add(turn.id)
    }
  }
  return superseded
}
