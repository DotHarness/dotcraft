import { describe, expect, it } from 'vitest'
import {
  getSubAgentAccent,
  getSubAgentIdentitySeed
} from '../utils/subAgentPresentation'

describe('SubAgent identity presentation', () => {
  it('keeps the accent stable across surfaces from the visible agent name', () => {
    const conversationSeed = getSubAgentIdentitySeed({
      agentPath: '/root/reviewer',
      nickname: 'Reviewer'
    })
    const detailPanelSeed = getSubAgentIdentitySeed({
      agentPath: '/root/reviewer',
      childThreadId: 'thread_reviewer',
      nickname: 'Reviewer'
    })

    expect(conversationSeed).toBe('Reviewer')
    expect(detailPanelSeed).toBe('Reviewer')
    expect(getSubAgentAccent(conversationSeed)).toBe(getSubAgentAccent(detailPanelSeed))
  })

  it('never hashes an internal thread id when no visible nickname exists', () => {
    expect(getSubAgentIdentitySeed({ childThreadId: 'thread_reviewer', nickname: 'Reviewer' }))
      .toBe('Reviewer')
    expect(getSubAgentIdentitySeed({ childThreadId: 'thread_reviewer' })).toBeNull()
    expect(getSubAgentIdentitySeed({ nickname: 'Reviewer' })).toBe('Reviewer')
  })
})
