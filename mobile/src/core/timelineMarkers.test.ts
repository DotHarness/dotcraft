import { describe, expect, it } from 'vitest'
import { emptyHistory, type ChatHistory, type HistoryItem, type HistoryTurn } from './history'
import { systemEventSignals, turnSignals } from './signals'
import { NO_SIGNALS } from './state'
import { buildTranscript, type TranscriptEntry } from './transcript'

let clock = 0

function item(type: string, payload: Record<string, unknown>, turnId = 't1'): HistoryItem {
  clock += 1
  const at = `2026-10-22T08:00:${String(clock).padStart(2, '0')}Z`
  return { id: `i${clock}`, turnId, type, status: 'completed', payload, createdAt: at, completedAt: at, partial: false }
}

function turn(id: string, status: string, extra: Partial<HistoryTurn> = {}): HistoryTurn {
  return { id, status, error: null, startedAt: '2026-10-22T08:00:00Z', completedAt: '2026-10-22T08:01:00Z', ...extra }
}

function history(items: HistoryItem[], turns: HistoryTurn[] = [turn('t1', 'completed')]): ChatHistory {
  return { ...emptyHistory(), items, turns }
}

function flat(entries: TranscriptEntry[]): TranscriptEntry[] {
  return entries.flatMap((entry) => (entry.kind === 'activity' ? [entry, ...flat(entry.children)] : [entry]))
}

function dividers(entries: TranscriptEntry[]) {
  return flat(entries).flatMap((entry) => (entry.kind === 'divider' ? [entry] : []))
}

describe('system notices', () => {
  it('turns each notice kind into its divider and skips the ones Desktop hides', () => {
    const entries = buildTranscript(
      history([
        item('userMessage', { text: 'hi' }),
        item('agentMessage', { text: 'done', phase: 'final' }),
        item('systemNotice', { kind: 'forked', sourceThreadId: 'thread_a' }),
        item('systemNotice', { kind: 'remoteRoute', reason: 'connected', initiator: 'agent', hostName: 'devbox' }),
        item('systemNotice', { kind: 'remoteRoute', reason: 'disconnected', initiator: 'client', hostName: 'devbox' }),
        item('systemNotice', { kind: 'remoteRoute', reason: 'leaseLost', initiator: 'system', hostId: 'host_1' }),
        item('systemNotice', { kind: 'channel', reason: 'bound', channelName: 'telegram', targetName: 'Ops' }),
        item('systemNotice', { kind: 'channel', reason: 'unbound', channelName: 'weixin' }),
        item('systemNotice', { kind: 'compacted', trigger: 'manual', mode: 'partial' }),
        item('systemNotice', { kind: 'compacted', trigger: 'auto', mode: 'micro' }),
        item('systemNotice', { kind: 'compacted', trigger: 'reactive', mode: 'partial' }),
        item('systemNotice', { kind: 'later' }),
      ]),
    )
    expect(dividers(entries).map(({ divider, host, channel, target }) => ({ divider, host, channel, target }))).toEqual([
      { divider: 'forked' },
      { divider: 'remoteConnected', host: 'devbox' },
      { divider: 'remoteLocal' },
      { divider: 'remoteDisconnected', host: 'host_1' },
      { divider: 'channelBound', channel: 'Telegram', target: 'Ops' },
      { divider: 'channelUnbound', channel: 'WeChat' },
      { divider: 'compactedManual' },
      { divider: 'compactedReactive' },
    ])
  })

  it('keeps a divider before the final reply out of the collapsed work', () => {
    const entries = buildTranscript(
      history([
        item('userMessage', { text: 'hi' }),
        item('agentMessage', { text: 'looking', phase: 'commentary' }),
        item('systemNotice', { kind: 'compacted', trigger: 'auto', mode: 'partial' }),
        item('agentMessage', { text: 'done', phase: 'final' }),
      ]),
    )
    expect(entries.map((entry) => entry.kind)).toEqual(['user', 'activity', 'divider', 'assistant'])
  })
})

describe('message origins', () => {
  it('marks messages the computer sent and links the first message of a spawned chat', () => {
    const entries = buildTranscript(
      history(
        [
          item('userMessage', { text: 'first' }),
          item('userMessage', { text: 'from automation', triggerKind: 'automation', triggerLabel: 'Nightly' }, 't2'),
          item('userMessage', { text: 'continue', triggerKind: 'goal' }, 't3'),
          item('userMessage', { text: 'note', triggerKind: 'subagentInput', triggerLabel: 'reviewer' }, 't4'),
          item('userMessage', { text: 'mine', sentAsGoal: true }, 't5'),
        ],
        ['t1', 't2', 't3', 't4', 't5'].map((id) => turn(id, 'completed')),
      ),
      null,
      { spawnedFrom: 'thread_parent' },
    )
    const users = entries.flatMap((entry) => (entry.kind === 'user' ? [{ origin: entry.origin, goal: entry.goal }] : []))
    expect(users).toEqual([
      { origin: { kind: 'thread', label: '', threadId: 'thread_parent' }, goal: false },
      { origin: { kind: 'automation', label: 'Nightly', threadId: null }, goal: false },
      { origin: { kind: 'goal', label: '', threadId: null }, goal: false },
      { origin: { kind: 'subagent', label: 'reviewer', threadId: null }, goal: false },
      { origin: null, goal: true },
    ])
  })

  it('does not mark a spawned chat until its first turn is loaded', () => {
    const entries = buildTranscript({ ...history([item('userMessage', { text: 'later' })]), olderCursor: 'older' }, null, {
      spawnedFrom: 'thread_parent',
    })
    expect(entries.find((entry) => entry.kind === 'user')).toMatchObject({ origin: null })
  })
})

