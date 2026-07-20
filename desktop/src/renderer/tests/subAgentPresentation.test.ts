import { describe, expect, it } from 'vitest'
import {
  getSubAgentAccent,
  getSubAgentIdentitySeed
} from '../utils/subAgentPresentation'

describe('SubAgent identity presentation', () => {
  it('keeps the accent stable across surfaces by preferring the agent path', () => {
    const conversationSeed = getSubAgentIdentitySeed({
      agentPath: '/root/reviewer',
      nickname: 'Reviewer'
    })
    const detailPanelSeed = getSubAgentIdentitySeed({
      agentPath: '/root/reviewer',
      childThreadId: 'thread_reviewer',
      nickname: 'Reviewer'
    })

    expect(conversationSeed).toBe('/root/reviewer')
    expect(detailPanelSeed).toBe('/root/reviewer')
    expect(getSubAgentAccent(conversationSeed)).toBe(getSubAgentAccent(detailPanelSeed))
  })

  it('falls back to child thread id and then nickname for historical entries', () => {
    expect(getSubAgentIdentitySeed({ childThreadId: 'thread_reviewer', nickname: 'Reviewer' }))
      .toBe('thread_reviewer')
    expect(getSubAgentIdentitySeed({ nickname: 'Reviewer' })).toBe('Reviewer')
  })
})
