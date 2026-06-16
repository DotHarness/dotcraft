import { describe, expect, it } from 'vitest'
import { parseProfile, toMarkdown, type ProfileDraft } from '../components/agents/agentProfileDraft'

describe('agent profile draft avatar metadata', () => {
  it('round-trips avatar frontmatter', () => {
    const draft: ProfileDraft = {
      name: 'avatar-bot',
      description: 'Uses a persisted avatar',
      avatar: { palette: 6, face: 1, accessory: 2 },
      model: 'inherit',
      reasoningEffort: 'medium',
      tools: { allow: [], deny: [], agentControl: 'full' },
      mcp: { servers: [], toolsAllow: [], toolsDeny: [] },
      skills: { preload: [], allow: [], deny: [] },
      permissions: { approvalPolicy: 'default', requireApprovalOutsideWorkspace: false },
      roleInstructions: 'Avatar body.'
    }

    const markdown = toMarkdown(draft)
    const parsed = parseProfile(markdown)

    expect(markdown).toContain('avatar: 278')
    expect(parsed.avatar).toEqual(draft.avatar)
  })

  it('leaves profiles without avatar metadata unset', () => {
    const parsed = parseProfile(`---
name: legacy-bot
description: Legacy profile
model: inherit
---

Legacy body.
`)

    expect(parsed.avatar).toBeUndefined()
  })
})