describe('thread actions', () => {
  it('shows one card per chat for successful thread tools and keeps failed calls as tool rows', () => {
    const created = { toolName: 'CreateThread', namespace: 'desktop', success: true, structuredResult: { thread: { id: 'thread_b', displayName: 'Docs' } } }
    const entries = flat(
      buildTranscript(
        history([
          item('userMessage', { text: 'split it' }),
          item('dynamicToolCall', created),
          item('dynamicToolCall', created),
          item('dynamicToolCall', { toolName: 'SendMessageToThread', namespace: 'desktop', success: true, structuredResult: { threadId: 'thread_c', queued: true } }),
          item('dynamicToolCall', { toolName: 'SendMessageToThread', namespace: 'desktop', success: false }),
          item('agentMessage', { text: 'done', phase: 'final' }),
        ]),
      ),
    )
    expect(entries.flatMap((entry) => (entry.kind === 'threadAction' ? [entry.action] : []))).toEqual([
      { kind: 'created', threadId: 'thread_b', name: 'Docs', queued: false },
      { kind: 'messaged', threadId: 'thread_c', name: null, queued: true },
    ])
    expect(entries.filter((entry) => entry.kind === 'tool' || entry.kind === 'toolGroup')).toHaveLength(1)
  })
})

describe('turn endings', () => {
  it('shows each error in place and the turn error only when no item carries it', () => {
    const entries = buildTranscript(
      history(
        [item('userMessage', { text: 'go' }), item('error', { message: 'stream broke' }), item('agentMessage', { text: 'partial' })],
        [turn('t1', 'failed', { error: 'stream broke' })],
      ),
    )
    const failures = entries.filter((entry) => entry.kind === 'failure')
    expect(failures).toHaveLength(1)
    expect(failures[0]).toMatchObject({ message: 'stream broke', overloaded: false, retry: null })
    const turnOnly = buildTranscript(history([item('userMessage', { text: 'go' })], [turn('t1', 'failed', { error: 'boom' })]))
    expect(turnOnly.at(-1)).toEqual({ kind: 'failure', id: 'turn-t1', message: 'boom', overloaded: false, retry: null })
    const silent = buildTranscript(history([item('userMessage', { text: 'go' })], [turn('t1', 'failed')]))
    expect(silent.some((entry) => entry.kind === 'failure')).toBe(false)
  })

  it('uses Desktop wording for provider failures', () => {
    const failed = (providerError: string) =>
      buildTranscript(history([item('userMessage', { text: 'go' })], [turn('t1', 'failed', { error: 'boom', providerError })])).at(-1)
    expect(failed('usageLimitExceeded')).toMatchObject({ kind: 'divider', divider: 'usageLimit' })
    expect(failed('rateLimitExceeded')).toMatchObject({ kind: 'failure', message: 'boom', overloaded: true, retry: null })
  })

  it('offers capacity retry on the last turn with a growing countdown', () => {
    const overloaded = (id: string) => turn(id, 'failed', { error: 'busy', providerError: 'serverOverloaded' })
    const user = item('userMessage', { text: 'go' })
    const first = buildTranscript(history([user, item('error', { message: 'busy' })], [overloaded('t1')]))
    expect(first.find((entry) => entry.kind === 'failure')).toMatchObject({ overloaded: true, retry: { delay: 10 } })

    const reissued = history(
      [user, item('error', { message: 'busy' }), item('error', { message: 'busy' }, 't2'), item('error', { message: 'busy' }, 't3')],
      [overloaded('t1'), overloaded('t2'), overloaded('t3')],
    )
    const entries = buildTranscript(reissued)
    const failures = entries.filter((entry) => entry.kind === 'failure')
    expect(failures).toHaveLength(2)
    expect(failures.map((entry) => entry.retry)).toEqual([null, { delay: 120 }])
  })

  it('says you stopped a turn the phone interrupted', () => {
    const cancelled = history([item('userMessage', { text: 'go' })], [turn('t1', 'cancelled')])
    expect(buildTranscript(cancelled).find((entry) => entry.kind === 'activity')).toMatchObject({ status: 'stopped' })
    expect(buildTranscript(cancelled, null, { stopped: ['t1'] }).find((entry) => entry.kind === 'activity')).toMatchObject({ status: 'youStopped' })
  })
})

describe('live signals', () => {
  it('follows compaction and stream retries until they end', () => {
    const manual = systemEventSignals(NO_SIGNALS, { threadId: 'a', kind: 'compacting' })
    expect(manual.compacting).toBe('manual')
    expect(systemEventSignals(NO_SIGNALS, { threadId: 'a', turnId: 't1', kind: 'compacting' }).compacting).toBe('auto')
    expect(systemEventSignals(manual, { threadId: 'a', kind: 'compactSkipped' }).compacting).toBeNull()

    const first = systemEventSignals(NO_SIGNALS, { threadId: 'a', turnId: 't1', kind: 'streamError', params: { attempt: 1, max: 5 } })
    expect(first).toBe(NO_SIGNALS)
    const retry = systemEventSignals(NO_SIGNALS, {
      threadId: 'a',
      turnId: 't1',
      kind: 'streamError',
      messageKey: 'system.streamError.serverBusy',
      params: { attempt: 2, max: 5 },
    })
    expect(retry.retry).toEqual({ turnId: 't1', attempt: 2, max: 5, busy: true })
    expect(turnSignals(retry, { id: 't1', status: 'running' })).toBe(retry)
    expect(turnSignals(retry, { id: 't1', status: 'completed' }).retry).toBeNull()
  })
})
