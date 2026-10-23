import { beforeEach, describe, expect, it } from 'vitest'
import {
  buildStreamingPlanDraft,
  selectStreamingPlanDraft,
  useConversationStore
} from '../stores/conversationStore'
import { coreToolFields, streamingToolCallStarted } from './testToolPresentation'

describe('selectStreamingPlanDraft', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
  })

  it('returns a partial plan draft from single-field Markdown while CreatePlan arguments stream', () => {
    const store = useConversationStore.getState()

    store.setTurns([
      {
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'running',
        items: [],
        startedAt: new Date().toISOString()
      }
    ])
    store.onItemStarted(streamingToolCallStarted('turn-1', 'item-plan-1', 'CreatePlan', 'call-1'))
    store.onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'item-plan-1',
      delta: '{"plan":"# Ship feature X\\n\\n## 概览\\n\\nStep',
      toolName: 'CreatePlan',
      callId: 'call-1'
    })
    store.onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'item-plan-1',
      delta: ' one\\n\\n## Changes\\n\\n- Do it',
      toolName: 'CreatePlan',
      callId: 'call-1'
    })

    const draft = selectStreamingPlanDraft(useConversationStore.getState())
    expect(draft).not.toBeNull()
    expect(draft?.itemId).toBe('item-plan-1')
    expect(draft?.title).toBe('Ship feature X')
    expect(draft?.overview).toBe('Step one')
    expect(draft?.plan).toContain('# Ship feature X')
    expect(draft?.content).toContain('## 概览')
  })

  it('extracts closed todo objects from the streaming todos array', () => {
    const store = useConversationStore.getState()
    store.setTurns([
      {
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'running',
        items: [],
        startedAt: new Date().toISOString()
      }
    ])

    const slash = String.fromCharCode(92)
    store.onItemStarted(streamingToolCallStarted('turn-1', 'item-plan-2', 'CreatePlan', 'call-2'))
    store.onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'item-plan-2',
      delta: `{"plan":"# X","todos":[{"id":"t1","content":"${slash}u4fee${slash}u590d${slash}u6d41${slash}u5f0f${slash}u8ba1${slash}u5212","status":"pending"},{"id":"t2","content":"do B",`,
      toolName: 'CreatePlan',
      callId: 'call-2'
    })

    const draft = selectStreamingPlanDraft(useConversationStore.getState())
    expect(draft?.todos).toHaveLength(1)
    expect(draft?.todos[0]).toEqual({ id: 't1', content: '修复流式计划', status: 'pending' })
  })

  it('stops returning a draft once the tool call completes', () => {
    const store = useConversationStore.getState()
    store.setTurns([
      {
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'running',
        items: [],
        startedAt: new Date().toISOString()
      }
    ])

    expect(selectStreamingPlanDraft(useConversationStore.getState())).toBeNull()
    store.onItemStarted(streamingToolCallStarted('turn-1', 'item-plan-3', 'CreatePlan', 'call-3'))
    store.onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'item-plan-3',
      delta: '{"plan":"# Y"',
      toolName: 'CreatePlan',
      callId: 'call-3'
    })

    expect(selectStreamingPlanDraft(useConversationStore.getState())).not.toBeNull()

    store.onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'item-plan-3',
        type: 'toolCall',
        status: 'completed',
        toolCallId: 'call-3',
        turnId: 'turn-1',
        payload: {
          toolName: 'CreatePlan', ...coreToolFields('CreatePlan'),
          arguments: { plan: '# Y' }
        }
      }
    })

    expect(selectStreamingPlanDraft(useConversationStore.getState())).toBeNull()
  })

  it('falls back to legacy title and overview fields when plan is absent', () => {
    const draft = buildStreamingPlanDraft(
      'item-legacy',
      '{"title":"Legacy Plan","overview":"Legacy overview"'
    )

    expect(draft.title).toBe('Legacy Plan')
    expect(draft.overview).toBe('Legacy overview')
    expect(draft.plan).toBeNull()
    expect(draft.content).toBeNull()
  })
})
