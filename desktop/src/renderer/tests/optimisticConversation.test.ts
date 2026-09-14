import { buildComposerInputParts } from '../utils/composeInputParts'
import { createOptimisticUserMessage } from '../utils/inputPresentation'
import { describe, it, expect, beforeEach } from 'vitest'
import type { ConversationTurn } from '../types/conversation'
import { useConversationStore } from '../stores/conversationStore'
const s = () => useConversationStore.getState()
function makeTurn(overrides: Record<string, unknown> = {}): Record<string, unknown> { return { id: 'turn-1', threadId: 'thread-1', status: 'running', items: [], startedAt: new Date().toISOString(), ...overrides } }
beforeEach(() => s().reset())
describe('optimistic turns', () => {
  it('addOptimisticTurn immediately adds the turn and sets running state', () => {
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-1',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-item-1',
          clientUserMessageId: 'initial',
          type: 'userMessage',
          status: 'completed',
          text: 'Hello',
          createdAt: new Date().toISOString()
        }
      ],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)

    expect(s().turns).toHaveLength(1)
    expect(s().turns[0].id).toBe('local-turn-1')
    expect(s().turns[0].items[0].text).toBe('Hello')
    expect(s().turnStatus).toBe('running')
  })

  it('onTurnStarted replaces optimistic turn, preserving user message items', () => {
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-1',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-item-1',
          clientUserMessageId: 'initial',
          type: 'userMessage',
          status: 'completed',
          text: 'Hello',
          createdAt: new Date().toISOString()
        }
      ],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)

    s().onTurnStarted(makeTurn({ id: 'real-turn-1', items: [] }))
    s().promoteOptimisticTurn('local-turn-1', 'real-turn-1')

    const state = s()
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('real-turn-1')
    expect(state.turns[0].items[0].text).toBe('Hello')
    expect(state.turns[0].items[0].type).toBe('userMessage')
  })

  it('removeOptimisticTurn removes the turn and resets running state', () => {
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-fail',
      threadId: 'thread-1',
      status: 'running',
      items: [],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)
    expect(s().turnStatus).toBe('running')

    s().removeOptimisticTurn('local-turn-fail')
    expect(s().turns).toHaveLength(0)
    expect(s().turnStatus).toBe('idle')
    expect(s().activeTurnId).toBeNull()
  })

  it('promoteOptimisticTurn replaces local ID with server ID in turns and activeTurnId', () => {
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-123',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-item-1',
          clientUserMessageId: 'initial',
          type: 'userMessage',
          status: 'completed',
          text: 'Hello',
          createdAt: new Date().toISOString()
        }
      ],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)
    expect(s().activeTurnId).toBe('local-turn-123')

    s().promoteOptimisticTurn('local-turn-123', 'turn_server_abc')

    const state = s()
    expect(state.activeTurnId).toBe('turn_server_abc')
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn_server_abc')
    expect(state.turns[0].items[0].text).toBe('Hello')
  })

  it('preserveExistingRealtime prefers the server userMessage over the promoted optimistic preview', () => {
    const optimisticTurn: ConversationTurn = {
      id: 'local-turn-preview',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-preview',
          clientUserMessageId: 'preview',
          type: 'userMessage',
          status: 'completed',
          text: 'cancel this turn',
          nativeInputParts: [{ type: 'text', text: 'cancel this turn' }],
          createdAt: '2026-06-13T10:00:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:00.000Z'
    }

    s().addOptimisticTurn(optimisticTurn)
    s().promoteOptimisticTurn('local-turn-preview', 'turn-server-preview')
    s().setTurns([
      makeTurn({
        id: 'turn-server-preview',
        threadId: 'thread-1',
        status: 'cancelled',
        startedAt: '2026-06-13T10:00:00.050Z',
        completedAt: '2026-06-13T10:00:01.000Z',
        items: [
          {
            id: 'server-user-preview',
          clientUserMessageId: 'preview',
            type: 'userMessage',
            status: 'completed',
            payload: {
              text: 'cancel this turn',
              nativeInputParts: [{ type: 'text', text: 'cancel this turn' }]
            },
            createdAt: '2026-06-13T10:00:00.050Z',
            completedAt: '2026-06-13T10:00:00.050Z'
          }
        ]
      })
    ], { preserveExistingRealtime: true })

    const state = s()
    const userMessages = state.turns.flatMap((turn) => turn.items.filter((item) => item.type === 'userMessage'))
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-server-preview')
    expect(state.turns[0].status).toBe('cancelled')
    expect(userMessages).toHaveLength(1)
    expect(userMessages[0].id).toBe('server-user-preview')
  })

  it('onTurnStarted replaces a promoted optimistic preview with the canonical server userMessage', () => {
    s().addOptimisticTurn({
      id: 'local-turn-live',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-live',
          clientUserMessageId: 'live',
          type: 'userMessage',
          status: 'completed',
          text: 'live canonical',
          nativeInputParts: [{ type: 'text', text: 'live canonical' }],
          createdAt: '2026-06-13T10:00:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:00.000Z'
    })
    s().promoteOptimisticTurn('local-turn-live', 'turn-server-live')

    s().onTurnStarted(makeTurn({
      id: 'turn-server-live',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-06-13T10:00:00.050Z',
      items: [
        {
          id: 'server-user-live',
          clientUserMessageId: 'live',
          type: 'userMessage',
          status: 'completed',
          payload: {
            text: 'live canonical',
            nativeInputParts: [{ type: 'text', text: 'live canonical' }]
          },
          createdAt: '2026-06-13T10:00:00.050Z'
        }
      ]
    }))

    const state = s()
    const userMessages = state.turns[0].items.filter((item) => item.type === 'userMessage')
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-server-live')
    expect(state.turnStatus).toBe('running')
    expect(userMessages).toHaveLength(1)
    expect(userMessages[0].id).toBe('server-user-live')
  })

  it('does not keep a represented local optimistic turn when a snapshot arrives before promotion', () => {
    const optimisticTurn: ConversationTurn = {
      id: 'local-turn-cancel',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-cancel',
          clientUserMessageId: 'cancel',
          type: 'userMessage',
          status: 'completed',
          text: 'stop me',
          createdAt: '2026-06-13T10:00:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:00.000Z'
    }

    s().addOptimisticTurn(optimisticTurn)
    s().setTurns([
      makeTurn({
        id: 'turn-server-cancel',
        threadId: 'thread-1',
        status: 'cancelled',
        startedAt: '2026-06-13T10:00:00.100Z',
        completedAt: '2026-06-13T10:00:01.000Z',
        items: [
          {
            id: 'server-user-cancel',
          clientUserMessageId: 'cancel',
            type: 'userMessage',
            status: 'completed',
            payload: { text: 'stop me' },
            createdAt: '2026-06-13T10:00:00.100Z'
          }
        ]
      })
    ], { preserveExistingRealtime: true })
    s().promoteOptimisticTurn('local-turn-cancel', 'turn-server-cancel')

    const state = s()
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-server-cancel')
    expect(state.turns[0].items.filter((item) => item.type === 'userMessage')).toHaveLength(1)
    expect(state.activeTurnId).toBeNull()
  })

  it('promoteOptimisticTurn coalesces when the server turn is already present', () => {
    s().setTurns([
      makeTurn({
        id: 'turn-server-existing',
        threadId: 'thread-1',
        status: 'running',
        startedAt: '2026-06-13T10:00:00.050Z',
        items: [
          {
            id: 'server-user-existing',
          clientUserMessageId: 'existing',
            type: 'userMessage',
            status: 'completed',
            payload: { text: 'already here' },
            createdAt: '2026-06-13T10:00:00.050Z'
          }
        ]
      })
    ])
    s().addOptimisticTurn({
      id: 'local-turn-existing',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-existing',
          clientUserMessageId: 'existing',
          type: 'userMessage',
          status: 'completed',
          text: 'already here',
          createdAt: '2026-06-13T10:00:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:00.000Z'
    })

    s().promoteOptimisticTurn('local-turn-existing', 'turn-server-existing')

    const state = s()
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-server-existing')
    expect(state.activeTurnId).toBe('turn-server-existing')
    expect(state.turns[0].items.filter((item) => item.type === 'userMessage')).toHaveLength(1)
  })

  it('keeps a newer same-text optimistic turn when the snapshot only has older history', () => {
    s().addOptimisticTurn({
      id: 'local-turn-repeat',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-repeat',
          clientUserMessageId: 'repeat',
          type: 'userMessage',
          status: 'completed',
          text: 'repeat',
          createdAt: '2026-06-13T10:05:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:05:00.000Z'
    })

    s().setTurns([
      makeTurn({
        id: 'turn-old-repeat',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: '2026-06-13T10:00:00.000Z',
        completedAt: '2026-06-13T10:00:01.000Z',
        items: [
          {
            id: 'server-user-old-repeat',
          clientUserMessageId: 'old-repeat',
            type: 'userMessage',
            status: 'completed',
            payload: { text: 'repeat' },
            createdAt: '2026-06-13T10:00:00.000Z'
          }
        ]
      })
    ], { preserveExistingRealtime: true })

    expect(s().turns.map((turn) => turn.id)).toEqual(['turn-old-repeat', 'local-turn-repeat'])
  })

  it('keeps same-text terminal history inside clock skew when it completed before the optimistic turn', () => {
    s().addOptimisticTurn({
      id: 'local-turn-skew-repeat',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-skew-repeat',
          clientUserMessageId: 'skew-repeat',
          type: 'userMessage',
          status: 'completed',
          text: 'ok',
          createdAt: '2026-06-13T10:00:01.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:01.000Z'
    })

    s().setTurns([
      makeTurn({
        id: 'turn-old-skew-repeat',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: '2026-06-13T10:00:00.500Z',
        completedAt: '2026-06-13T10:00:00.900Z',
        items: [
          {
            id: 'server-user-old-skew-repeat',
          clientUserMessageId: 'old-skew-repeat',
            type: 'userMessage',
            status: 'completed',
            payload: { text: 'ok' },
            createdAt: '2026-06-13T10:00:00.500Z'
          }
        ]
      })
    ], { preserveExistingRealtime: true })

    expect(s().turns.map((turn) => turn.id)).toEqual([
      'turn-old-skew-repeat',
      'local-turn-skew-repeat'
    ])
  })

  it('matches the submission identity regardless of server clock skew', () => {
    s().addOptimisticTurn({
      id: 'local-turn-skew-cancel',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-skew-cancel',
          clientUserMessageId: 'skew-cancel',
          type: 'userMessage',
          status: 'completed',
          text: 'cancel fast',
          createdAt: '2026-06-13T10:00:01.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:01.000Z'
    })

    s().setTurns([
      makeTurn({
        id: 'turn-server-skew-cancel',
        threadId: 'thread-1',
        status: 'cancelled',
        startedAt: '2026-06-13T10:00:00.500Z',
        completedAt: '2026-06-13T10:00:01.200Z',
        items: [
          {
            id: 'server-user-skew-cancel',
          clientUserMessageId: 'skew-cancel',
            type: 'userMessage',
            status: 'completed',
            payload: { text: 'cancel fast' },
            createdAt: '2026-06-13T10:00:00.500Z'
          }
        ]
      })
    ], { preserveExistingRealtime: true })

    const state = s()
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-server-skew-cancel')
    expect(state.turns[0].status).toBe('cancelled')
    expect(state.turns[0].items.filter((item) => item.type === 'userMessage')).toHaveLength(1)
  })

  it('promoteOptimisticTurn does not change activeTurnId if it was already replaced', () => {
    // Simulate race: turn/started arrived before turn/start response and already updated activeTurnId
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-999',
      threadId: 'thread-1',
      status: 'running',
      items: [],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)
    s().onTurnStarted(makeTurn({ id: 'turn_server_xyz' }))

    s().promoteOptimisticTurn('local-turn-999', 'turn_server_from_response')
    expect(s().activeTurnId).toBe('turn_server_xyz')
  })

  it('Scenario B: onTurnStarted does not create duplicate when promoteOptimisticTurn ran first', () => {
    // Scenario B: RPC response arrives BEFORE the turn/started notification
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-456',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-item-1',
          clientUserMessageId: 'initial',
          type: 'userMessage',
          status: 'completed',
          text: 'Hello',
          createdAt: new Date().toISOString()
        }
      ],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)
    expect(s().turns).toHaveLength(1)
    expect(s().activeTurnId).toBe('local-turn-456')

    s().promoteOptimisticTurn('local-turn-456', 'turn_001')
    expect(s().turns).toHaveLength(1)
    expect(s().turns[0].id).toBe('turn_001')
    expect(s().activeTurnId).toBe('turn_001')

    s().onTurnStarted(makeTurn({ id: 'turn_001' }))

    const state = s()
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn_001')
    expect(state.turns[0].items[0].text).toBe('Hello')
    expect(state.turnStatus).toBe('running')
    expect(state.activeTurnId).toBe('turn_001')
  })

  it('Scenario A: onTurnStarted notification arrives before RPC response (existing happy path)', () => {
    // Scenario A: notification arrives before RPC response — existing behaviour
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-789',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-item-2',
          type: 'userMessage',
          status: 'completed',
          text: 'Hi there',
          createdAt: new Date().toISOString()
        }
      ],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)

    s().onTurnStarted(makeTurn({ id: 'turn_002' }))
    s().promoteOptimisticTurn('local-turn-789', 'turn_002')
    expect(s().turns).toHaveLength(1)
    expect(s().turns[0].id).toBe('turn_002')
    expect(s().turns[0].items[0].text).toBe('Hi there')

    s().promoteOptimisticTurn('local-turn-789', 'turn_002')
    expect(s().turns).toHaveLength(1)
    expect(s().activeTurnId).toBe('turn_002')
  })

  it('Scenario A: onTurnStarted replaces matching optimistic user message with canonical server item', () => {
    s().addOptimisticTurn({
      id: 'local-turn-canonical-first',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-canonical-first',
          clientUserMessageId: 'canonical-first',
          type: 'userMessage',
          status: 'completed',
          text: 'notification first',
          nativeInputParts: [{ type: 'text', text: 'notification first' }],
          createdAt: '2026-06-13T10:00:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:00.000Z'
    })

    s().onTurnStarted(makeTurn({
      id: 'turn-canonical-first',
      threadId: 'thread-1',
      startedAt: '2026-06-13T10:00:00.050Z',
      items: [
        {
          id: 'server-user-canonical-first',
          clientUserMessageId: 'canonical-first',
          type: 'userMessage',
          status: 'completed',
          payload: {
            text: 'notification first',
            nativeInputParts: [{ type: 'text', text: 'notification first' }]
          },
          createdAt: '2026-06-13T10:00:00.050Z'
        }
      ]
    }))
    s().promoteOptimisticTurn('local-turn-canonical-first', 'turn-canonical-first')

    const userMessages = s().turns[0].items.filter((item) => item.type === 'userMessage')
    expect(s().turns).toHaveLength(1)
    expect(s().turns[0].id).toBe('turn-canonical-first')
    expect(userMessages).toHaveLength(1)
    expect(userMessages[0].id).toBe('server-user-canonical-first')
  })
})


