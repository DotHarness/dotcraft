import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import {
  beginThreadHistory,
  loadThreadHistoryGap,
  openThreadHistoryAround,
  restartThreadHistory,
  useThreadHistoryStore
} from '../stores/threadHistoryStore'
import { listThreadTurns } from '../utils/threadHistory'
import {
  createFakeHistoryServer,
  loadedTurnIds,
  openThreadHistory,
  turnRange,
  type FakeHistoryServer
} from './threadHistoryFakeServer'

const THREAD = 'thread-1'

function useServer(turnCount: number): FakeHistoryServer {
  const server = createFakeHistoryServer(THREAD, turnCount)
  vi.stubGlobal('window', { api: { appServer: { sendRequest: server.request } } })
  return server
}

function gapPositions(): string[] {
  return useThreadHistoryStore.getState().gaps.map((gap) => gap.followingTurnId)
}

function gapBefore(turnId: string): number {
  const gap = useThreadHistoryStore.getState().gaps.find((candidate) => candidate.followingTurnId === turnId)
  if (!gap) throw new Error(`no gap before ${turnId}`)
  return gap.id
}

async function listedPosition(server: FakeHistoryServer, turnId: string, pageCursor: string | null = null) {
  const page = await listThreadTurns(server.request, THREAD, pageCursor, 'descending', 100)
  return {
    backwardsCursor: page.backwardsCursor ?? '',
    offset: page.turns.findIndex((turn) => turn.id === turnId)
  }
}

describe('thread history segments', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    beginThreadHistory(null)
  })

  it('loads older pages into the gap before the oldest segment until the thread start is loaded', async () => {
    const server = useServer(12)
    await openThreadHistory(server, THREAD)
    expect(loadedTurnIds()).toEqual(turnRange(8, 12))
    expect(gapPositions()).toEqual(['turn-8'])

    await loadThreadHistoryGap(gapBefore('turn-8'), 'older')
    expect(loadedTurnIds()).toEqual(turnRange(3, 12))
    expect(gapPositions()).toEqual(['turn-3'])

    await loadThreadHistoryGap(gapBefore('turn-3'), 'older')
    expect(loadedTurnIds()).toEqual(turnRange(1, 12))
    expect(gapPositions()).toEqual([])
  })

  it('opens a segment around a listed turn with a gap on each side', async () => {
    const server = useServer(30)
    await openThreadHistory(server, THREAD)
    const position = await listedPosition(server, 'turn-15')
    expect(position.offset).toBeGreaterThan(0)

    await expect(openThreadHistoryAround(THREAD, position)).resolves.toBe('turn-15')

    expect(loadedTurnIds()).toEqual([...turnRange(11, 19), ...turnRange(26, 30)])
    expect(gapPositions()).toEqual(['turn-11', 'turn-26'])
  })

  it('fills a gap between segments from either edge and merges them when the ranges meet', async () => {
    const server = useServer(30)
    await openThreadHistory(server, THREAD)
    await openThreadHistoryAround(THREAD, await listedPosition(server, 'turn-15'))

    await loadThreadHistoryGap(gapBefore('turn-26'), 'newer')
    expect(loadedTurnIds()).toEqual([...turnRange(11, 24), ...turnRange(26, 30)])
    expect(gapPositions()).toEqual(['turn-11', 'turn-26'])

    await loadThreadHistoryGap(gapBefore('turn-26'), 'older')
    expect(loadedTurnIds()).toEqual(turnRange(11, 30))
    expect(gapPositions()).toEqual(['turn-11'])
  })

  it('merges a segment opened at the top of a listing page into the newest segment it reaches', async () => {
    const server = useServer(12)
    await openThreadHistory(server, THREAD)
    const secondPage = await listThreadTurns(server.request, THREAD, null, 'descending', 6)
    const position = await listedPosition(server, 'turn-6', secondPage.nextCursor)
    expect(position.offset).toBe(0)

    await expect(openThreadHistoryAround(THREAD, position)).resolves.toBe('turn-6')

    expect(loadedTurnIds()).toEqual(turnRange(2, 12))
    expect(gapPositions()).toEqual(['turn-2'])
    await loadThreadHistoryGap(gapBefore('turn-2'), 'older')
    expect(loadedTurnIds()).toEqual(turnRange(1, 12))
    expect(gapPositions()).toEqual([])
  })

  it('resolves a listed turn that is already loaded without changing loaded history', async () => {
    const server = useServer(12)
    await openThreadHistory(server, THREAD)

    await expect(openThreadHistoryAround(THREAD, await listedPosition(server, 'turn-10'))).resolves.toBe('turn-10')

    expect(loadedTurnIds()).toEqual(turnRange(8, 12))
    expect(gapPositions()).toEqual(['turn-8'])
  })

  it('shares one request per gap edge', async () => {
    const server = useServer(12)
    await openThreadHistory(server, THREAD)
    const callsBefore = server.turnsListCalls().length

    const gapId = gapBefore('turn-8')
    await Promise.all([loadThreadHistoryGap(gapId, 'older'), loadThreadHistoryGap(gapId, 'older')])

    expect(server.turnsListCalls().length - callsBefore).toBe(1)
    expect(loadedTurnIds()).toEqual(turnRange(3, 12))
  })

  it('ignores a page that lands after the history was restarted', async () => {
    const server = useServer(12)
    await openThreadHistory(server, THREAD)
    const release = server.holdNextTurnsList()
    const load = loadThreadHistoryGap(gapBefore('turn-8'), 'older')

    const head = useConversationStore.getState().turns
    restartThreadHistory(THREAD, head, null)
    release()
    await load

    expect(loadedTurnIds()).toEqual(turnRange(8, 12))
    expect(gapPositions()).toEqual([])
  })

  it('rejects opening a segment once the thread is switched away', async () => {
    const server = useServer(30)
    await openThreadHistory(server, THREAD)
    const position = await listedPosition(server, 'turn-15')
    const release = server.holdNextTurnsList()
    const opening = openThreadHistoryAround(THREAD, position)

    useThreadStore.getState().setActiveThreadId('thread-2')
    beginThreadHistory('thread-2')
    release()

    await expect(opening).rejects.toThrow()
    expect(loadedTurnIds()).toEqual(turnRange(26, 30))
  })
})
