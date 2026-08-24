import { describe, expect, it } from 'vitest'
import { buildWelcomeThreadConfiguration } from '../utils/welcomeThreadConfiguration'

const reasoning = {
  enabled: true,
  effort: 'high' as const,
  output: 'summary' as const
}

describe('buildWelcomeThreadConfiguration', () => {
  it('keeps an untouched approval choice inherited while snapshotting Welcome settings', () => {
    const config = buildWelcomeThreadConfiguration({
      mode: 'plan',
      providerId: 'openai',
      model: 'gpt-test',
      reasoning,
      speed: 'fast',
      contextWindow: { mode: 'max' },
      approvalPolicy: 'autoApprove',
      approvalPolicyExplicit: false
    })

    expect(config).toEqual({
      mode: 'plan',
      providerId: 'openai',
      model: 'gpt-test',
      reasoning,
      speed: 'fast',
      contextWindow: { mode: 'max' }
    })
    expect(config).not.toHaveProperty('approvalPolicy')
  })

  it('writes an explicit per-thread approval override', () => {
    const config = buildWelcomeThreadConfiguration({
      mode: 'agent',
      reasoning,
      approvalPolicy: 'prompt',
      approvalPolicyExplicit: true
    })

    expect(config.approvalPolicy).toBe('prompt')
  })

  it('creates a Profile-backed thread without overriding Profile-owned policy', () => {
    const config = buildWelcomeThreadConfiguration({
      mode: 'plan',
      providerId: 'openai',
      model: 'gpt-test',
      reasoning,
      speed: 'standard',
      approvalPolicy: 'autoApprove',
      approvalPolicyExplicit: true,
      agentProfileId: 'reviewer'
    })

    expect(config).toMatchObject({
      agentProfileId: 'reviewer',
      providerId: 'openai',
      model: 'gpt-test',
      reasoning,
      speed: 'standard'
    })
    expect(config).not.toHaveProperty('mode')
    expect(config).not.toHaveProperty('approvalPolicy')
  })
})
