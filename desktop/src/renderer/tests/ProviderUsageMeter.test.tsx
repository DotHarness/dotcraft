import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { ProviderUsageMeter } from '../components/settings/providers/ProviderUsageMeter'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useProvidersStore } from '../stores/providersStore'

afterEach(() => {
  cleanup()
  useProvidersStore.getState().reset()
})

it('dismisses usage with Escape while the trigger keeps focus without forwarding keys to the provider row', () => {
  useProvidersStore.getState().applyUsage('provider', {
    available: true,
    planType: null,
    primary: null,
    secondary: null,
    credits: null,
    balances: [{ currency: 'USD', total: '10', granted: '0', toppedUp: '10' }],
    limitReachedKind: null,
    fetchedAt: null,
    pluginId: null
  })
  const rowKeyDown = vi.fn()
  render(
    <LocaleProvider loadSettings={false}>
      <div onKeyDown={rowKeyDown}>
        <ProviderUsageMeter provider={{
          id: 'provider', displayName: 'Provider', protocol: 'openai', hasApiKey: true, endPoint: ''
        }} />
      </div>
    </LocaleProvider>
  )
  const trigger = screen.getByRole('button', { expanded: false })
  trigger.focus()
  fireEvent.click(trigger)
  expect(screen.getByRole('dialog')).toBeTruthy()

  fireEvent.keyDown(trigger, { key: 'Escape' })

  expect(screen.queryByRole('dialog')).toBeNull()
  expect(trigger.getAttribute('aria-expanded')).toBe('false')
  expect(document.activeElement).toBe(trigger)
  expect(rowKeyDown).not.toHaveBeenCalled()
})
