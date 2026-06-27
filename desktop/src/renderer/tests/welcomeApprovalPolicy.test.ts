import { describe, expect, it } from 'vitest'
import { welcomeApprovalPolicyToWrite } from '../stores/welcomeApprovalPolicy'

describe('welcomeApprovalPolicyToWrite', () => {
  // Regression: selecting "Ask for approval" on the Welcome composer must survive the
  // thread handoff. Previously `prompt` was normalized to `default`, so in a workspace
  // whose default is full access the new thread auto-approved despite the explicit
  // choice to be prompted.
  it('preserves an explicit prompt choice', () => {
    expect(welcomeApprovalPolicyToWrite('prompt')).toBe('prompt')
  })

  it('preserves an explicit full-access choice', () => {
    expect(welcomeApprovalPolicyToWrite('autoApprove')).toBe('autoApprove')
  })

  it('does not write when the choice is the workspace default', () => {
    expect(welcomeApprovalPolicyToWrite('default')).toBeUndefined()
    expect(welcomeApprovalPolicyToWrite(undefined)).toBeUndefined()
  })
})
