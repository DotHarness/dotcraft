import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { ProviderUsageBadge } from '../components/conversation/ProviderUsageBadge'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { useProvidersStore, type ProviderSummary, type ProviderUsageSnapshot } from '../stores/providersStore'
import { installDesktopApiMock } from './desktopApiMock'

const provider: ProviderSummary = {
  id: 'openai',
  displayName: 'OpenAI (ChatGPT)',
  protocol: 'openai-responses',
  authMethod: 'chatgptOAuth'
}

function usage(overrides: Partial<ProviderUsageSnapshot> = {}): ProviderUsageSnapshot {
  return {
    available: true,
    planType: 'pro',
    primary: null,
    secondary: null,
    credits: null,
    limitReachedKind: null,
    fetchedAt: '2026-07-13T05:00:00.000Z',
    ...overrides
  }
}

describe('ProviderUsageBadge', () => {
  beforeEach(() => {
    useProvidersStore.getState().reset()
    useConnectionStore.setState({ capabilities: {} })
    installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) } })
  })

  it('reads usage for the active provider and follows only its usage updates', async () => {
    let notify: ((payload: { method: string; params: unknown }) => void) | null = null
    const sendRequest = vi.fn(async () => ({ available: false }))
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: {
        sendRequest,
        onNotification: (callback: (payload: { method: string; params: unknown }) => void) => {
          notify = callback
          return () => { notify = null }
        }
      }
    })
    useConnectionStore.setState({ capabilities: { providerManagement: true } })

    renderBadge()

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('provider/auth/usage', { providerId: 'openai' }, 30_000))
    expect(screen.queryByRole('button')).not.toBeInTheDocument()

    const window = { usedPercent: 40, windowSeconds: 604_800, resetAt: '2099-01-07T00:00:00.000Z' }
    act(() => notify?.({ method: 'provider/auth/usage/updated', params: { providerId: 'other', ...usage({ primary: window }) } }))
    expect(screen.queryByRole('button')).not.toBeInTheDocument()

    act(() => notify?.({ method: 'provider/auth/usage/updated', params: { providerId: 'OpenAI', ...usage({ primary: window }) } }))
    expect(await screen.findByRole('button', { name: /60% left this week/i })).toBeInTheDocument()
  })
})

function renderBadge(): void {
  render(
    <LocaleProvider>
      <ProviderUsageBadge provider={provider} />
    </LocaleProvider>
  )
}
