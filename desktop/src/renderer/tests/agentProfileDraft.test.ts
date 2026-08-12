import { describe, expect, it } from 'vitest'
import { parseProfile, toMarkdown, type ProfileDraft } from '../components/agents/agentProfileDraft'

describe('agent profile draft avatar metadata', () => {
  it('round-trips avatar frontmatter', () => {
    const draft: ProfileDraft = {
      name: 'avatar-bot',
      description: 'Uses a persisted avatar',
      avatar: { palette: 6, face: 1, accessory: 2 },
      providerPreference: null,
      tools: { mode: 'all', allow: [], deny: [], agentControl: 'full' },
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
name: inherited-bot
description: Inherited profile
---

Inherited body.
`)

    expect(parsed.avatar).toBeUndefined()
    expect(parsed.providerPreference).toBeNull()
  })

  it('round-trips a provider model preset', () => {
    const draft = createDraftWithProviderPreference()
    const markdown = toMarkdown(draft)

    expect(markdown).toContain(`providerPreference:
  providerId: openai
  model: gpt-5.6
  reasoning:
    enabled: true
    effort: ultra
  speed: fast
  contextWindow:
    mode: max`)
    expect(parseProfile(markdown).providerPreference).toEqual(draft.providerPreference)
  })

  it('does not interpret the removed model and reasoning fields', () => {
    const parsed = parseProfile(`---
name: old-shape
model: gpt-old
reasoning:
  effort: high
---
`)

    expect(parsed.providerPreference).toBeNull()
  })

  it('does not accept a partial or mode-based provider preference', () => {
    const partial = parseProfile(`---
name: partial
providerPreference:
  providerId: openai
  model: gpt-5.6
---
`)
    const modeBased = parseProfile(`---
name: mode-based
providerPreference:
  mode: pinned
  preference:
    providerId: openai
    model: gpt-5.6
---
`)

    expect(partial.providerPreference).toBeNull()
    expect(modeBased.providerPreference).toBeNull()
  })

  it('rejects the removed reasoning output field', () => {
    const parsed = parseProfile(`---
name: removed-output
providerPreference:
  providerId: openai
  model: gpt-5.6
  reasoning:
    enabled: true
    effort: high
    output: full
  speed: fast
  contextWindow:
    mode: max
---
`)

    expect(parsed.providerPreference).toBeNull()
  })

  it.each([
    ['all', '', 'all'],
    ['allowList', 'tools:\n  allow: []', 'allowList'],
    ['denyList', 'tools:\n  deny: []', 'denyList']
  ] as const)('round-trips the %s tool policy mode', (mode, expectedYaml, expectedMode) => {
    const draft = createDraftWithProviderPreference()
    draft.tools.mode = mode

    const markdown = toMarkdown(draft)

    if (expectedYaml) expect(markdown).toContain(expectedYaml)
    else expect(markdown).not.toContain('tools:')
    expect(parseProfile(markdown).tools.mode).toBe(expectedMode)
  })
})

function createDraftWithProviderPreference(): ProfileDraft {
  return {
    name: 'pinned-bot',
    description: 'Pinned',
    providerPreference: {
      providerId: 'openai',
      model: 'gpt-5.6',
      reasoning: { enabled: true, effort: 'ultra' },
      speed: 'fast',
      contextWindow: { mode: 'max' }
    },
    tools: { mode: 'all', allow: [], deny: [], agentControl: 'full' },
    mcp: { servers: [], toolsAllow: [], toolsDeny: [] },
    skills: { preload: [], allow: [], deny: [] },
    permissions: { approvalPolicy: 'default', requireApprovalOutsideWorkspace: false },
    roleInstructions: ''
  }
}
