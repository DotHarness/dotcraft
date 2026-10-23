import { describe, expect, it } from 'vitest'
import { buildWelcomeThreadConfiguration } from '../utils/welcomeThreadConfiguration'

const reasoning = {
  enabled: true,
  effort: 'high' as const,
  output: 'summary' as const
}

describe('buildWelcomeThreadConfiguration', () => {
  it('snapshots Welcome settings, writing an approval override only when chosen explicitly', () => {
    const config = buildWelcomeThreadConfiguration({
      mode: 'plan',
      providerId: 'openai',
      model: 'gpt-test',
      reasoning,
      speed: 'fast',
      approvalPolicy: 'autoApprove',
      approvalPolicyExplicit: false
    })

    expect(config).toEqual({
      mode: 'plan',
      providerId: 'openai',
      model: 'gpt-test',
      reasoning,
      speed: 'fast',
    })
    expect(config).not.toHaveProperty('approvalPolicy')

    const explicit = buildWelcomeThreadConfiguration({
      mode: 'agent',
      reasoning,
      approvalPolicy: 'prompt',
      approvalPolicyExplicit: true
    })

    expect(explicit.approvalPolicy).toBe('prompt')
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