it('correlates page captures independently of image count and keeps identical subsequent submissions', () => {
  const { inputParts } = buildComposerInputParts({ text: '', contexts: [{ id: 'page', kind: 'pageReference', url: 'https://example.test', title: 'Page', selectionKind: 'region', text: '', comment: 'Update', image: { tempPath: '/page.png', fileName: 'page.png', mimeType: 'image/png' } }] })
  const local = createOptimisticUserMessage(inputParts, '', 'first')
  const turn = { id: 'local-turn-first', threadId: 'thread-1', status: 'running' as const, startedAt: local.createdAt, items: [local] }
  s().addOptimisticTurn(turn)
  const server = makeTurn({ id: 'server-turn', items: [{ ...local, id: 'server-user', images: [{ path: '/page.png' }] }] })
  s().onTurnStarted(server)
  s().promoteOptimisticTurn(turn.id, 'server-turn')
  s().onItemCompleted({ turnId: 'server-turn', item: (server.items as object[])[0] })
  expect(s().turns).toHaveLength(1)
  expect(s().turns[0].items).toHaveLength(1)
  s().addOptimisticTurn({ ...turn, id: 'local-turn-second', items: [createOptimisticUserMessage(inputParts, '', 'second')] })
  s().setTurns([server], { preserveExistingRealtime: true, realtimeScopeThreadId: 'thread-1' })
  expect(s().turns.flatMap(t => t.items)).toHaveLength(2)
})

it.each(['onItemStarted', 'onItemCompleted', 'onTurnStarted'] as const)('reconciles %s before the start response after an empty started event', (event) => {
  const local = createOptimisticUserMessage([{ type: 'text', text: 'Hello' }], 'Hello', 'early-event')
  const localTurn = { id: 'local-turn-early-event', threadId: 'thread-1', status: 'running' as const, startedAt: local.createdAt, items: [local] }
  s().addOptimisticTurn(localTurn)
  s().onTurnStarted(makeTurn({ id: 'server-turn', items: [] }))
  const serverItem = { ...local, id: 'server-user' }
  const deliver = () => event === 'onTurnStarted'
    ? s().onTurnStarted(makeTurn({ id: 'server-turn', items: [serverItem] }))
    : s()[event]({ turnId: 'server-turn', item: serverItem })
  deliver()
  expect(s().turns).toHaveLength(1)
  expect(s().turns[0].items.map(item => item.id)).toEqual(['server-user'])
  deliver()
  s().promoteOptimisticTurn(localTurn.id, 'server-turn')
  expect(s().turns).toHaveLength(1)
  expect(s().turns[0].items.map(item => item.id)).toEqual(['server-user'])
})
