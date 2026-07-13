import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { ChatGptUsageBadge } from '../components/conversation/ChatGptUsageBadge'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useProvidersStore, type ProviderSummary } from '../stores/providersStore'

const provider: ProviderSummary = {
  id: 'openai',
  displayName: 'OpenAI (ChatGPT)',
  protocol: 'openai-responses',
  authMethod: 'chatgptOAuth',
  chatGptAccountId: 'acct_test',
  chatGptPlanType: 'pro'
}

describe('ChatGptUsageBadge', () => {
  beforeEach(() => {
    useProvidersStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
      }
    })
  })

  it('shows only weekly usage when the weekly window occupies the primary slot', async () => {
    useProvidersStore.setState({
      chatGptUsage: {
        available: true,
        planType: 'pro',
        primary: {
          usedPercent: 2,
          windowSeconds: 604_800,
          resetAt: '2099-01-07T00:00:00.000Z'
        },
        secondary: null,
        credits: null,
        limitReachedKind: null,
        fetchedAt: '2026-07-13T05:00:00.000Z'
      }
    })

    renderBadge()

    const badge = screen.getByRole('button', { name: /ChatGPT.*98% left this week/i })
    expect(badge).not.toHaveAccessibleName(/5h window/i)

    fireEvent.mouseEnter(badge.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('98% wk')
    expect(screen.getByRole('tooltip')).not.toHaveTextContent('5h')

    fireEvent.click(badge)

    expect(screen.getByRole('dialog', { name: 'ChatGPT' })).toBeInTheDocument()
    expect(screen.getByText('Weekly')).toBeInTheDocument()
    expect(screen.queryByText('Session')).not.toBeInTheDocument()
    expect(screen.queryByText('—')).not.toBeInTheDocument()
  })

  it('shows the unavailable state instead of empty window placeholders', () => {
    useProvidersStore.setState({
      chatGptUsage: {
        available: true,
        planType: 'pro',
        primary: null,
        secondary: null,
        credits: null,
        limitReachedKind: null,
        fetchedAt: '2026-07-13T05:00:00.000Z'
      }
    })

    renderBadge()
    fireEvent.click(screen.getByRole('button', { name: /ChatGPT.*Pro/i }))

    expect(screen.getByText("Couldn't fetch usage. The desktop will retry shortly.")).toBeInTheDocument()
    expect(screen.queryByText('Session')).not.toBeInTheDocument()
    expect(screen.queryByText('Weekly')).not.toBeInTheDocument()
  })
})

function renderBadge(): void {
  render(
    <LocaleProvider>
      <ChatGptUsageBadge provider={provider} />
    </LocaleProvider>
  )
}
