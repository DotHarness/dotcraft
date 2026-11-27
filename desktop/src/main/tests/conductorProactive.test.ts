import { describe, expect, it } from 'vitest'
import type { LinkedChat } from '../threadTasks/linkedChatStore'
import {
  buildProactiveSignal,
  isSilentProactiveReply
} from '../threadTasks/conductorProactive'

function linked(overrides: Partial<LinkedChat> & { threadId: string; projectId?: string }): LinkedChat {
  return {
    parent: { threadId: 'conductor-1', projectId: 'chats', workspacePath: '/chats' },
    child: {
      threadId: overrides.threadId,
      projectId: overrides.projectId ?? 'dotcraft',
      projectName: overrides.projectId ?? 'dotcraft',
      workspacePath: `/${overrides.projectId ?? 'dotcraft'}`,
      title: `Chat ${overrides.threadId}`
    },
    status: overrides.status ?? 'idle',
    ...(overrides.waitingFor ? { waitingFor: overrides.waitingFor } : {}),
    createdAt: '2026-11-27T00:00:00.000Z'
  }
}

describe('conductorProactive', () => {
  it('treats empty and DONT_NOTIFY replies as silent', () => {
    expect(isSilentProactiveReply('')).toBe(true)
    expect(isSilentProactiveReply('   ')).toBe(true)
    expect(isSilentProactiveReply('DONT_NOTIFY')).toBe(true)
    expect(isSilentProactiveReply(' `DONT_NOTIFY` ')).toBe(true)
    expect(isSilentProactiveReply('One chat is waiting on approval.')).toBe(false)
  })

  it('stays silent when no active or changed chats exist and ignores conductor itself', () => {
    const empty = buildProactiveSignal([], [], 'conductor-1')
    expect(empty.noteworthy).toBe(false)

    const onlySelf = buildProactiveSignal(
      [{ id: 'conductor-1', projectId: 'chats', activeState: 'working' }],
      [],
      'conductor-1'
    )
    expect(onlySelf.noteworthy).toBe(false)

    const onlyIdleLinked = buildProactiveSignal([], [linked({ threadId: 't-1', status: 'idle' })], 'conductor-1')
    expect(onlyIdleLinked.noteworthy).toBe(false)
  })

  it('flags unfollowed active chats and waiting linked chats with deterministic fingerprints', () => {
    const unfollowed = buildProactiveSignal(
      [{ id: 't-active', projectId: 'dotcraft', activeState: 'working' }],
      [],
      'conductor-1'
    )
    expect(unfollowed.noteworthy).toBe(true)

    const waitingA = buildProactiveSignal(
      [],
      [linked({ threadId: 't-wait', status: 'waitingForInput', waitingFor: 'approval' })],
      'conductor-1'
    )
    const waitingB = buildProactiveSignal(
      [],
      [linked({ threadId: 't-wait', status: 'waitingForInput', waitingFor: 'approval' })],
      'conductor-1'
    )
    expect(waitingA.noteworthy).toBe(true)
    expect(waitingA.fingerprint).toBe(waitingB.fingerprint)
  })
})
