import { describe, expect, it, vi } from 'vitest'
import { loginSetupChatGpt } from '../workspaceSetup'

const settings = {} as Parameters<typeof loginSetupChatGpt>[1]

describe('setup ChatGPT login bridge', () => {
  it('authorizes without binding a provider before final setup submission', async () => {
    const runBackend = vi.fn().mockResolvedValue({ kind: 'success' })
    expect(await loginSetupChatGpt('subscription', settings, runBackend)).toEqual({ kind: 'success' })
    expect(runBackend).toHaveBeenCalledWith(
      ['auth', 'openai', 'login', '--provider-id', 'subscription', '--no-bind'], undefined, 15 * 60_000)
  })

  it('preserves backend failure details for a retryable sign-in prompt', async () => {
    const runBackend = vi.fn().mockRejectedValue(new Error('Authorization cancelled'))
    expect(await loginSetupChatGpt('subscription', settings, runBackend)).toEqual({
      kind: 'error', errorCode: 'openai_login_failed', errorMessage: 'Authorization cancelled'
    })
  })
})
